using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MUTASSIM.ToolPro.App.Services;
using MUTASSIM.ToolPro.Core.Configuration;
using MUTASSIM.ToolPro.Core.Operations;
using MUTASSIM.ToolPro.Firmware;

namespace MUTASSIM.ToolPro.App.ViewModels;

public enum FirmwareFileState
{
    Pending,
    Hashing,
    Hashed,
    Verified,
    Mismatch,
    Failed,
    Cancelled
}

public sealed partial class FirmwareFileItem : ObservableObject
{
    public FirmwareFileItem(string path)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path);
        SizeBytes = new FileInfo(path).Length;
    }

    public string Path { get; }
    public string Name { get; }
    public long SizeBytes { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHash))]
    private string? _sha256;
    [ObservableProperty] private string? _expectedHash;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private FirmwareFileState _state = FirmwareFileState.Pending;
    [ObservableProperty] private string? _message;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    public bool IsIdle => !IsBusy;
    public bool HasHash => !string.IsNullOrEmpty(Sha256);
}

/// <summary>Phase 1 Firmware Manager: add files (browse / drag &amp; drop) and verify them with real SHA-256.</summary>
public sealed partial class FirmwareManagerViewModel(
    IOperationTracker tracker,
    ISettingsService settings,
    IDialogService dialogs,
    ILogger<FirmwareManagerViewModel> logger) : ObservableObject, IDisposable
{
    private CancellationTokenSource _cts = new();

    public ObservableCollection<FirmwareFileItem> Files { get; } = [];

    [ObservableProperty] private bool _isWorking;

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var files = dialogs.OpenFiles(LocalizationService.Get("Firmware.FileFilter"), settings.Current.LastFirmwareFolder);
        if (files.Length == 0) return;
        AddFiles(files);

        var folder = System.IO.Path.GetDirectoryName(files[0]);
        if (folder is not null && folder != settings.Current.LastFirmwareFolder)
        {
            var updated = settings.Current.Clone();
            updated.LastFirmwareFolder = folder;
            await settings.SaveAsync(updated);
        }
    }

    /// <summary>Called by the view for Drag &amp; Drop and by Browse. Folders are expanded (top level only).</summary>
    public void AddFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    AddFiles(Directory.EnumerateFiles(path));
                    continue;
                }

                if (!File.Exists(path)) continue;
                if (Files.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
                Files.Add(new FirmwareFileItem(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Cannot add {Path}", path);
                dialogs.Error(LocalizationService.Format("Firmware.CannotAdd", path, ex.Message));
            }
        }
    }

    [RelayCommand]
    private async Task HashAsync(FirmwareFileItem? item)
    {
        if (item is null || item.IsBusy) return;
        await HashOneAsync(item, _cts.Token);
    }

    [RelayCommand]
    private async Task HashAllAsync()
    {
        IsWorking = true;
        try
        {
            foreach (var item in Files.Where(f => !f.IsBusy).ToArray())
            {
                if (_cts.IsCancellationRequested) break;
                await HashOneAsync(item, _cts.Token);
            }
        }
        finally
        {
            IsWorking = false;
            if (_cts.IsCancellationRequested) ResetCancellation();
        }
    }

    [RelayCommand]
    private void Cancel() => _cts.Cancel();

    [RelayCommand]
    private void Remove(FirmwareFileItem? item)
    {
        if (item is { IsBusy: false }) Files.Remove(item);
    }

    [RelayCommand]
    private void Clear()
    {
        foreach (var item in Files.Where(f => !f.IsBusy).ToArray()) Files.Remove(item);
    }

    [RelayCommand]
    private void CopyHash(FirmwareFileItem? item)
    {
        if (item?.Sha256 is { } hash) dialogs.CopyToClipboard(hash);
    }

    [RelayCommand]
    private void OpenFolder(FirmwareFileItem? item)
    {
        if (item is not null && System.IO.Path.GetDirectoryName(item.Path) is { } dir) dialogs.OpenInExplorer(dir);
    }

    private async Task HashOneAsync(FirmwareFileItem item, CancellationToken ct)
    {
        item.IsBusy = true;
        item.State = FirmwareFileState.Hashing;
        item.Message = null;
        item.Progress = 0;
        var progress = new Progress<double>(p => item.Progress = p);

        IOperationScope op;
        try
        {
            op = await tracker.BeginAsync("fw.sha256", $"Firmware SHA-256: {item.Name}", device: null, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not start operation record");
            item.State = FirmwareFileState.Failed;
            item.Message = ex.Message;
            item.IsBusy = false;
            return;
        }

        await using var _ = op;
        try
        {
            if (!File.Exists(item.Path)) throw new FileNotFoundException(LocalizationService.Get("Firmware.FileMissing"), item.Path);

            var result = await Task.Run(() => FileHasher.ComputeSha256Async(item.Path, progress, ct), ct);
            item.Sha256 = result.Sha256;
            var seconds = result.Elapsed.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

            if (string.IsNullOrWhiteSpace(item.ExpectedHash))
            {
                item.State = FirmwareFileState.Hashed;
                item.Message = LocalizationService.Format("Firmware.HashedIn", seconds);
                await op.SucceedAsync($"SHA-256 computed ({result.Sha256}) in {seconds}s");
            }
            else if (FileHasher.DigestsMatch(item.ExpectedHash, result.Sha256))
            {
                item.State = FirmwareFileState.Verified;
                item.Message = LocalizationService.Get("Firmware.Match");
                await op.SucceedAsync($"SHA-256 matches expected value ({result.Sha256})");
            }
            else
            {
                item.State = FirmwareFileState.Mismatch;
                item.Message = LocalizationService.Get("Firmware.Mismatch");
                await op.FailAsync($"SHA-256 MISMATCH. Expected {item.ExpectedHash.Trim()}, got {result.Sha256}");
            }
        }
        catch (OperationCanceledException)
        {
            item.State = FirmwareFileState.Cancelled;
            item.Message = LocalizationService.Get("Firmware.Cancelled");
            item.Progress = 0;
            await op.CancelAsync("Cancelled by user");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            item.State = FirmwareFileState.Failed;
            item.Message = ex.Message;
            await op.FailAsync(ex.Message, ex);
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    private void ResetCancellation()
    {
        _cts.Dispose();
        _cts = new CancellationTokenSource();
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
    }
}

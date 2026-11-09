using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MUTASSIM.ToolPro.App.Navigation;
using MUTASSIM.ToolPro.App.Services;
using MUTASSIM.ToolPro.App.Workspace;
using MUTASSIM.ToolPro.Firmware;

namespace MUTASSIM.ToolPro.App.ViewModels;

/// <summary>One file slot (AP, BL, CP, CSC, USERDATA), in the order shown on the page.</summary>
public sealed partial class FlashSlot(string key, SamsungPart[] accepts, bool wipesData) : ObservableObject
{
    public string Key { get; } = key;
    public SamsungPart[] Accepts { get; } = accepts;
    public bool WipesDataSlot { get; } = wipesData;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileName), nameof(HasFile), nameof(Detail))]
    private string? _path;

    [ObservableProperty] private bool _isChecked = true;

    public bool HasFile => Path is not null;
    public string FileName => Path is null ? LocalizationService.Get("SamFlash.NotSelected") : System.IO.Path.GetFileName(Path);

    public string Detail
    {
        get
        {
            if (Path is null) return "";
            var f = SamsungFirmwareFile.FromPath(Path);
            var data = f.Part switch
            {
                SamsungPart.HOME_CSC => " · " + LocalizationService.Get("SamFlash.DataKept"),
                SamsungPart.CSC or SamsungPart.USERDATA => " · " + LocalizationService.Get("SamFlash.DataErased"),
                _ => ""
            };
            return $"{f.Part} · {f.Build?.Build ?? "?"}{data}";
        }
    }

    public void Refresh() => OnPropertyChanged(nameof(FileName));
}

/// <summary>
/// Samsung Flash page: five slots in order AP, BL, CP, CSC, USERDATA; "Load ZIP" extracts and sorts the files;
/// an optional PIT file is used to check the mapping (never written). Runs through the OperationRunner,
/// so busy state, confirmation, log and history work like every other operation.
/// </summary>
public sealed partial class SamsungFlashViewModel : ObservableObject, IPageViewModel
{
    private readonly OperationRunner _runner;
    private readonly IDialogService _dialogs;
    private readonly OperationLog _log;

    public SamsungFlashViewModel(OperationRunner runner, IDialogService dialogs, OperationLog log, DeviceContext deviceContext)
    {
        _runner = runner;
        _dialogs = dialogs;
        _log = log;
        DeviceContext = deviceContext;
        Slots =
        [
            new("AP", [SamsungPart.AP], false),
            new("BL", [SamsungPart.BL], false),
            new("CP", [SamsungPart.CP], false),
            new("CSC", [SamsungPart.HOME_CSC, SamsungPart.CSC], false),
            new("USERDATA", [SamsungPart.USERDATA], true) { IsChecked = false }
        ];
        foreach (var s in Slots) s.PropertyChanged += (_, _) => OnPropertyChanged(nameof(DataNotice));
    }

    public DeviceContext DeviceContext { get; }
    public OperationRunner Runner => _runner;
    public ObservableCollection<FlashSlot> Slots { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PitName), nameof(HasPit))]
    private string? _pitPath;

    [ObservableProperty] private string? _errorText;

    public bool HasPit => PitPath is not null;
    public string PitName => PitPath is null ? LocalizationService.Get("SamFlash.PitAuto") : Path.GetFileName(PitPath);

    private static bool Arabic => LocalizationService.Get("Lang.IsRtl") == "true";

    public IReadOnlyList<string> PhoneScreenErrors =>
        SamsungFlashErrors.PhoneScreen.Select(p => $"{p.Screen}:  {(Arabic ? p.Hint.Ar : p.Hint.En)}").ToList();

    public string DataNotice
    {
        get
        {
            var wipes = Selected().Any(f => SamsungFirmwareFile.FromPath(f).Part is SamsungPart.CSC or SamsungPart.USERDATA);
            return LocalizationService.Get(wipes ? "SamFlash.NoticeErase" : "SamFlash.NoticeKeep");
        }
    }

    public Task OnNavigatedToAsync() => Task.CompletedTask;

    private List<string> Selected() =>
        Slots.Where(s => s is { IsChecked: true, Path: not null }).Select(s => s.Path!).ToList();

    // ------------------------------------------------------------------ files

    [RelayCommand]
    private void BrowseSlot(FlashSlot? slot)
    {
        if (slot is null) return;
        var files = _dialogs.OpenFiles("Samsung firmware (*.tar.md5;*.tar)|*.tar.md5;*.tar|All files|*.*", null, multiselect: false);
        if (files.Length == 1) Place(files[0], slot);
    }

    [RelayCommand]
    private void ClearSlot(FlashSlot? slot)
    {
        if (slot is not null) slot.Path = null;
    }

    [RelayCommand]
    private void ClearAll()
    {
        foreach (var s in Slots) s.Path = null;
        PitPath = null;
        ErrorText = null;
    }

    /// <summary>Puts a file in the slot that fits its name; a file chosen in the wrong slot is moved, not refused.</summary>
    private void Place(string path, FlashSlot requested)
    {
        ErrorText = null;
        var part = SamsungFirmwareFile.FromPath(path).Part;
        if (part == SamsungPart.Unknown)
        {
            ErrorText = LocalizationService.Format("SamFlash.UnknownFile", Path.GetFileName(path));
            return;
        }

        var target = requested.Accepts.Contains(part) ? requested : Slots.First(s => s.Accepts.Contains(part));
        if (target != requested)
            _log.Warn(LocalizationService.Format("SamFlash.Moved", Path.GetFileName(path), target.Key));

        foreach (var other in Slots.Where(s => s != target && string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase)))
            other.Path = null;
        target.Path = path;
        target.IsChecked = part != SamsungPart.USERDATA || target.IsChecked;
    }

    [RelayCommand]
    private void BrowsePit()
    {
        var files = _dialogs.OpenFiles("PIT (*.pit)|*.pit|All files|*.*", null, multiselect: false);
        if (files.Length != 1) return;
        try
        {
            var pit = SamsungFlashPlanner.ReadPitFile(files[0]);
            PitPath = files[0];
            ErrorText = null;
            _log.Info($"PIT: {Path.GetFileName(files[0])} · {pit.Count} partitions");
        }
        catch (Exception ex) when (ex is PitFormatException or IOException)
        {
            ErrorText = ex.Message;
        }
    }

    [RelayCommand]
    private void ClearPit() => PitPath = null;

    [RelayCommand]
    private async Task LoadZipAsync()
    {
        var files = _dialogs.OpenFiles("ZIP (*.zip)|*.zip", null, multiselect: false);
        if (files.Length != 1) return;
        var zip = files[0];
        ZipImportResult? imported = null;

        var action = new ActionDefinition("sam.zip.import", "Load firmware ZIP", "تحميل ملف ZIP للسوفت", DeviceRequirement.None, async ctx =>
        {
            var last = -100.0;
            var progress = new Progress<double>(p =>
            {
                if (p - last < 10 && p < 100) return;
                last = p;
                ctx.Log.Muted($"ZIP {p:0}%");
            });
            imported = await FirmwareZip.ImportAsync(zip, ctx.Log.Muted, progress, ctx.Ct);
            ctx.Log.Value("Folder", imported.Folder);
            foreach (var (part, path) in imported.Parts) ctx.Log.Value(part.ToString(), Path.GetFileName(path));
            if (imported.Pit is not null) ctx.Log.Value("PIT", Path.GetFileName(imported.Pit));
            foreach (var alt in imported.AlternativeCsc) ctx.Log.Muted($"Also in ZIP (not selected): {Path.GetFileName(alt)}");
            foreach (var ign in imported.Ignored) ctx.Log.Muted($"Ignored: {ign}");
            return ActionResult.Ok($"{imported.Parts.Count} firmware file(s) found");
        });

        var result = await _runner.RunAsync(action);
        if (result is not { Success: true } || imported is null) return;

        ClearAll();
        foreach (var (_, path) in imported.Parts)
            Place(path, Slots.First(s => s.Accepts.Contains(SamsungFirmwareFile.FromPath(path).Part)));
        Slots.First(s => s.Key == "USERDATA").IsChecked = false; // never erase data by default
        PitPath = imported.Pit;
        OnPropertyChanged(nameof(DataNotice));
    }

    // ------------------------------------------------------------------ run

    [RelayCommand]
    private Task CheckAsync() => RunAsync(dryRun: true);

    [RelayCommand]
    private Task FlashAsync() => RunAsync(dryRun: false);

    private async Task RunAsync(bool dryRun)
    {
        ErrorText = null;
        var files = Selected();
        if (!Slots.Any(s => s is { Key: "AP", HasFile: true, IsChecked: true }) || !Slots.Any(s => s is { Key: "BL", HasFile: true, IsChecked: true }))
        {
            ErrorText = LocalizationService.Get("SamFlash.NeedApBl");
            return;
        }
        await _runner.RunAsync(SamsungFlashActions.Create(files, PitPath, dryRun));
    }
}

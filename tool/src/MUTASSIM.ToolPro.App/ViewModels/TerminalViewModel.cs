using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MUTASSIM.ToolPro.App.Services;
using MUTASSIM.ToolPro.Logging;

namespace MUTASSIM.ToolPro.App.ViewModels;

/// <summary>
/// System Terminal — live system log with colour by severity, copy / save / clear.
/// Entries are batched every 200 ms so heavy logging never freezes the UI.
/// ADB / Fastboot command tabs are added with their engines in Phase 3.
/// </summary>
public sealed partial class TerminalViewModel : ObservableObject, IDisposable
{
    private const int MaxLines = 3000;
    private readonly InMemoryLogSink _sink;
    private readonly IDialogService _dialogs;
    private readonly ConcurrentQueue<LogEntry> _pending = new();
    private readonly DispatcherTimer _flush;

    public TerminalViewModel(InMemoryLogSink sink, IDialogService dialogs)
    {
        _sink = sink;
        _dialogs = dialogs;
        foreach (var e in sink.Snapshot().TakeLast(MaxLines)) Entries.Add(e);

        _sink.EntryAdded += OnEntryAdded;
        _flush = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
        _flush.Tick += (_, _) => Flush();
        _flush.Start();
    }

    public ObservableCollection<LogEntry> Entries { get; } = [];

    [ObservableProperty] private bool _autoScroll = true;
    [ObservableProperty] private bool _showDebug;

    /// <summary>Raised after new lines were appended (the view scrolls to the end when AutoScroll is on).</summary>
    public event EventHandler? LinesAppended;

    [RelayCommand]
    private void Copy() => _dialogs.CopyToClipboard(BuildText());

    [RelayCommand]
    private async Task SaveAsync()
    {
        var path = _dialogs.SaveFile("Log (*.log)|*.log|Text (*.txt)|*.txt", $"toolpro_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        if (path is null) return;
        await File.WriteAllTextAsync(path, BuildText(), new UTF8Encoding(true));
    }

    [RelayCommand]
    private void Clear()
    {
        Entries.Clear();
        _sink.Clear();
    }

    private void OnEntryAdded(object? sender, LogEntry e) => _pending.Enqueue(e);

    private void Flush()
    {
        var added = false;
        while (_pending.TryDequeue(out var e))
        {
            if (!ShowDebug && e.Severity is LogSeverity.Debug or LogSeverity.Verbose) continue;
            Entries.Add(e);
            added = true;
        }

        while (Entries.Count > MaxLines) Entries.RemoveAt(0);
        if (added) LinesAppended?.Invoke(this, EventArgs.Empty);
    }

    private string BuildText()
    {
        var sb = new StringBuilder();
        foreach (var e in Entries)
        {
            sb.Append(e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(" [").Append(e.Severity).Append("] (")
              .Append(e.Source).Append(") ").AppendLine(e.Message);
            if (e.Exception is not null) sb.AppendLine(e.Exception);
        }
        return sb.ToString();
    }

    public void Dispose()
    {
        _flush.Stop();
        _sink.EntryAdded -= OnEntryAdded;
    }
}

using System.Collections.ObjectModel;
using System.Text;
using MUTASSIM.ToolPro.App.Services;

namespace MUTASSIM.ToolPro.App.Workspace;

public enum LogKind
{
    Info,
    Value,
    Ok,
    Warn,
    Error,
    Muted,
    Header
}

public sealed record OpLine(DateTime Time, string Text, LogKind Kind)
{
    public string TimeText => Time.ToString("HH:mm:ss");
}

/// <summary>The technician-facing operation log (right panel). Thread-safe: writes are marshalled to the UI.</summary>
public sealed class OperationLog
{
    private const int MaxLines = 5000;

    public ObservableCollection<OpLine> Lines { get; } = [];

    /// <summary>Raised on the UI thread after a line is appended (for auto-scroll).</summary>
    public event EventHandler? LineAdded;

    public void Header(string text) => Write($"── {text} ──", LogKind.Header);
    public void Info(string text) => Write(text, LogKind.Info);
    public void Ok(string text) => Write(text, LogKind.Ok);
    public void Warn(string text) => Write(text, LogKind.Warn);
    public void Error(string text) => Write(text, LogKind.Error);
    public void Muted(string text) => Write(text, LogKind.Muted);

    /// <summary>"Label .......... : value" — only written when the device actually returned a value.</summary>
    public void Value(string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        Write($"{label,-20}: {value.Trim()}", LogKind.Value);
    }

    public void Write(string text, LogKind kind)
    {
        var line = new OpLine(DateTime.Now, text, kind);
        Ui.Post(() =>
        {
            Lines.Add(line);
            while (Lines.Count > MaxLines) Lines.RemoveAt(0);
            LineAdded?.Invoke(this, EventArgs.Empty);
        });
    }

    public void Clear() => Ui.Post(Lines.Clear);

    public string ToText()
    {
        var sb = new StringBuilder();
        foreach (var l in Lines) sb.Append(l.TimeText).Append("  ").AppendLine(l.Text);
        return sb.ToString();
    }
}

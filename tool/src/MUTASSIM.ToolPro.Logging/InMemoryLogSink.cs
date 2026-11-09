using Serilog.Core;
using Serilog.Events;

namespace MUTASSIM.ToolPro.Logging;

public enum LogSeverity
{
    Verbose,
    Debug,
    Info,
    Success,
    Warning,
    Error
}

public sealed record LogEntry(
    DateTimeOffset Timestamp,
    LogSeverity Severity,
    string Source,
    string Message,
    string? DeviceKey,
    string? Exception);

/// <summary>
/// Ring-buffer sink feeding the in-app System Terminal. Thread-safe; raises <see cref="EntryAdded"/>
/// on the logging thread (UI must marshal to its dispatcher).
/// A log event carrying the property "Outcome"="Success" is shown as a success line.
/// </summary>
public sealed class InMemoryLogSink(int capacity = 5000) : ILogEventSink
{
    private readonly Queue<LogEntry> _buffer = new();
    private readonly object _gate = new();

    public event EventHandler<LogEntry>? EntryAdded;

    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_gate) return _buffer.ToArray();
    }

    public void Clear()
    {
        lock (_gate) _buffer.Clear();
    }

    public void Emit(LogEvent logEvent)
    {
        var entry = new LogEntry(
            logEvent.Timestamp,
            Map(logEvent),
            ReadScalar(logEvent, "SourceContext") is { } ctx ? ShortName(ctx) : "App",
            logEvent.RenderMessage(),
            ReadScalar(logEvent, "DeviceKey"),
            logEvent.Exception?.ToString());

        lock (_gate)
        {
            _buffer.Enqueue(entry);
            while (_buffer.Count > capacity) _buffer.Dequeue();
        }

        EntryAdded?.Invoke(this, entry);
    }

    private static LogSeverity Map(LogEvent e)
    {
        if (ReadScalar(e, "Outcome") == "Success") return LogSeverity.Success;
        return e.Level switch
        {
            LogEventLevel.Verbose => LogSeverity.Verbose,
            LogEventLevel.Debug => LogSeverity.Debug,
            LogEventLevel.Information => LogSeverity.Info,
            LogEventLevel.Warning => LogSeverity.Warning,
            _ => LogSeverity.Error
        };
    }

    private static string? ReadScalar(LogEvent e, string name) =>
        e.Properties.TryGetValue(name, out var v) && v is ScalarValue { Value: { } value } ? value.ToString() : null;

    private static string ShortName(string sourceContext)
    {
        var idx = sourceContext.LastIndexOf('.');
        return idx >= 0 ? sourceContext[(idx + 1)..] : sourceContext;
    }
}

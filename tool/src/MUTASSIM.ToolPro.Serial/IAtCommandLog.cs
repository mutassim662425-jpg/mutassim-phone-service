namespace MUTASSIM.ToolPro.Serial;

/// <summary>
/// Receives every command sent and every response parsed, for the on-screen AT console and the audit log.
/// Implementations must not throw; a logging failure must never break a diagnostic read.
/// </summary>
public interface IAtCommandLog
{
    void OnSent(AtCommand command);
    void OnReceived(AtResponse response);
    /// <summary>A command the engine refused to send (e.g. a state-changing / protection command).</summary>
    void OnRefused(AtCommand command, string reason);
}

/// <summary>An in-memory log of AT exchanges (newest last), safe for the UI to bind to.</summary>
public sealed class AtCommandLog : IAtCommandLog
{
    public sealed record Entry(DateTimeOffset At, string Direction, string Text);

    private readonly object _gate = new();
    private readonly List<Entry> _entries = [];
    public int Capacity { get; init; } = 2000;

    public IReadOnlyList<Entry> Entries { get { lock (_gate) return _entries.ToArray(); } }

    public event EventHandler<Entry>? Appended;

    public void OnSent(AtCommand command) =>
        Add(">>", command.Text + (command.IsReadOnly ? "" : "   [state-change — not executed]"));

    public void OnReceived(AtResponse response)
    {
        foreach (var line in response.Lines) Add("<<", line);
        Add("==", $"{response.Status} ({response.Elapsed.TotalMilliseconds:F0} ms)");
    }

    public void OnRefused(AtCommand command, string reason) =>
        Add("!!", $"REFUSED {command.Text} — {reason}");

    private void Add(string direction, string text)
    {
        var entry = new Entry(DateTimeOffset.Now, direction, text);
        lock (_gate)
        {
            _entries.Add(entry);
            if (_entries.Count > Capacity) _entries.RemoveRange(0, _entries.Count - Capacity);
        }
        Appended?.Invoke(this, entry);
    }

    public void Clear() { lock (_gate) _entries.Clear(); }
}

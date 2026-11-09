namespace MUTASSIM.ToolPro.Serial;

/// <summary>
/// Whether an AT command only reads information or changes the device. The engine executes
/// <see cref="Read"/> commands; <see cref="StateChange"/> commands are described and logged but are
/// never run by this engine (requirement: diagnostic commands are separated from state-changing ones,
/// and protection-bypass / engineering / activation commands are not executed here).
/// </summary>
public enum AtCommandKind
{
    /// <summary>Reads information with no side effects (e.g. ATI, AT+CGMI, AT+CGSN).</summary>
    Read,

    /// <summary>Would change device state, settings, or protection. Not executed by this engine.</summary>
    StateChange
}

/// <summary>The final result code of an AT exchange.</summary>
public enum AtStatus
{
    /// <summary>Terminated with a final "OK".</summary>
    Ok,
    /// <summary>Terminated with a plain "ERROR".</summary>
    Error,
    /// <summary>Terminated with "+CME ERROR: &lt;n&gt;" (mobile equipment error).</summary>
    CmeError,
    /// <summary>Terminated with "+CMS ERROR: &lt;n&gt;" (message service error).</summary>
    CmsError,
    /// <summary>No final result code arrived before the timeout.</summary>
    Timeout,
    /// <summary>The port returned nothing at all.</summary>
    Empty
}

/// <summary>One AT command to send, with its read/state-change classification and a human description.</summary>
public sealed record AtCommand(string Text, AtCommandKind Kind, string Title, string? Description = null)
{
    /// <summary>A read-only command. Preferred factory so callers are explicit about intent.</summary>
    public static AtCommand Read(string text, string title, string? description = null) =>
        new(text, AtCommandKind.Read, title, description);

    /// <summary>A command that would change the device. Created for description/audit only — the engine refuses to run it.</summary>
    public static AtCommand StateChange(string text, string title, string? description = null) =>
        new(text, AtCommandKind.StateChange, title, description);

    public bool IsReadOnly => Kind == AtCommandKind.Read;
}

/// <summary>The parsed result of sending one AT command: every raw line plus the final status.</summary>
public sealed record AtResponse(
    string Command,
    IReadOnlyList<string> Lines,
    AtStatus Status,
    string? FinalLine,
    int? ErrorCode,
    TimeSpan Elapsed)
{
    public bool IsOk => Status == AtStatus.Ok;

    /// <summary>The response lines that are not echo of the command and not the final result code.</summary>
    public IReadOnlyList<string> Payload => Lines
        .Where(l => !string.Equals(l, Command, StringComparison.OrdinalIgnoreCase))
        .Where(l => !AtResponseParser.IsFinalResultCode(l))
        .ToArray();

    /// <summary>The single most useful payload line (first non-empty), or null.</summary>
    public string? FirstValue => Payload.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim();
}

namespace MUTASSIM.ToolPro.Serial;

/// <summary>
/// Classifies free-text AT commands typed into the console as read-only or state-changing, so the engine can
/// refuse to execute anything that changes the device or its protection.
///
/// Policy (conservative):
///   • Query / test forms ("AT+X?", "AT+X=?") are reads.
///   • A small allow-list of harmless session settings (echo, error verbosity) are reads.
///   • Known engineering / activation / protection / FRP commands are always state-change (refused).
///   • Any other set form ("AT+X=...") is treated as state-change by default — setting parameters changes the device.
///   • A bare execute/read ("AT", "ATI", "AT+CGSN") is a read.
/// This engine never executes the refused commands; obtaining them is not the point — the point is that an
/// unverified set/engineering command cannot change a device through this tool.
/// </summary>
public static class AtCommandClassifier
{
    // Publicly-known commands that change protection / run engineering or activation routines. Always refused.
    private static readonly string[] ProtectedPrefixes =
    [
        "AT+SWATD", "AT+ACTIVATE", "AT+PRECONFIG", "AT+SWSEC", "AT+KSTRINGB", "AT+DUMPCTRL",
        "AT+FRP", "AT+FACTORY", "AT+WIPE", "AT+FORMAT", "AT+ERASE", "AT+RSTBYTE", "AT+SETPROT",
        "AT+REMOVE", "AT+UNLOCK", "AT+BYPASS", "AT+DEVCONINFO", "AT+DUMP"
    ];

    // Harmless session settings that are reads for our purposes (do not change the device).
    private static readonly string[] SafeSessionSetPrefixes = ["AT+CMEE", "ATE", "AT+CSCS", "ATV", "ATQ"];

    public static AtCommand Classify(string rawText)
    {
        var text = (rawText ?? string.Empty).Trim();
        if (text.Length == 0) return AtCommand.Read(text, "(empty)");

        var upper = text.ToUpperInvariant();

        if (ProtectedPrefixes.Any(p => upper.StartsWith(p, StringComparison.Ordinal)))
            return AtCommand.StateChange(text, "Protected / engineering command", "Refused: changes device protection or runs an engineering/activation routine.");

        // Query and test forms only read.
        if (upper.EndsWith('?'))
            return AtCommand.Read(text, "Query");

        var isSetForm = upper.Contains('=');
        if (isSetForm && SafeSessionSetPrefixes.Any(p => upper.StartsWith(p, StringComparison.Ordinal)))
            return AtCommand.Read(text, "Session setting");

        if (isSetForm)
            return AtCommand.StateChange(text, "Set command", "Refused by default: setting parameters can change device state. Use an authorized, documented path.");

        // Bare execute / read form.
        return AtCommand.Read(text, "Read");
    }
}

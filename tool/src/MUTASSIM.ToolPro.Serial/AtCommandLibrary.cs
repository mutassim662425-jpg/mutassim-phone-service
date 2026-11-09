namespace MUTASSIM.ToolPro.Serial;

/// <summary>
/// Built-in, read-only AT commands (standard 3GPP TS 27.007 information commands). Everything here only
/// reads; there are deliberately no state-changing, engineering, activation or protection commands in this
/// library. New read sequences can be added here or supplied by a plugin.
/// </summary>
public static class AtCommandLibrary
{
    public static readonly AtCommand Attention = AtCommand.Read("AT", "Attention / link test");
    public static readonly AtCommand Identify = AtCommand.Read("ATI", "Product identification");
    public static readonly AtCommand Manufacturer = AtCommand.Read("AT+CGMI", "Manufacturer");
    public static readonly AtCommand Model = AtCommand.Read("AT+CGMM", "Model");
    public static readonly AtCommand Revision = AtCommand.Read("AT+CGMR", "Firmware / revision");
    public static readonly AtCommand SerialImei = AtCommand.Read("AT+CGSN", "IMEI / serial (read)");
    public static readonly AtCommand Imsi = AtCommand.Read("AT+CIMI", "IMSI (read)");
    public static readonly AtCommand ManufacturerAlt = AtCommand.Read("AT+GMI", "Manufacturer (alt)");
    public static readonly AtCommand ModelAlt = AtCommand.Read("AT+GMM", "Model (alt)");
    public static readonly AtCommand RevisionAlt = AtCommand.Read("AT+GMR", "Firmware (alt)");
    public static readonly AtCommand Capabilities = AtCommand.Read("AT+GCAP", "Capabilities");
    public static readonly AtCommand SignalQuality = AtCommand.Read("AT+CSQ", "Signal quality");
    public static readonly AtCommand SimStatus = AtCommand.Read("AT+CPIN?", "SIM lock state (query)");
    public static readonly AtCommand VerboseErrors = AtCommand.Read("AT+CMEE=2", "Enable verbose errors (session)");

    /// <summary>A quick link check: just "AT" expecting OK.</summary>
    public static AtCommandSequence LinkCheck { get; } = new("Link check", [Attention]);

    /// <summary>The standard read-only identity set used by the diagnostic reader.</summary>
    public static AtCommandSequence Identity { get; } = new("Device identity",
    [
        VerboseErrors, Attention, Manufacturer, Model, Revision, Identify, SerialImei
    ]);

    /// <summary>All built-in read-only commands, for the console's command picker.</summary>
    public static IReadOnlyList<AtCommand> All { get; } =
    [
        Attention, Identify, Manufacturer, Model, Revision, SerialImei, Imsi,
        ManufacturerAlt, ModelAlt, RevisionAlt, Capabilities, SignalQuality, SimStatus, VerboseErrors
    ];
}

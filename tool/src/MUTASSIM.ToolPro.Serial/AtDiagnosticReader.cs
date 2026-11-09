namespace MUTASSIM.ToolPro.Serial;

/// <summary>Read-only diagnostic information gathered from a device over AT.</summary>
public sealed record AtDiagnosticInfo
{
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public string? Firmware { get; init; }
    public string? Identification { get; init; }
    public string? Imei { get; init; }
    public bool LinkOk { get; init; }
    public IReadOnlyList<AtResponse> Raw { get; init; } = [];

    public bool HasAnything =>
        LinkOk || Manufacturer is not null || Model is not null || Firmware is not null || Imei is not null;
}

/// <summary>
/// High-level, read-only device identity reader. Runs the standard 3GPP identity reads and extracts the
/// manufacturer, model, firmware and IMEI. It performs no state changes; it is the safe "read device info"
/// building block the Samsung and generic diagnostic engines use.
/// </summary>
public sealed class AtDiagnosticReader(AtCommandChannel channel)
{
    public async Task<AtDiagnosticInfo> ReadIdentityAsync(CancellationToken ct = default)
    {
        var runner = new AtSequenceRunner(channel) { StopOnError = false };
        var result = await runner.RunAsync(AtCommandLibrary.Identity, ct).ConfigureAwait(false);

        var link = result.For("AT");
        return new AtDiagnosticInfo
        {
            LinkOk = link?.IsOk ?? false,
            Manufacturer = Value(result, "AT+CGMI") ?? Value(result, "AT+GMI"),
            Model = Value(result, "AT+CGMM") ?? Value(result, "AT+GMM"),
            Firmware = Value(result, "AT+CGMR") ?? Value(result, "AT+GMR"),
            Identification = Value(result, "ATI"),
            Imei = NormalizeImei(Value(result, "AT+CGSN")),
            Raw = result.Responses
        };
    }

    private static string? Value(AtSequenceResult result, string command)
    {
        var response = result.For(command);
        if (response is null || !response.IsOk) return null;
        var value = response.FirstValue;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>Keeps the 14–16 digits of an IMEI, dropping any prefix label the modem adds.</summary>
    public static string? NormalizeImei(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return digits.Length is >= 14 and <= 16 ? digits : (digits.Length == 0 ? null : digits);
    }
}

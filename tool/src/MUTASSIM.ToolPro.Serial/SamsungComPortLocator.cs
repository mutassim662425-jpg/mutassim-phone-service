using System.Globalization;
using System.Text.RegularExpressions;

namespace MUTASSIM.ToolPro.Serial;

/// <summary>
/// Pure logic (unit-tested) for locating and ranking the Samsung modem/diagnostic COM port among the ports an
/// enumerator reports, and for parsing the small bits of Windows PnP strings the enumerator feeds in. No I/O,
/// no WMI — the Windows enumerator supplies raw strings and this turns them into ranked <see cref="SerialPortInfo"/>.
/// </summary>
public static partial class SamsungComPortLocator
{
    /// <summary>Samsung's USB vendor id.</summary>
    public const int SamsungVendorId = 0x04E8;

    /// <summary>Ranks ports best-first: a Samsung modem port before a diagnostic port before anything else.</summary>
    public static IReadOnlyList<SerialPortInfo> Rank(IEnumerable<SerialPortInfo> ports) =>
        ports.OrderByDescending(Score).ThenBy(p => p.PortName, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>The single best Samsung candidate, or null when none of the ports looks like a Samsung interface.</summary>
    public static SerialPortInfo? PickBest(IEnumerable<SerialPortInfo> ports)
    {
        var best = Rank(ports).FirstOrDefault();
        return best is not null && Score(best) > 0 ? best : null;
    }

    private static int Score(SerialPortInfo p)
    {
        var score = 0;
        if (p.VendorId == SamsungVendorId) score += 100;
        var d = p.Description?.ToLowerInvariant() ?? "";
        if (d.Contains("samsung")) score += 40;
        if (d.Contains("modem")) score += 20;
        else if (d.Contains("diagnostic") || d.Contains("diag")) score += 10;
        return score;
    }

    [GeneratedRegex(@"\((?<com>COM\d+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex ComInName();

    /// <summary>Extracts "COM5" from a WMI name like "SAMSUNG Mobile USB Modem (COM5)".</summary>
    public static string? ExtractComPort(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var m = ComInName().Match(name);
        return m.Success ? m.Groups["com"].Value.ToUpperInvariant() : null;
    }

    /// <summary>The name without its trailing "(COMx)" suffix, used as the description.</summary>
    public static string? DescriptionFromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var cleaned = ComInName().Replace(name, "").Trim().Trim('-', '–', ' ');
        return cleaned.Length > 0 ? cleaned : null;
    }

    [GeneratedRegex(@"VID_(?<vid>[0-9A-Fa-f]{4}).*?PID_(?<pid>[0-9A-Fa-f]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex VidPid();

    /// <summary>Parses (vid, pid) from a PnP device id like "USB\VID_04E8&amp;PID_6860\...". Null when absent.</summary>
    public static (int Vid, int Pid)? ParseVidPid(string? pnpDeviceId)
    {
        if (string.IsNullOrWhiteSpace(pnpDeviceId)) return null;
        var m = VidPid().Match(pnpDeviceId);
        if (!m.Success) return null;
        return (int.Parse(m.Groups["vid"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                int.Parse(m.Groups["pid"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }
}

using System.Text.RegularExpressions;

namespace MUTASSIM.ToolPro.Adb;

public sealed record AdbDeviceEntry(string Serial, string State, IReadOnlyDictionary<string, string> Attributes)
{
    public string? Model => Attributes.TryGetValue("model", out var m) ? m.Replace('_', ' ') : null;
    public string? Product => Attributes.TryGetValue("product", out var p) ? p : null;
    public string? DeviceCodename => Attributes.TryGetValue("device", out var d) ? d : null;
}

public static partial class AdbParsers
{
    /// <summary>Parses the output of <c>adb devices -l</c>.</summary>
    public static IReadOnlyList<AdbDeviceEntry> ParseDevices(string output)
    {
        var list = new List<AdbDeviceEntry>();
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith('*') || line.StartsWith("adb server", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;

            var serial = parts[0];
            // "no permissions (…)" has spaces in the state.
            var stateParts = new List<string>();
            var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 1; i < parts.Length; i++)
            {
                var idx = parts[i].IndexOf(':');
                if (idx > 0 && stateParts.Count > 0) // first token is always the state
                    attrs[parts[i][..idx]] = parts[i][(idx + 1)..];
                else
                    stateParts.Add(parts[i]);
            }
            list.Add(new AdbDeviceEntry(serial, string.Join(' ', stateParts), attrs));
        }
        return list;
    }

    [GeneratedRegex(@"^\[(?<k>[^\]]+)\]:\s*\[(?<v>.*)\]\s*$")]
    private static partial Regex GetpropLine();

    /// <summary>Parses <c>adb shell getprop</c> output: "[key]: [value]".</summary>
    public static IReadOnlyDictionary<string, string> ParseGetprop(string output)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in output.Split('\n'))
        {
            var m = GetpropLine().Match(raw.TrimEnd('\r'));
            if (m.Success) dict[m.Groups["k"].Value] = m.Groups["v"].Value;
        }
        return dict;
    }

    /// <summary>Parses <c>pm list packages</c> output ("package:com.x").</summary>
    public static IReadOnlyList<string> ParsePackages(string output) =>
        output.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("package:", StringComparison.Ordinal))
            .Select(l => l["package:".Length..])
            .Where(p => p.Length > 0)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

    /// <summary>Returns the device-owner / profile-owner admin components from <c>dumpsys device_policy</c>.</summary>
    public static IReadOnlyList<string> ParseDeviceAdmins(string dumpsys)
    {
        var result = new List<string>();
        string? section = null;
        foreach (var raw in dumpsys.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("Device Owner", StringComparison.OrdinalIgnoreCase)) section = "Device Owner";
            else if (line.StartsWith("Profile Owner", StringComparison.OrdinalIgnoreCase)) section = "Profile Owner";
            else if (section is not null && line.StartsWith("admin=", StringComparison.OrdinalIgnoreCase))
            {
                result.Add($"{section}: {ExtractComponent(line)}");
                section = null;
            }
        }
        return result.Distinct().ToArray();
    }

    private static string ExtractComponent(string adminLine)
    {
        var m = Regex.Match(adminLine, @"ComponentInfo\{(?<c>[^}]+)\}");
        return m.Success ? m.Groups["c"].Value : adminLine["admin=".Length..];
    }
}

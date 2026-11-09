using System.Text.RegularExpressions;

namespace MUTASSIM_SERVER.Core.Commands;

/// <summary>
/// The single list of commands the system accepts. The server refuses to
/// queue anything else and the agent refuses to execute anything else.
/// </summary>
public static partial class AllowedCommands
{
    public const string Ping = "PING";
    public const string DeviceScan = "DEVICE_SCAN";
    public const string AdbInfo = "ADB_INFO";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal) { Ping, DeviceScan, AdbInfo };

    /// <summary>Returns the canonical (upper-case) name, or null if not allowed.</summary>
    public static string? Normalize(string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
            return null;

        var upper = action.Trim().ToUpperInvariant();
        return All.Contains(upper) ? upper : null;
    }

    /// <summary>Agent/device identifiers: 1-64 chars of letters, digits, . _ : -</summary>
    public static bool IsValidDeviceId(string? deviceId) =>
        !string.IsNullOrWhiteSpace(deviceId) && DeviceIdRegex().IsMatch(deviceId);

    [GeneratedRegex("^[A-Za-z0-9._:-]{1,64}$")]
    private static partial Regex DeviceIdRegex();
}

using System.Security.Cryptography;
using System.Text;

namespace MUTASSIM.ToolPro.Core.Security;

/// <summary>
/// One-way hash of a device key (which may contain a serial number), so history can tell
/// devices apart without storing the serial in clear text.
/// </summary>
public static class DeviceKeyHasher
{
    public static string Hash(string deviceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceKey);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(deviceKey));
        return Convert.ToHexString(bytes, 0, 12).ToLowerInvariant();
    }
}

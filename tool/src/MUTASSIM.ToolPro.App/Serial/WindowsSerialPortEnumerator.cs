using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Serial;

namespace MUTASSIM.ToolPro.App.Serial;

/// <summary>
/// Lists COM ports for the AT console. Combines the raw port names from the OS with whatever the device
/// manager already knows about connected phones (friendly name, USB VID/PID, which COM port they expose),
/// so the Samsung modem/diagnostic port can be picked automatically. No extra WMI code: the device manager
/// already does the USB enumeration.
/// </summary>
public sealed class WindowsSerialPortEnumerator(IDeviceManager devices) : ISerialPortEnumerator
{
    public IReadOnlyList<SerialPortInfo> List()
    {
        var byName = new Dictionary<string, SerialPortInfo>(StringComparer.OrdinalIgnoreCase);

        if (OperatingSystem.IsWindows())
            foreach (var name in SystemSerialChannel.AvailablePorts())
                byName[name] = new SerialPortInfo(name);

        foreach (var d in devices.Devices)
        {
            if (string.IsNullOrWhiteSpace(d.ComPort)) continue;
            var description = Join(d.Brand ?? d.Manufacturer, d.Model) ?? d.Source;
            byName[d.ComPort] = new SerialPortInfo(d.ComPort, description, d.UsbVendorId, d.UsbProductId);
        }

        return SamsungComPortLocator.Rank(byName.Values);
    }

    private static string? Join(string? a, string? b)
    {
        var s = string.Join(' ', new[] { a, b }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return s.Length > 0 ? s : null;
    }
}

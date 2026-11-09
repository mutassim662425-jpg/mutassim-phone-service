using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MUTASSIM.ToolPro.Core.Devices;

namespace MUTASSIM.ToolPro.Usb;

/// <summary>One raw Windows PnP entry (Win32_PnPEntity) for a USB device or interface.</summary>
public sealed record PnpEntry(string InstanceId, string? Name, string? PnpClass, int ErrorCode);

/// <summary>
/// Windows USB detection through WMI (Win32_PnPEntity). Reports phones from known vendors, including
/// modes that adb/fastboot cannot see (Download, EDL, BROM, Preloader, Apple, missing driver, MTP).
/// The device manager merges a USB entry into the ADB/Fastboot device with the same serial number,
/// so one phone is never shown twice.
/// </summary>
public sealed partial class UsbDeviceDetector(ILogger<UsbDeviceDetector> logger) : IDeviceDetector
{
    public string Name => "USB";
    public bool IsAvailable => OperatingSystem.IsWindows();
    public string? UnavailableReason => OperatingSystem.IsWindows() ? null : "USB detection requires Windows.";

    public Task<IReadOnlyList<DeviceInfo>> ScanAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return Task.FromResult<IReadOnlyList<DeviceInfo>>([]);
#pragma warning disable CA1416 // guarded by OperatingSystem.IsWindows() above
        return Task.Run(() => (IReadOnlyList<DeviceInfo>)Build(QueryPnp()), cancellationToken);
#pragma warning restore CA1416
    }

    /// <summary>All USB PnP entries from known phone/chipset vendors (used by the USB Drivers page too).</summary>
    public IReadOnlyList<PnpEntry> ListVendorEntries()
    {
        if (!OperatingSystem.IsWindows()) return [];
        return QueryPnp()
            .Where(e => UsbIdClassifier.TryParseInstanceId(e.InstanceId, out var vid, out _) && UsbIdClassifier.VendorName(vid) is not null)
            .ToArray();
    }

    [SupportedOSPlatform("windows")]
    private List<PnpEntry> QueryPnp()
    {
        var list = new List<PnpEntry>();
        using var searcher = new System.Management.ManagementObjectSearcher(
            "SELECT PNPDeviceID, Name, PNPClass, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'USB%'");
        using var results = searcher.Get();
        foreach (var obj in results)
        {
            using (obj)
            {
                var id = obj["PNPDeviceID"] as string;
                if (string.IsNullOrEmpty(id)) continue;
                var err = obj["ConfigManagerErrorCode"] is { } code ? Convert.ToInt32(code) : 0;
                list.Add(new PnpEntry(id, obj["Name"] as string, obj["PNPClass"] as string, err));
            }
        }
        logger.LogDebug("WMI returned {Count} USB entries", list.Count);
        return list;
    }

    /// <summary>Pure function (unit-tested): turns raw PnP entries into devices.</summary>
    public static List<DeviceInfo> Build(IEnumerable<PnpEntry> entries)
    {
        var parsed = entries
            .Select(e => UsbIdClassifier.TryParseInstanceId(e.InstanceId, out var vid, out var pid)
                ? (Entry: e, Vid: vid, Pid: pid, IsInterface: e.InstanceId.Contains("&MI_", StringComparison.OrdinalIgnoreCase))
                : default)
            .Where(x => x.Entry is not null && UsbIdClassifier.VendorName(x.Vid) is not null)
            .ToList();

        var devices = new List<DeviceInfo>();
        foreach (var parent in parsed.Where(x => !x.IsInterface))
        {
            var classification = UsbIdClassifier.Classify(parent.Vid, parent.Pid)!;
            var siblings = parsed.Where(x => x.Vid == parent.Vid && x.Pid == parent.Pid).ToList();
            var parentCount = siblings.Count(x => !x.IsInterface);
            // Interfaces can only be attributed safely when exactly one physical device has this VID/PID.
            var related = parentCount == 1
                ? siblings
                : siblings.Where(x => !x.IsInterface && ReferenceEquals(x.Entry, parent.Entry)).ToList();

            var worstError = related.Max(x => x.Entry.ErrorCode);
            var driver = worstError switch
            {
                0 => DriverState.Installed,
                28 => DriverState.Missing,
                _ => DriverState.Problem
            };

            var mode = classification.Mode;
            if (mode == ConnectionMode.Fastboot) continue; // fastboot.exe reports these with full details
            if (mode == ConnectionMode.Unknown &&
                related.Any(x => string.Equals(x.Entry.PnpClass, "WPD", StringComparison.OrdinalIgnoreCase)))
                mode = ConnectionMode.Mtp;

            // Report: special modes, MTP, or any vendor device with a driver problem. Plain composite
            // ADB/charging interfaces with working drivers are left to the ADB detector.
            if (mode == ConnectionMode.Unknown && driver == DriverState.Installed) continue;

            var com = related.Select(x => ComPort(x.Entry.Name)).FirstOrDefault(c => c is not null);
            var serial = SerialFromInstanceId(parent.Entry.InstanceId);

            // The phone reports its own product string (e.g. "TECNO CAMON 40 Pro 5G"). When it starts with a
            // known brand, that brand is used; the USB vendor (often the chip maker) is kept as chipset info.
            var productName = related.Select(x => x.Entry.Name).FirstOrDefault(n => BrandFromProductName(n) is not null)
                              ?? parent.Entry.Name;
            var brand = BrandFromProductName(productName);
            var vendorIsChipMaker = parent.Vid is 0x0E8D or 0x05C6 or 0x1782;

            devices.Add(new DeviceInfo
            {
                Key = "usb:" + parent.Entry.InstanceId.ToUpperInvariant(),
                Mode = mode,
                Source = "USB",
                Manufacturer = brand is not null ? productName!.Split(' ')[0] : classification.VendorName,
                Brand = brand,
                Chipset = vendorIsChipMaker ? $"{classification.VendorName} (USB VID {parent.Vid:X4})" : null,
                Model = productName ?? classification.Description,
                SerialNumber = serial,
                UsbVendorId = parent.Vid,
                UsbProductId = parent.Pid,
                UsbInstanceId = parent.Entry.InstanceId,
                ComPort = com,
                DriverState = driver,
                Properties = new Dictionary<string, string>
                {
                    ["usb.description"] = classification.Description,
                    ["usb.errorCode"] = worstError.ToString()
                }
            });
        }
        return devices;
    }

    private static readonly string[] KnownBrands =
    [
        "samsung", "xiaomi", "redmi", "poco", "huawei", "honor", "oppo", "realme", "vivo", "iqoo", "tecno", "infinix",
        "itel", "motorola", "moto", "nokia", "google", "pixel", "oneplus", "zte", "nubia", "lenovo", "sony", "lg", "apple"
    ];

    /// <summary>Brand from the device's own USB product string ("TECNO CAMON 40 Pro 5G" → "tecno"); null when not recognised.</summary>
    public static string? BrandFromProductName(string? productName)
    {
        if (string.IsNullOrWhiteSpace(productName)) return null;
        var first = productName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();
        var brand = KnownBrands.FirstOrDefault(b => first == b);
        return brand switch
        {
            "moto" => "motorola",
            "pixel" => "google",
            "iqoo" => "vivo",
            _ => brand
        };
    }

    [GeneratedRegex(@"\((COM\d+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex ComRegex();

    public static string? ComPort(string? name) =>
        name is not null && ComRegex().Match(name) is { Success: true } m ? m.Groups[1].Value.ToUpperInvariant() : null;

    /// <summary>The last segment of an instance id is the device's iSerial when it has no '&amp;' (Windows-generated otherwise).</summary>
    public static string? SerialFromInstanceId(string instanceId)
    {
        var idx = instanceId.LastIndexOf('\\');
        if (idx < 0 || idx == instanceId.Length - 1) return null;
        var last = instanceId[(idx + 1)..];
        return last.Contains('&') ? null : last;
    }
}

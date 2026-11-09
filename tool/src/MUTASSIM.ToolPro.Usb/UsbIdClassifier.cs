using MUTASSIM.ToolPro.Core.Devices;

namespace MUTASSIM.ToolPro.Usb;

public sealed record UsbClassification(string VendorName, ConnectionMode Mode, string Description);

/// <summary>
/// Classifies a USB VID/PID pair using publicly documented identifiers.
/// Only returns a specific <see cref="ConnectionMode"/> for well-known IDs; otherwise the mode is Unknown
/// and the final decision is left to the protocol detectors (ADB/Fastboot/…).
/// </summary>
public static class UsbIdClassifier
{
    private static readonly Dictionary<ushort, string> Vendors = new()
    {
        [0x04E8] = "Samsung",
        [0x05C6] = "Qualcomm",
        [0x0E8D] = "MediaTek",
        [0x05AC] = "Apple",
        [0x18D1] = "Google",
        [0x2717] = "Xiaomi",
        [0x12D1] = "Huawei",
        [0x339B] = "Honor",
        [0x22D9] = "OPPO / Realme",
        [0x2D95] = "Vivo",
        [0x22B8] = "Motorola",
        [0x2E04] = "HMD (Nokia)",
        [0x0421] = "Nokia",
        [0x2A70] = "OnePlus",
        [0x19D2] = "ZTE",
        [0x17EF] = "Lenovo",
        [0x0FCE] = "Sony",
        [0x1004] = "LG",
        [0x1782] = "Spreadtrum / Unisoc"
    };

    private static readonly Dictionary<(ushort Vid, ushort Pid), (ConnectionMode Mode, string Description)> Known = new()
    {
        [(0x04E8, 0x685D)] = (ConnectionMode.SamsungDownload, "Samsung Download Mode"),
        [(0x05C6, 0x9008)] = (ConnectionMode.QualcommEdl, "Qualcomm HS-USB QDLoader 9008 (EDL)"),
        [(0x05C6, 0x900E)] = (ConnectionMode.QualcommDiag, "Qualcomm HS-USB Diagnostics 900E"),
        [(0x0E8D, 0x0003)] = (ConnectionMode.MediaTekBrom, "MediaTek BootROM"),
        [(0x0E8D, 0x2000)] = (ConnectionMode.MediaTekPreloader, "MediaTek Preloader"),
        [(0x05AC, 0x1227)] = (ConnectionMode.AppleDfu, "Apple DFU Mode"),
        [(0x05AC, 0x1280)] = (ConnectionMode.AppleRecovery, "Apple Recovery Mode"),
        [(0x05AC, 0x1281)] = (ConnectionMode.AppleRecovery, "Apple Recovery Mode"),
        [(0x05AC, 0x1282)] = (ConnectionMode.AppleRecovery, "Apple Recovery Mode"),
        [(0x05AC, 0x1283)] = (ConnectionMode.AppleRecovery, "Apple Recovery Mode"),
        [(0x18D1, 0x4EE0)] = (ConnectionMode.Fastboot, "Android Bootloader (Fastboot)"),
        [(0x18D1, 0xD00D)] = (ConnectionMode.Fastboot, "Android Bootloader (Fastboot)")
    };

    public static string? VendorName(ushort vendorId) => Vendors.GetValueOrDefault(vendorId);

    public static UsbClassification? Classify(ushort vendorId, ushort productId)
    {
        if (Known.TryGetValue((vendorId, productId), out var known))
            return new UsbClassification(VendorName(vendorId) ?? $"VID {vendorId:X4}", known.Mode, known.Description);

        // Apple mobile devices in normal mode use PIDs 0x12xx (excluding the recovery/DFU ids above).
        if (vendorId == 0x05AC && productId is >= 0x1290 and <= 0x12FF)
            return new UsbClassification("Apple", ConnectionMode.AppleNormal, "Apple Mobile Device");

        return VendorName(vendorId) is { } name
            ? new UsbClassification(name, ConnectionMode.Unknown, "Known vendor; mode decided by protocol detectors")
            : null;
    }

    /// <summary>Parses VID/PID from a Windows PnP instance id such as "USB\VID_04E8&amp;PID_685D\R58N…".</summary>
    public static bool TryParseInstanceId(string? instanceId, out ushort vendorId, out ushort productId)
    {
        vendorId = productId = 0;
        if (string.IsNullOrEmpty(instanceId)) return false;

        var upper = instanceId.ToUpperInvariant();
        var v = upper.IndexOf("VID_", StringComparison.Ordinal);
        var p = upper.IndexOf("PID_", StringComparison.Ordinal);
        if (v < 0 || p < 0 || v + 8 > upper.Length || p + 8 > upper.Length) return false;

        return ushort.TryParse(upper.AsSpan(v + 4, 4), System.Globalization.NumberStyles.HexNumber, null, out vendorId)
            && ushort.TryParse(upper.AsSpan(p + 4, 4), System.Globalization.NumberStyles.HexNumber, null, out productId);
    }
}

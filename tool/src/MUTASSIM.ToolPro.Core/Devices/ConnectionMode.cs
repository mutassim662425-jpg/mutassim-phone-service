namespace MUTASSIM.ToolPro.Core.Devices;

/// <summary>Every connection state the detection engine can report.</summary>
public enum ConnectionMode
{
    Unknown = 0,

    // Android (Google platform tools)
    Adb,
    AdbUnauthorized,
    AdbOffline,
    Recovery,
    Sideload,
    Fastboot,
    FastbootD,

    // Vendor / chipset modes
    SamsungDownload,
    Mtp,
    QualcommEdl,
    QualcommDiag,
    MediaTekPreloader,
    MediaTekBrom,

    // Apple
    AppleNormal,
    AppleRecovery,
    AppleDfu,

    // Generic
    UsbSerial
}

public enum DriverState
{
    Unknown = 0,
    Installed,
    Missing,
    Problem
}

public static class ConnectionModeExtensions
{
    public static string ToDisplayText(this ConnectionMode mode) => mode switch
    {
        ConnectionMode.Adb => "ADB",
        ConnectionMode.AdbUnauthorized => "ADB (Unauthorized)",
        ConnectionMode.AdbOffline => "ADB (Offline)",
        ConnectionMode.Recovery => "Recovery",
        ConnectionMode.Sideload => "ADB Sideload",
        ConnectionMode.Fastboot => "Fastboot",
        ConnectionMode.FastbootD => "FastbootD",
        ConnectionMode.SamsungDownload => "Download Mode",
        ConnectionMode.Mtp => "MTP",
        ConnectionMode.QualcommEdl => "Qualcomm EDL 9008",
        ConnectionMode.QualcommDiag => "Qualcomm Diag",
        ConnectionMode.MediaTekPreloader => "MediaTek Preloader",
        ConnectionMode.MediaTekBrom => "MediaTek BROM",
        ConnectionMode.AppleNormal => "Apple (Normal)",
        ConnectionMode.AppleRecovery => "Apple Recovery",
        ConnectionMode.AppleDfu => "Apple DFU",
        ConnectionMode.UsbSerial => "USB Serial",
        _ => "Unknown"
    };
}

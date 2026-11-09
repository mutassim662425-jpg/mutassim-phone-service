using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Core.Modules;
using static MUTASSIM.ToolPro.Core.Modules.Svc;

namespace MUTASSIM.ToolPro.Adb;

/// <summary>ADB services (engine implemented in Phases 2–3 on top of Google Platform-Tools adb.exe).</summary>
public sealed class AdbModule : ServiceModuleBase
{
    private static readonly ConnectionMode[] AdbModes = [ConnectionMode.Adb];

    public override string Id => ModuleIds.Adb;
    public override string DisplayName => "ADB Services";
    public override ModuleCategory Category => ModuleCategory.Protocol;
    public override string Description =>
        "Android Debug Bridge services through Google's official Platform-Tools. Requires USB debugging enabled and authorised on the phone.";

    public override IReadOnlyList<ServiceDescriptor> Services { get; } =
    [
        Built("adb.detect", "Detect ADB devices", 2, "Implemented; awaiting real-device test") with { RequiredModes = AdbModes },
        Built("adb.info", "Read device information (getprop)", 2, "Implemented; awaiting real-device test") with { RequiredModes = AdbModes },
        Built("adb.packages", "List installed packages", 3, "Implemented; awaiting real-device test") with { RequiredModes = AdbModes },
        Built("adb.install", "Install APK", 3, "Implemented; awaiting real-device test") with { RequiredModes = AdbModes },
        Built("adb.uninstall", "Uninstall user app", 3, "User-removable apps only") with { RequiredModes = AdbModes, IsDestructive = true },
        Built("adb.reboot", "Reboot", 3, "Implemented; awaiting real-device test") with { RequiredModes = AdbModes },
        Built("adb.reboot.recovery", "Reboot to Recovery", 3, "Implemented; awaiting real-device test") with { RequiredModes = AdbModes },
        Built("adb.reboot.bootloader", "Reboot to Bootloader", 3, "Implemented; awaiting real-device test") with { RequiredModes = AdbModes },
        Built("adb.reboot.download", "Reboot to Download Mode", 3, "Samsung devices that support it") with { RequiredModes = AdbModes },
        Built("adb.terminal", "ADB Terminal (validated commands)", 3, "Allow-list + explicit warning for risky commands"),
        Built("adb.log", "Command history & saved results", 3, "Implemented; awaiting real-device test")
    ];

    public override bool CanHandle(DeviceInfo device) =>
        device.Mode is ConnectionMode.Adb or ConnectionMode.AdbUnauthorized or ConnectionMode.AdbOffline
            or ConnectionMode.Recovery or ConnectionMode.Sideload;
}

using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Core.Modules;
using static MUTASSIM.ToolPro.Core.Modules.Svc;

namespace MUTASSIM.ToolPro.Fastboot;

public sealed class FastbootModule : ServiceModuleBase
{
    private static readonly ConnectionMode[] Modes = [ConnectionMode.Fastboot, ConnectionMode.FastbootD];

    public override string Id => ModuleIds.Fastboot;
    public override string DisplayName => "Fastboot Services";
    public override ModuleCategory Category => ModuleCategory.Protocol;
    public override string Description =>
        "Bootloader services through Google's official fastboot.exe. Flash/unlock only via OEM-sanctioned procedures, with model check, data-loss warning and explicit consent.";

    public override IReadOnlyList<ServiceDescriptor> Services { get; } =
    [
        Built("fb.detect", "Detect Fastboot devices", 2, "Implemented; awaiting real-device test") with { RequiredModes = Modes },
        Built("fb.getvar", "Read variables (getvar all)", 3, "Implemented; awaiting real-device test") with { RequiredModes = Modes },
        Built("fb.product", "Read product name", 3, "Implemented; awaiting real-device test") with { RequiredModes = Modes },
        Built("fb.lockstate", "Read bootloader lock state", 3, "Implemented; awaiting real-device test") with { RequiredModes = Modes },
        Built("fb.reboot", "Reboot to system", 3, "Implemented; awaiting real-device test") with { RequiredModes = Modes },
        Built("fb.reboot.recovery", "Reboot to Recovery", 3, "Implemented; awaiting real-device test") with { RequiredModes = Modes },
        Planned("fb.partitions", "List partitions (as exposed by device)", 3) with { RequiredModes = Modes },
        Planned("fb.fw.check", "Firmware compatibility check", 3) with { RequiredModes = Modes },
        Planned("fb.flash", "Flash official images", 3, "Model + image verification, data-loss warning, explicit consent") with { RequiredModes = Modes, IsDestructive = true },
        Authorized("fb.unlock", "Bootloader unlock (OEM official method)", 3,
            "Only where the OEM allows it (e.g. 'OEM unlocking' enabled, vendor unlock token). Erases user data.") with { RequiredModes = Modes, IsDestructive = true }
    ];

    public override bool CanHandle(DeviceInfo device) =>
        device.Mode is ConnectionMode.Fastboot or ConnectionMode.FastbootD;
}

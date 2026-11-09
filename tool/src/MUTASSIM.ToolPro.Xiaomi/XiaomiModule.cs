using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Core.Modules;
using static MUTASSIM.ToolPro.Core.Modules.Svc;

namespace MUTASSIM.ToolPro.Xiaomi;

public sealed class XiaomiModule : ServiceModuleBase
{
    public override string Id => ModuleIds.Xiaomi;
    public override string DisplayName => "Xiaomi / Redmi / POCO";
    public override ModuleCategory Category => ModuleCategory.Brand;
    public override string Description =>
        "Xiaomi, Redmi and POCO devices. Qualcomm and MediaTek models are separated and use different connection paths. " +
        "Bootloader unlock and account matters follow Xiaomi's official procedures only.";
    public override IReadOnlyList<string> Brands => ["xiaomi", "redmi", "poco"];
    public override IReadOnlyList<ushort> UsbVendorIds => [0x2717];

    public override IReadOnlyList<ServiceDescriptor> Services { get; } =
    [
        Built("mi.adb.info", "ADB information (model, build, MIUI/HyperOS version)", 6, "Implemented; awaiting real-device test") with { RequiredModes = [ConnectionMode.Adb] },
        Built("mi.fb.info", "Fastboot information (product, lock state)", 6, "Implemented; awaiting real-device test") with { RequiredModes = [ConnectionMode.Fastboot] },
        Built("mi.chipset", "Chipset family detection (Qualcomm / MediaTek)", 6, "Implemented; awaiting real-device test"),
        Planned("mi.rom.compat", "ROM compatibility check (codename / region / anti-rollback)", 6),
        Built("mi.reboot", "Reboot services", 6, "Implemented; awaiting real-device test"),
        Built("mi.diag", "Connection & driver diagnostics", 6, "Implemented; awaiting real-device test"),
        Planned("mi.flash", "Official fastboot ROM flashing", 6, "Verified fastboot ROM packages only") with { RequiredModes = [ConnectionMode.Fastboot], IsDestructive = true },
        Authorized("mi.unlock", "Bootloader unlock", 6, "Official Mi Unlock procedure with the owner's Mi Account only")
    ];
}

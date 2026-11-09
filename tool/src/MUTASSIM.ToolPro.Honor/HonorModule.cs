using MUTASSIM.ToolPro.Core.Modules;
using static MUTASSIM.ToolPro.Core.Modules.Svc;

namespace MUTASSIM.ToolPro.Honor;

public sealed class HonorModule : ServiceModuleBase
{
    public override string Id => ModuleIds.Honor;
    public override string DisplayName => "Honor";
    public override ModuleCategory Category => ModuleCategory.Brand;
    public override string Description =>
        "Honor devices (independent from Huawei since 2020; older models may still report Huawei identifiers).";
    public override IReadOnlyList<string> Brands => ["honor"];
    public override IReadOnlyList<ushort> UsbVendorIds => [0x339B];

    public override IReadOnlyList<ServiceDescriptor> Services { get; } =
    [
        Built("hn.info", "Device information (model, system version)", 6, "Implemented; awaiting real-device test"),
        Built("hn.usb", "USB diagnostics", 6, "Implemented; awaiting real-device test"),
        Built("hn.adb", "ADB tools (where permitted)", 6, "Implemented; awaiting real-device test"),
        Built("hn.fastboot", "Fastboot tools (where supported)", 6, "Implemented; awaiting real-device test"),
        Planned("hn.fw", "Firmware files & compatibility", 6),
        External("hn.recovery", "System recovery assistance", 6, "Uses Honor's official recovery tools"),
        Authorized("hn.service", "Authorised service functions", 6, "Require an authorised service account / Honor-approved tool")
    ];
}

using MUTASSIM.ToolPro.Core.Modules;
using static MUTASSIM.ToolPro.Core.Modules.Svc;

namespace MUTASSIM.ToolPro.Huawei;

public sealed class HuaweiModule : ServiceModuleBase
{
    public override string Id => ModuleIds.Huawei;
    public override string DisplayName => "Huawei";
    public override ModuleCategory Category => ModuleCategory.Brand;
    public override string Description =>
        "Huawei devices. Each service states whether it needs special authorisation, a certified account, or an external tool.";
    public override IReadOnlyList<string> Brands => ["huawei"];
    public override IReadOnlyList<ushort> UsbVendorIds => [0x12D1];

    public override IReadOnlyList<ServiceDescriptor> Services { get; } =
    [
        Built("hw.info", "Device information (model, system version)", 6, "Implemented; awaiting real-device test"),
        Built("hw.usb", "USB diagnostics", 6, "Implemented; awaiting real-device test"),
        Built("hw.adb", "ADB tools (where permitted)", 6, "Implemented; awaiting real-device test"),
        Built("hw.fastboot", "Fastboot tools (where supported)", 6, "Implemented; awaiting real-device test"),
        Planned("hw.fw", "Firmware files & compatibility", 6),
        External("hw.recovery", "System recovery assistance", 6, "Uses Huawei's official recovery (HiSuite / eRecovery)"),
        Authorized("hw.service", "Authorised service functions", 6, "Require an authorised service account / Huawei-approved tool")
    ];
}

using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Core.Modules;
using static MUTASSIM.ToolPro.Core.Modules.Svc;

namespace MUTASSIM.ToolPro.Apple;

public sealed class AppleModule : ServiceModuleBase
{
    public override string Id => ModuleIds.Apple;
    public override string DisplayName => "Apple (iPhone / iPad)";
    public override ModuleCategory Category => ModuleCategory.Brand;
    public override string Description =>
        "iPhone/iPad detection and information through Apple-supported interfaces (Apple Mobile Device Support). " +
        "Activation Lock, passcodes and Apple security protections are never bypassed.";
    public override IReadOnlyList<string> Brands => ["apple"];
    public override IReadOnlyList<ushort> UsbVendorIds => [0x05AC];

    public override IReadOnlyList<ServiceDescriptor> Services { get; } =
    [
        Built("ios.vidpid", "Identify Normal / Recovery / DFU by VID:PID", 1, "Classifier ready; live detection in Phase 6"),
        Planned("ios.info", "Device name, model, iOS version", 6),
        Built("ios.usb", "USB diagnostics & Apple driver status", 6, "Implemented; awaiting real-device test"),
        Planned("ios.fw", "Firmware (IPSW) information & signing status", 6),
        External("ios.restore", "Official restore / update", 6, "Performed with Apple's official tools (Apple Devices app / Finder)"),
        Unsupported("ios.bypass", "Activation Lock / passcode bypass", "Not provided by design")
    ];

    public override bool CanHandle(DeviceInfo device) =>
        device.Mode is ConnectionMode.AppleNormal or ConnectionMode.AppleRecovery or ConnectionMode.AppleDfu || base.CanHandle(device);
}

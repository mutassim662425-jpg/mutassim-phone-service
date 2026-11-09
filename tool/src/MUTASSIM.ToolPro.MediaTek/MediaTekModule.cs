using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Core.Modules;
using static MUTASSIM.ToolPro.Core.Modules.Svc;

namespace MUTASSIM.ToolPro.MediaTek;

public sealed class MediaTekModule : ServiceModuleBase
{
    public override string Id => ModuleIds.MediaTek;
    public override string DisplayName => "MediaTek";
    public override ModuleCategory Category => ModuleCategory.Chipset;
    public override string Description =>
        "Detection and diagnostics of MediaTek Preloader / BROM interfaces. Privileged access only through authorised mechanisms; " +
        "authentication (SLA/DAA) and security protections are never bypassed.";
    public override IReadOnlyList<ushort> UsbVendorIds => [0x0E8D];

    public override IReadOnlyList<ServiceDescriptor> Services { get; } =
    [
        Built("mtk.vidpid", "Identify Preloader / BROM by VID:PID", 1, "Classifier ready; live USB detection in Phase 5"),
        Built("mtk.usb", "USB information & connection type", 5, "Implemented; awaiting real-device test"),
        Planned("mtk.chip", "Chipset information (when reported)", 5),
        Built("mtk.drivers", "MediaTek driver management", 5, "Implemented; awaiting real-device test"),
        Planned("mtk.diag", "Connection diagnostics", 5),
        Planned("mtk.fw", "Firmware (scatter) verification & compatibility", 5),
        Authorized("mtk.restore", "Officially supported system restore", 5,
            "Uses OEM-provided, signed download agents and authorised accounts where the device requires them")
            with { IsDestructive = true }
    ];

    public override bool CanHandle(DeviceInfo device) =>
        device.Mode is ConnectionMode.MediaTekBrom or ConnectionMode.MediaTekPreloader || base.CanHandle(device);
}

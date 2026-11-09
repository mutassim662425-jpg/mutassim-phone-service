using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Core.Modules;
using static MUTASSIM.ToolPro.Core.Modules.Svc;

namespace MUTASSIM.ToolPro.Qualcomm;

public sealed class QualcommModule : ServiceModuleBase
{
    public override string Id => ModuleIds.Qualcomm;
    public override string DisplayName => "Qualcomm";
    public override ModuleCategory Category => ModuleCategory.Chipset;
    public override string Description =>
        "Detection and diagnostics of Qualcomm USB interfaces (EDL 9008 / Diag). Any Firehose, signing or authentication step runs only with " +
        "files that are authorised for and compatible with the device. No exploits or protection-bypass loaders.";
    public override IReadOnlyList<ushort> UsbVendorIds => [0x05C6];

    public override IReadOnlyList<ServiceDescriptor> Services { get; } =
    [
        Built("qc.vidpid", "Identify EDL 9008 / Diag by VID:PID", 1, "Classifier ready; live USB detection in Phase 5") with { RequiredModes = [ConnectionMode.QualcommEdl] },
        Built("qc.port", "COM port & driver status", 5, "Implemented; awaiting real-device test"),
        Planned("qc.usbhealth", "USB link health check", 5),
        Planned("qc.model", "Model identification (when the interface reports it)", 5),
        Planned("qc.requirements", "Show required authorised files per device", 5),
        Authorized("qc.firehose", "Firehose-based service operations", 5,
            "Only with OEM-signed, device-matching programmer files and any required OEM authentication")
            with { RequiredModes = [ConnectionMode.QualcommEdl], IsDestructive = true }
    ];

    public override bool CanHandle(DeviceInfo device) =>
        device.Mode is ConnectionMode.QualcommEdl or ConnectionMode.QualcommDiag || base.CanHandle(device);
}

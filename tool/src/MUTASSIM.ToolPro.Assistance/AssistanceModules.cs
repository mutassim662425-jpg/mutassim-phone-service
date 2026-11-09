using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Core.Modules;
using static MUTASSIM.ToolPro.Core.Modules.Svc;

namespace MUTASSIM.ToolPro.Assistance;

/// <summary>
/// FRP (Google Factory Reset Protection) assistance: diagnosis and the owner's official recovery path.
/// No unauthorised bypass, no exploitation of system vulnerabilities.
/// </summary>
public sealed class FrpModule : ServiceModuleBase
{
    public override string Id => ModuleIds.Frp;
    public override string DisplayName => "FRP Services";
    public override ModuleCategory Category => ModuleCategory.Service;
    public override string Description =>
        "Google account verification assistance for the device owner: diagnosis, official Google account recovery and manufacturer procedures with proof of ownership.";

    public override IReadOnlyList<ServiceDescriptor> Services { get; } =
    [
        Built("frp.state", "Detect whether account verification is requested (when exposed)", 6, "Implemented; awaiting real-device test"),
        Built("frp.info", "Show model and OS version", 6, "Implemented; awaiting real-device test"),
        Built("frp.diagnose", "Diagnose account-access problem (official recovery path)", 6, "Backed by OfficialRecoveryGuide; awaiting real-device test"),
        Built("frp.guide", "Google account recovery guidance for the owner", 6, "Implemented; awaiting real-device test"),
        Built("frp.oem", "Manufacturer official procedure & proof-of-ownership checklist", 6, "Backed by OfficialRecoveryGuide (per-brand)"),
        Planned("frp.ticket", "Service request tracking", 6),
        Unsupported("frp.bypass", "Unauthorised FRP bypass", "Not provided by design")
    ];

    public override bool CanHandle(DeviceInfo device) => false;
}

/// <summary>Enterprise device management (MDM) assistance — de-enrolment only through the organisation's admin.</summary>
public sealed class MdmModule : ServiceModuleBase
{
    public override string Id => ModuleIds.Mdm;
    public override string DisplayName => "MDM Services";
    public override ModuleCategory Category => ModuleCategory.Service;
    public override string Description =>
        "Enterprise-managed device assistance: detect management (when the device exposes it), show the managing organisation, " +
        "and organise de-enrolment requests through the organisation's administrator or approved console.";

    public override IReadOnlyList<ServiceDescriptor> Services { get; } =
    [
        Built("mdm.detect", "Detect device-owner / work-profile management (ADB, when permitted)", 6, "Implemented; awaiting real-device test") with { RequiredModes = [ConnectionMode.Adb] },
        Built("mdm.info", "Show management status & organisation details", 6, "Implemented; awaiting real-device test"),
        Planned("mdm.diagnose", "Diagnose enrolment problems", 6),
        Planned("mdm.request", "De-enrolment request workflow", 6),
        Authorized("mdm.release", "Release / de-enrolment", 6, "Executed only by the organisation's administrator or approved MDM console"),
        Unsupported("mdm.bypass", "Unauthorised MDM bypass", "Not provided by design")
    ];

    public override bool CanHandle(DeviceInfo device) => false;
}

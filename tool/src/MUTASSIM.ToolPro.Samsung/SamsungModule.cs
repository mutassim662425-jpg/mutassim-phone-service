using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Core.Modules;
using static MUTASSIM.ToolPro.Core.Modules.Svc;

namespace MUTASSIM.ToolPro.Samsung;

public sealed class SamsungModule : ServiceModuleBase
{
    public override string Id => ModuleIds.Samsung;
    public override string DisplayName => "Samsung";
    public override ModuleCategory Category => ModuleCategory.Brand;
    public override string Description =>
        "Samsung Galaxy devices: information via ADB, Download Mode detection, firmware manager for BL/AP/CP/CSC/HOME_CSC. " +
        "Flashing uses only trusted, legally usable tools/protocols; no claim of full compatibility with Samsung's closed protocol.";
    public override IReadOnlyList<string> Brands => ["samsung"];
    public override IReadOnlyList<ushort> UsbVendorIds => [0x04E8];

    public override IReadOnlyList<ServiceDescriptor> Services { get; } =
    [
        Built("sam.info", "Model / CSC / Firmware / Binary / Security patch", 4, "Read via ADB properties") with { RequiredModes = [ConnectionMode.Adb] },
        Built("sam.dl.detect", "Download Mode detection", 4, "USB 04E8:685D") with { RequiredModes = [ConnectionMode.SamsungDownload] },
        Built("sam.dl.info", "Download Mode connection details", 4, "Implemented; awaiting real-device test") with { RequiredModes = [ConnectionMode.SamsungDownload] },
        Built("sam.fw.parse", "Firmware set analysis BL/AP/CP/CSC/HOME_CSC", 4, "Samsung tar.md5: implemented; awaiting real firmware test"),
        Built("sam.fw.verify", "Firmware integrity (tar.md5) & model match", 4, "Samsung tar.md5: implemented; awaiting real firmware test"),
        Built("sam.reboot.download", "Reboot to Download Mode (ADB)", 3, "Implemented; awaiting real-device test") with { RequiredModes = [ConnectionMode.Adb] },
        External("sam.flash", "Firmware flashing", 4,
            "Requires a trusted, redistributable implementation of the Download-Mode protocol; will be enabled only after validation on real devices.")
            with { RequiredModes = [ConnectionMode.SamsungDownload], IsDestructive = true }
    ];
}

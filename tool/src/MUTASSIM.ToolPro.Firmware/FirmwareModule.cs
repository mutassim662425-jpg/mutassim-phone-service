using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Core.Modules;
using static MUTASSIM.ToolPro.Core.Modules.Svc;

namespace MUTASSIM.ToolPro.Firmware;

public sealed class FirmwareModule : ServiceModuleBase
{
    public override string Id => ModuleIds.Firmware;
    public override string DisplayName => "Firmware Manager";
    public override ModuleCategory Category => ModuleCategory.System;
    public override string Description =>
        "Central local firmware library. Files are added manually from official/trusted sources — nothing is auto-downloaded from unknown sources.";

    public override IReadOnlyList<ServiceDescriptor> Services { get; } =
    [
        Built("fw.sha256", "SHA-256 integrity check (with expected-hash verification)", 1,
            "Streaming hash with progress; awaiting first run on Windows"),
        Built("fw.dragdrop", "Add files by Drag & Drop / Browse", 1, "Awaiting first run on Windows"),
        Planned("fw.library", "Library organised by brand / model / version", 4),
        Planned("fw.search", "Search by model and OS version", 4),
        Built("fw.metadata", "Metadata extraction (Samsung BL/AP/CP/CSC/HOME_CSC tar.md5)", 4, "Samsung tar.md5: implemented; awaiting real firmware test"),
        Built("fw.missing", "Missing-file detection", 4, "Samsung tar.md5: implemented; awaiting real firmware test"),
        Built("fw.compat", "Compatibility check against connected device", 4, "Samsung tar.md5: implemented; awaiting real firmware test"),
        Planned("fw.usage", "Per-firmware usage history", 4),
        Planned("fw.importexport", "Import / export firmware lists", 4)
    ];

    public override bool CanHandle(DeviceInfo device) => false;
}

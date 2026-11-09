using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Core.Modules;
using static MUTASSIM.ToolPro.Core.Modules.Svc;

namespace MUTASSIM.ToolPro.MultiBrand;

/// <summary>
/// Unified multi-brand module (section 10 of the spec): one engine, one profile per company.
/// The chipset, model and connection mode are resolved before any service is offered;
/// two phones of the same brand are never assumed to share a protocol.
/// </summary>
public sealed class BrandProfileModule(
    string id,
    string displayName,
    string[] brands,
    ushort[] usbVendorIds,
    string chipsetNote) : ServiceModuleBase
{
    public override string Id { get; } = id;
    public override string DisplayName { get; } = displayName;
    public override ModuleCategory Category => ModuleCategory.Brand;
    public override string Description { get; } =
        $"{displayName} profile on the unified multi-brand engine. {chipsetNote} " +
        "Services are offered only after chipset, model and connection mode are confirmed.";
    public override IReadOnlyList<string> Brands { get; } = brands;
    public override IReadOnlyList<ushort> UsbVendorIds { get; } = usbVendorIds;

    public override IReadOnlyList<ServiceDescriptor> Services { get; } =
    [
        Built($"{id}.detect", "Device detection", 6, "Implemented; awaiting real-device test"),
        Built($"{id}.info", "Device information", 6, "Implemented; awaiting real-device test") with { RequiredModes = [ConnectionMode.Adb] },
        Built($"{id}.adb", "ADB services", 6, "Implemented; awaiting real-device test") with { RequiredModes = [ConnectionMode.Adb] },
        Built($"{id}.fastboot", "Fastboot services (when supported)", 6, "Implemented; awaiting real-device test") with { RequiredModes = [ConnectionMode.Fastboot] },
        Planned($"{id}.fw.info", "Firmware information", 6),
        Planned($"{id}.fw.compat", "Firmware compatibility", 6),
        Built($"{id}.usb", "USB diagnostics & drivers", 6, "Implemented; awaiting real-device test"),
        Built($"{id}.reboot", "Reboot services", 6, "Implemented; awaiting real-device test"),
        Planned($"{id}.flash", "Supported official flashing", 6, "Per model; only verified official packages") with { IsDestructive = true },
        Built($"{id}.history", "Operation history", 6, "Implemented; awaiting real-device test")
    ];

    /// <summary>All built-in brand profiles.</summary>
    public static IEnumerable<BrandProfileModule> CreateBuiltIn() =>
    [
        new(ModuleIds.Oppo, "OPPO", ["oppo"], [0x22D9], "Qualcomm and MediaTek models exist."),
        new(ModuleIds.Realme, "Realme", ["realme"], [], "Qualcomm, MediaTek and Unisoc models exist."),
        new(ModuleIds.Vivo, "Vivo", ["vivo"], [0x2D95], "Qualcomm and MediaTek models exist."),
        new(ModuleIds.Tecno, "Tecno", ["tecno"], [], "Mostly MediaTek / Unisoc."),
        new(ModuleIds.Infinix, "Infinix", ["infinix"], [], "Mostly MediaTek / Unisoc."),
        new(ModuleIds.Motorola, "Motorola", ["motorola"], [0x22B8], "Qualcomm and MediaTek models exist."),
        new(ModuleIds.Nokia, "Nokia (HMD)", ["nokia", "hmd global"], [0x2E04], "Qualcomm, MediaTek and Unisoc models exist."),
        new(ModuleIds.GooglePixel, "Google Pixel", ["google"], [0x18D1], "Qualcomm / Google Tensor."),
        new(ModuleIds.OnePlus, "OnePlus", ["oneplus"], [0x2A70], "Mostly Qualcomm, some MediaTek."),
        new(ModuleIds.Zte, "ZTE", ["zte"], [0x19D2], "Qualcomm, MediaTek and Unisoc models exist."),
        new(ModuleIds.Lenovo, "Lenovo", ["lenovo"], [0x17EF], "Qualcomm and MediaTek models exist."),
        new(ModuleIds.Sony, "Sony", ["sony"], [0x0FCE], "Qualcomm."),
        new(ModuleIds.Lg, "LG", ["lge", "lg"], [0x1004], "Legacy devices (LG left the phone market in 2021).")
    ];
}

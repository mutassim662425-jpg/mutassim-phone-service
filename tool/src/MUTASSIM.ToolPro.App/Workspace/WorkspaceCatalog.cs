using MUTASSIM.ToolPro.Adb;
using MUTASSIM.ToolPro.Core.Modules;
using MUTASSIM.ToolPro.Fastboot;
using static MUTASSIM.ToolPro.App.Workspace.ActionLibrary;

namespace MUTASSIM.ToolPro.App.Workspace;

/// <summary>
/// The brand / chipset / protocol tabs. Only implemented actions appear as buttons; every other service of
/// the tab's modules is listed (without a button) in the automatic "Planned" group.
/// </summary>
public static class WorkspaceCatalog
{
    private static ActionGroup Info(string prefix, params BrandKeys[] keys) =>
        new("Information", "المعلومات", [ReadInfoDevice, ReadInfoAdb($"{prefix}.info.adb", keys), ReadInfoFastboot($"{prefix}.info.fb")]);

    private static ActionGroup AdbReboots(params AdbRebootTarget[] extra) =>
        new("Reboot (ADB)", "إعادة التشغيل (ADB)",
        [
            AdbReboot(AdbRebootTarget.System), AdbReboot(AdbRebootTarget.Recovery),
            AdbReboot(AdbRebootTarget.Bootloader), AdbReboot(AdbRebootTarget.FastbootD),
            .. extra.Select(AdbReboot)
        ]);

    private static readonly ActionGroup FastbootReboots =
        new("Reboot (Fastboot)", "إعادة التشغيل (Fastboot)",
        [
            FastbootReboot(FastbootRebootTarget.System), FastbootReboot(FastbootRebootTarget.Bootloader),
            FastbootReboot(FastbootRebootTarget.Recovery), FastbootReboot(FastbootRebootTarget.FastbootD)
        ]);

    private static WorkspaceDefinition Brand(string route, string en, string ar, string[] modules, BrandKeys[] keys,
        params ActionGroup[] extraGroups) =>
        new(route, en, ar, modules, [Info(route, keys), AdbReboots(), FastbootReboots, .. extraGroups]);

    public static readonly IReadOnlyList<WorkspaceDefinition> All =
    [
        new("ws.samsung", "Samsung", "Samsung", [ModuleIds.Samsung],
        [
            Info("ws.samsung", SamsungKeys),
            new("Download Mode", "وضع Download",
            [
                AdbReboot(AdbRebootTarget.Download),
                ScanUsb("sam.dl.usb", "Download Mode Info (USB)", "معلومات وضع Download (USB)", 0x04E8)
            ]),
            new("Firmware", "الفلاشة", [SamsungFirmwareCheck]),
            AdbReboots(),
            FastbootReboots
        ]),
        Brand("ws.xiaomi", "Xiaomi", "Xiaomi", [ModuleIds.Xiaomi], XiaomiKeys,
            new ActionGroup("EDL", "EDL", [AdbReboot(AdbRebootTarget.Edl)])),
        Brand("ws.huawei", "Huawei / Honor", "Huawei / Honor", [ModuleIds.Huawei, ModuleIds.Honor], HuaweiKeys),
        Brand("ws.oppo", "OPPO / Realme", "OPPO / Realme", [ModuleIds.Oppo, ModuleIds.Realme], OppoKeys),
        Brand("ws.vivo", "Vivo", "Vivo", [ModuleIds.Vivo], VivoKeys),
        Brand("ws.tecno", "Tecno / Infinix", "Tecno / Infinix", [ModuleIds.Tecno, ModuleIds.Infinix], []),
        Brand("ws.moto", "Motorola / Nokia", "Motorola / Nokia", [ModuleIds.Motorola, ModuleIds.Nokia], []),
        Brand("ws.other", "Other", "أخرى",
            [ModuleIds.GooglePixel, ModuleIds.OnePlus, ModuleIds.Zte, ModuleIds.Lenovo, ModuleIds.Sony, ModuleIds.Lg], []),
        new("ws.apple", "Apple", "Apple", [ModuleIds.Apple],
        [
            new("USB", "USB", [ReadInfoDevice, ScanUsb("ios.usb", "Scan Apple USB (Normal / Recovery / DFU)", "فحص أجهزة Apple (USB)", 0x05AC)])
        ]),
        new("ws.qualcomm", "Qualcomm", "Qualcomm", [ModuleIds.Qualcomm],
        [
            new("Ports", "المنافذ", [ReadInfoDevice, ScanUsb("qc.ports", "Scan Qualcomm Ports (EDL / Diag)", "فحص منافذ Qualcomm", 0x05C6)]),
            new("EDL", "EDL", [AdbReboot(AdbRebootTarget.Edl)])
        ]),
        new("ws.mediatek", "MediaTek", "MediaTek", [ModuleIds.MediaTek],
        [
            new("Ports", "المنافذ", [ReadInfoDevice, ScanUsb("mtk.ports", "Scan MediaTek Ports (BROM / Preloader)", "فحص منافذ MediaTek", 0x0E8D)])
        ]),
        new("ws.adb", "ADB", "ADB", [ModuleIds.Adb],
        [
            Info("ws.adb"),
            new("Apps", "التطبيقات", [ListApps(true), ListApps(false), InstallApk, UninstallUserApp]),
            AdbReboots(AdbRebootTarget.Download, AdbRebootTarget.Sideload)
        ], TerminalKind.Adb),
        new("ws.fastboot", "Fastboot", "Fastboot", [ModuleIds.Fastboot],
        [
            new("Information", "المعلومات", [ReadInfoFastboot("ws.fastboot.info")]),
            FastbootReboots
        ], TerminalKind.Fastboot),
        new("ws.frp", "FRP", "FRP", [ModuleIds.Frp],
        [
            new("Owner assistance", "مساعدة المالك", [FrpInfo, OpenGoogleRecovery])
        ]),
        new("ws.mdm", "MDM", "MDM", [ModuleIds.Mdm],
        [
            new("Management", "الإدارة", [MdmInfo])
        ])
    ];

    public static WorkspaceDefinition? Find(string route) => All.FirstOrDefault(w => w.Route == route);

    public static string? RouteForModule(string moduleId) =>
        All.FirstOrDefault(w => w.ModuleIds.Contains(moduleId, StringComparer.OrdinalIgnoreCase))?.Route;
}

using MUTASSIM.ToolPro.Adb;
using MUTASSIM.ToolPro.Api.Assist;
using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Fastboot;
using static MUTASSIM.ToolPro.App.Workspace.ActionLibrary;

namespace MUTASSIM.ToolPro.App.Workspace;

/// <summary>
/// Turns a server-recommended action ID into one of the tool's OWN built-in actions. The server never sends
/// commands: an unknown ID resolves to null and is not run. Every action keeps its normal mode check,
/// confirmation dialog and operation history.
/// </summary>
public static class AssistActionMap
{
    public static ActionDefinition? Resolve(string actionId, DeviceInfo? device)
    {
        if (!AssistKnownActions.All.Contains(actionId)) return null;

        return actionId switch
        {
            "device.info" => ReadInfoDevice,
            "adb.info" => ReadInfoAdb("adb.info", BrandKeysFor(device)),
            "fb.info" => ReadInfoFastboot("fb.info"),
            "usb.scan" => ScanUsb("usb.scan", "Scan USB ports & drivers", "فحص منافذ USB والتعريفات"),

            "adb.reboot.system" => AdbReboot(AdbRebootTarget.System),
            "adb.reboot.recovery" => AdbReboot(AdbRebootTarget.Recovery),
            "adb.reboot.bootloader" => AdbReboot(AdbRebootTarget.Bootloader),
            "adb.reboot.fastbootd" => AdbReboot(AdbRebootTarget.FastbootD),
            "adb.reboot.download" => AdbReboot(AdbRebootTarget.Download),
            "adb.reboot.edl" => AdbReboot(AdbRebootTarget.Edl),

            "fb.reboot.system" => FastbootReboot(FastbootRebootTarget.System),
            "fb.reboot.recovery" => FastbootReboot(FastbootRebootTarget.Recovery),
            "fb.reboot.bootloader" => FastbootReboot(FastbootRebootTarget.Bootloader),
            "fb.reboot.fastbootd" => FastbootReboot(FastbootRebootTarget.FastbootD),

            "adb.apps.user" => ListApps(true),
            "adb.uninstall" => UninstallUserApp,
            "sam.fw.check" => SamsungFirmwareCheck,
            "frp.info" => FrpInfo,
            "frp.google" => OpenGoogleRecovery,
            "mdm.info" => MdmInfo,
            _ => null
        };
    }

    /// <summary>The local definition decides whether a step is dangerous, not the server.</summary>
    public static bool IsDangerous(ActionDefinition action) =>
        action.IsDangerous || action.ConfirmEn is not null || action.Id == UninstallUserApp.Id;

    private static BrandKeys[] BrandKeysFor(DeviceInfo? device)
    {
        var b = $"{device?.Brand} {device?.Manufacturer}".ToLowerInvariant();
        if (b.Contains("samsung")) return SamsungKeys;
        if (b.Contains("xiaomi") || b.Contains("redmi") || b.Contains("poco")) return XiaomiKeys;
        if (b.Contains("huawei") || b.Contains("honor")) return HuaweiKeys;
        if (b.Contains("oppo") || b.Contains("realme") || b.Contains("oneplus")) return OppoKeys;
        if (b.Contains("vivo") || b.Contains("iqoo")) return VivoKeys;
        return [];
    }
}

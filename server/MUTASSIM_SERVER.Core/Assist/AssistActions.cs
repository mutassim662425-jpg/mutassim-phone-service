namespace MUTASSIM_SERVER.Core.Assist;

/// <summary>
/// The only action IDs the server may recommend. Each one is a built-in, already-reviewed function of
/// MUTASSIM TOOL PRO (ActionLibrary). Anything not listed here is never sent and never executed.
/// There is deliberately no FRP / MDM / Activation-Lock bypass, IMEI or partition-write action.
/// </summary>
public static class AssistActions
{
    public sealed record ActionInfo(string Id, string TitleEn, string TitleAr, bool Dangerous);

    public static readonly IReadOnlyDictionary<string, ActionInfo> All =
        new ActionInfo[]
        {
            new("device.info", "Read device info (USB)", "قراءة معلومات الجهاز (USB)", false),
            new("adb.info", "Read full info (ADB)", "قراءة المعلومات الكاملة (ADB)", false),
            new("fb.info", "Read info (Fastboot)", "قراءة المعلومات (Fastboot)", false),
            new("usb.scan", "Scan USB ports & drivers", "فحص منافذ USB والتعريفات", false),

            new("adb.reboot.system", "Reboot system", "إعادة تشغيل النظام", false),
            new("adb.reboot.recovery", "Reboot to Recovery", "إعادة التشغيل إلى Recovery", false),
            new("adb.reboot.bootloader", "Reboot to Bootloader", "إعادة التشغيل إلى Bootloader", false),
            new("adb.reboot.fastbootd", "Reboot to FastbootD", "إعادة التشغيل إلى FastbootD", false),
            new("adb.reboot.download", "Reboot to Download mode", "إعادة التشغيل إلى Download", false),
            new("adb.reboot.edl", "Reboot to EDL (9008)", "إعادة التشغيل إلى EDL", true),

            new("fb.reboot.system", "Reboot system (Fastboot)", "إعادة تشغيل النظام (Fastboot)", false),
            new("fb.reboot.recovery", "Reboot to Recovery (Fastboot)", "إعادة التشغيل إلى Recovery (Fastboot)", false),
            new("fb.reboot.bootloader", "Reboot to Bootloader (Fastboot)", "إعادة التشغيل إلى Bootloader (Fastboot)", false),
            new("fb.reboot.fastbootd", "Reboot to FastbootD (Fastboot)", "إعادة التشغيل إلى FastbootD (Fastboot)", false),

            new("adb.apps.user", "List user apps", "عرض تطبيقات المستخدم", false),
            new("adb.uninstall", "Uninstall a user app", "إزالة تطبيق مستخدم", true),

            new("sam.fw.check", "Check Samsung firmware files", "فحص ملفات فلاشة Samsung", false),

            new("frp.info", "Read FRP / setup state", "قراءة حالة FRP والإعداد", false),
            new("frp.google", "Open Google Account Recovery (official)", "فتح استعادة حساب Google (رسمي)", false),
            new("mdm.info", "Check device management (MDM)", "فحص الإدارة المؤسسية (MDM)", false),
        }.ToDictionary(a => a.Id, StringComparer.Ordinal);

    public static bool IsAllowed(string? id) => id is not null && All.ContainsKey(id);
}

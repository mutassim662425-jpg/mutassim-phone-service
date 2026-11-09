namespace MUTASSIM_SERVER.Core.Assist;

/// <summary>
/// Rule-based solution engine: (problem + connection mode + brand + chipset) → ordered steps made only of
/// allow-listed tool actions, plus text guidance for what must be done by hand or officially.
/// Pure and deterministic, so it is fully unit-testable.
/// </summary>
public static class SolutionEngine
{
    public static readonly IReadOnlyList<AssistProblem> Problems =
    [
        new("identify", "Identify the phone / read full info", "التعرف على الهاتف وقراءة معلوماته"),
        new("not_detected", "Phone not detected by the PC", "الهاتف غير معرّف على الكمبيوتر"),
        new("unauthorized", "USB debugging not authorized", "تصحيح USB غير مصرّح"),
        new("bootloop", "Stuck on logo / bootloop / won't start", "معلّق على الشعار / ريستارت متكرر / لا يقلع"),
        new("stuck_fastboot", "Stuck in Fastboot / Bootloader", "معلّق في وضع Fastboot"),
        new("stuck_recovery", "Stuck in Recovery", "معلّق في وضع Recovery"),
        new("flash_prepare", "Prepare for official firmware flashing", "تجهيز الهاتف لتفليش رسمي"),
        new("app_problem", "A user app is causing problems", "تطبيق يسبب مشاكل"),
        new("account_locked", "Google account verification after reset (FRP)", "طلب حساب Google بعد الفورمات (FRP)"),
        new("managed_device", "Phone is managed by an organisation (MDM)", "الهاتف تابع لجهة/شركة (MDM)"),
        new("edl", "Qualcomm EDL (9008) mode needed", "الحاجة لوضع EDL (9008) لمعالجات Qualcomm"),
    ];

    public static bool IsKnownProblem(string? code) =>
        code is not null && Problems.Any(p => p.Code == code);

    public static AssistResponse Diagnose(Guid sessionId, string problem, AssistDevice? device)
    {
        var mode = ModeGroup(device?.Mode);
        var brand = BrandGroup(device);
        var qualcomm = IsQualcomm(device);
        var plan = new Plan();

        // Problems that need a working connection first.
        var needsConnection = problem is not ("account_locked" or "not_detected" or "unauthorized" or "flash_prepare");
        if (needsConnection && mode is "none" or "mtp" or "unauthorized" or "offline")
        {
            ConnectionPlan(plan, mode);
            plan.Summary(
                "The phone must be connected in a usable mode first. Fix the connection, then ask the server again.",
                "يجب توصيل الهاتف بوضع صالح أولاً. أصلح الاتصال ثم اطلب الحل مرة أخرى.");
            return plan.Build(sessionId, problem, mode);
        }

        switch (problem)
        {
            case "identify":
                Identify(plan, mode);
                plan.Summary("Read everything the phone reports in its current mode.",
                             "قراءة كل ما يعرضه الهاتف في وضعه الحالي.");
                break;

            case "not_detected":
                if (mode is "adb" or "fastboot" or "recovery" or "download" or "edl" or "mtk")
                {
                    plan.Summary("The phone IS detected by the tool. Reading its information.",
                                 "الهاتف معرّف بالفعل في الأداة. سيتم قراءة معلوماته.");
                    Identify(plan, mode);
                }
                else
                {
                    ConnectionPlan(plan, mode);
                    plan.Summary("Check USB, drivers and USB debugging.", "فحص منفذ USB والتعريفات وتصحيح USB.");
                }
                break;

            case "unauthorized":
                if (mode == "adb")
                {
                    plan.Summary("USB debugging is already authorized.", "تصحيح USB مصرّح بالفعل.");
                    plan.Step("adb.info", "Confirm the connection by reading info.", "تأكيد الاتصال بقراءة المعلومات.");
                }
                else
                {
                    ConnectionPlan(plan, "unauthorized");
                    plan.Summary("Authorize this PC on the phone screen.", "اسمح لهذا الكمبيوتر من شاشة الهاتف.");
                }
                break;

            case "bootloop":
                Bootloop(plan, mode, brand);
                break;

            case "stuck_fastboot":
                if (mode == "fastboot")
                {
                    plan.Summary("Leave Fastboot and start Android.", "الخروج من Fastboot وتشغيل أندرويد.");
                    plan.Step("fb.info", "Record bootloader state before changing anything.", "تسجيل حالة البوت لودر قبل أي تغيير.");
                    plan.Step("fb.reboot.system", "Ask the bootloader to start Android.", "طلب تشغيل أندرويد من البوت لودر.");
                    plan.Note("If it returns to Fastboot, the system partition is damaged: use the bootloop solution.",
                              "إذا عاد إلى Fastboot فهناك تلف في النظام: استخدم حل الريستارت المتكرر.");
                }
                else NotInMode(plan, mode, "Fastboot");
                break;

            case "stuck_recovery":
                if (mode == "recovery")
                {
                    plan.Summary("Leave Recovery and start Android.", "الخروج من Recovery وتشغيل أندرويد.");
                    plan.Step("device.info", "Confirm the phone and mode.", "تأكيد الهاتف والوضع.");
                    plan.Step("adb.reboot.system", "Reboot to Android (works when Recovery exposes ADB).", "إعادة التشغيل إلى أندرويد (يعمل إذا كان Recovery يدعم ADB).");
                    plan.Note("If ADB is not available in Recovery, choose \"Reboot system now\" on the phone with the volume and power keys.",
                              "إذا لم يتوفر ADB في Recovery اختر \"Reboot system now\" من الهاتف بأزرار الصوت والتشغيل.");
                }
                else NotInMode(plan, mode, "Recovery");
                break;

            case "flash_prepare":
                FlashPrepare(plan, mode, brand);
                break;

            case "app_problem":
                if (mode == "adb")
                {
                    plan.Summary("Find the problem app and remove it (user apps only).",
                                 "تحديد التطبيق المسبب للمشكلة وإزالته (تطبيقات المستخدم فقط).");
                    plan.Step("adb.apps.user", "List installed user apps to find the package name.", "عرض تطبيقات المستخدم لمعرفة اسم الحزمة.");
                    plan.Step("adb.uninstall", "Uninstall the chosen app. Its data is deleted.", "إزالة التطبيق المختار. ستُحذف بياناته.");
                    plan.Note("Ask the owner before removing any app.", "استأذن صاحب الهاتف قبل إزالة أي تطبيق.");
                }
                else NotInMode(plan, mode, "ADB");
                break;

            case "account_locked":
                plan.Summary("Only the owner can unlock the account, through Google or the manufacturer.",
                             "فك الحساب يتم فقط بواسطة المالك عن طريق Google أو الشركة المصنّعة.");
                if (mode == "adb")
                    plan.Step("frp.info", "Read the setup / FRP state.", "قراءة حالة الإعداد وFRP.");
                plan.Step("frp.google", "Open Google's official account recovery for the owner.", "فتح صفحة استعادة حساب Google الرسمية للمالك.");
                plan.Note("Sign in with the Google account that was on the phone before the reset.",
                          "سجّل الدخول بحساب Google الذي كان على الهاتف قبل الفورمات.");
                plan.Note("Account forgotten: the owner goes to an authorised service centre with proof of purchase.",
                          "إذا نُسي الحساب: يذهب المالك لمركز صيانة معتمد ومعه إثبات الشراء.");
                plan.Note("Bypassing FRP is not offered by this system.", "تخطي FRP غير متوفر في هذا النظام.");
                break;

            case "managed_device":
                if (mode == "adb")
                    plan.Step("mdm.info", "Show which organisation manages the phone.", "عرض الجهة التي تدير الهاتف.");
                plan.Summary("Only the organisation's administrator can release the phone.",
                             "فقط مسؤول الجهة يستطيع تحرير الهاتف.");
                plan.Note("Contact the organisation shown above and ask for de-enrolment from their MDM console.",
                          "تواصل مع الجهة المذكورة واطلب إلغاء التسجيل من لوحة الإدارة الخاصة بهم.");
                break;

            case "edl":
                Edl(plan, mode, qualcomm);
                break;
        }

        return plan.Build(sessionId, problem, mode);
    }

    // ------------------------------------------------------------------ rule groups

    private static void Identify(Plan plan, string mode)
    {
        switch (mode)
        {
            case "adb":
                plan.Step("adb.info", "Full Android information over ADB.", "معلومات أندرويد الكاملة عبر ADB.");
                break;
            case "fastboot":
                plan.Step("fb.info", "Bootloader variables (product, lock state, slot).", "متغيرات البوت لودر (المنتج، حالة القفل، الشريحة).");
                break;
            default:
                plan.Step("device.info", "What Windows and the device report in this mode.", "ما يعرضه ويندوز والجهاز في هذا الوضع.");
                if (mode is "download" or "edl" or "mtk" or "apple")
                    plan.Step("usb.scan", "Check the USB port and driver for this mode.", "فحص منفذ USB والتعريف لهذا الوضع.");
                break;
        }
    }

    private static void ConnectionPlan(Plan plan, string mode)
    {
        switch (mode)
        {
            case "unauthorized":
                plan.Note("Unlock the phone and tap \"Allow\" on the USB debugging prompt (tick \"Always allow\").",
                          "افتح قفل الهاتف واضغط \"سماح\" على رسالة تصحيح USB (مع تحديد \"السماح دائماً\").");
                plan.Note("No prompt? Developer options → Revoke USB debugging authorizations, then reconnect the cable.",
                          "لا تظهر الرسالة؟ خيارات المطور ← إلغاء تفويضات تصحيح USB ثم أعد توصيل الكابل.");
                plan.Step("device.info", "Re-check the connection state.", "إعادة فحص حالة الاتصال.");
                break;
            case "offline":
                plan.Note("Reconnect the cable on a rear USB port; if it stays offline restart the phone.",
                          "أعد توصيل الكابل في منفذ خلفي؛ وإذا بقي Offline أعد تشغيل الهاتف.");
                plan.Step("device.info", "Re-check the connection state.", "إعادة فحص حالة الاتصال.");
                break;
            default:
                plan.Step("usb.scan", "Look for the phone on USB and check its driver.", "البحث عن الهاتف في منافذ USB وفحص تعريفه.");
                plan.Note("Use a data cable (not charge-only) and a rear USB port.", "استخدم كابل بيانات (وليس شحن فقط) ومنفذاً خلفياً.");
                plan.Note("Enable Developer options (tap Build number 7 times) → USB debugging.",
                          "فعّل خيارات المطور (اضغط رقم الإصدار 7 مرات) ← تصحيح USB.");
                plan.Note("If the scan shows a driver problem, install the manufacturer's USB driver.",
                          "إذا ظهر خطأ في التعريف ثبّت تعريف USB الخاص بالشركة المصنّعة.");
                break;
        }
    }

    private static void Bootloop(Plan plan, string mode, string brand)
    {
        plan.Summary("Collect the state, then try the safe recovery path. Data is not erased by these steps.",
                     "جمع حالة الهاتف ثم تجربة طريق الإصلاح الآمن. هذه الخطوات لا تحذف البيانات.");
        switch (mode)
        {
            case "adb":
                plan.Step("adb.info", "Record build, security patch and storage before changes.", "تسجيل الإصدار والتحديث الأمني والتخزين قبل أي تغيير.");
                plan.Step("adb.reboot.recovery", "Open Recovery to wipe the cache partition.", "فتح Recovery لمسح الكاش.");
                plan.Note("In Recovery choose \"Wipe cache partition\" (keeps data), then \"Reboot system now\".",
                          "في Recovery اختر \"Wipe cache partition\" (لا يحذف البيانات) ثم \"Reboot system now\".");
                if (brand == "samsung")
                    plan.Step("adb.reboot.download", "If it still loops: Download mode to reflash official firmware.", "إذا استمرت المشكلة: وضع Download لإعادة تفليش السوفت الرسمي.");
                break;
            case "recovery":
                plan.Step("device.info", "Confirm the phone and mode.", "تأكيد الهاتف والوضع.");
                plan.Note("Choose \"Wipe cache partition\" (keeps data), then \"Reboot system now\".",
                          "اختر \"Wipe cache partition\" (لا يحذف البيانات) ثم \"Reboot system now\".");
                plan.Step("adb.reboot.system", "Reboot to Android (when Recovery exposes ADB).", "إعادة التشغيل إلى أندرويد (إذا كان Recovery يدعم ADB).");
                break;
            case "fastboot":
                plan.Step("fb.info", "Check slot and lock state.", "فحص الشريحة وحالة القفل.");
                plan.Step("fb.reboot.recovery", "Open Recovery to wipe the cache partition.", "فتح Recovery لمسح الكاش.");
                plan.Note("If Recovery does not help, flash the official firmware for this exact model.",
                          "إذا لم ينفع Recovery قم بتفليش السوفت الرسمي لنفس الموديل بالضبط.");
                break;
            case "download":
                plan.Step("device.info", "Confirm Download mode.", "تأكيد وضع Download.");
                plan.Step("sam.fw.check", "Verify the official firmware files (MD5, model, binary).", "التحقق من ملفات السوفت الرسمي (MD5، الموديل، الباينري).");
                plan.Note("Flash the verified BL/AP/CP/HOME_CSC with your flashing tool. HOME_CSC keeps data; CSC erases it.",
                          "فلّش ملفات BL/AP/CP/HOME_CSC الموثّقة ببرنامج التفليش. HOME_CSC يحتفظ بالبيانات و CSC يمسحها.");
                break;
            default:
                Identify(plan, mode);
                plan.Note("This mode needs the manufacturer's official service tool to restore firmware.",
                          "هذا الوضع يحتاج أداة الخدمة الرسمية للشركة لاستعادة السوفت.");
                break;
        }
        plan.Note("A factory reset from Recovery erases all data: only with the owner's approval.",
                  "إعادة ضبط المصنع من Recovery تحذف كل البيانات: فقط بموافقة المالك.");
    }

    private static void FlashPrepare(Plan plan, string mode, string brand)
    {
        plan.Summary("Identify the exact model, verify the firmware, and put the phone in flashing mode.",
                     "تحديد الموديل بالضبط، التحقق من السوفت، ووضع الهاتف في وضع التفليش.");
        plan.Note("Back up the owner's data and charge the battery above 50 %.",
                  "خذ نسخة احتياطية من بيانات المالك واشحن البطارية فوق 50٪.");

        if (brand == "samsung")
        {
            if (mode == "adb") plan.Step("adb.info", "Read model, CSC and bootloader binary.", "قراءة الموديل وCSC ورقم الباينري.");
            plan.Step("sam.fw.check", "Verify firmware files match the phone.", "التحقق من تطابق ملفات السوفت مع الهاتف.");
            if (mode == "adb") plan.Step("adb.reboot.download", "Reboot into Download mode.", "إعادة التشغيل إلى وضع Download.");
            else if (mode != "download")
                plan.Note("Enter Download mode with the key combination for this model.", "ادخل وضع Download بتركيبة الأزرار الخاصة بالموديل.");
            plan.Note("Never flash a firmware with a lower binary than the phone's bootloader.",
                      "لا تفلّش سوفت برقم باينري أقل من باينري البوت لودر في الهاتف.");
            return;
        }

        switch (mode)
        {
            case "adb":
                plan.Step("adb.info", "Read model, build and platform.", "قراءة الموديل والإصدار والمعالج.");
                plan.Step("adb.reboot.bootloader", "Reboot to the bootloader (Fastboot).", "إعادة التشغيل إلى البوت لودر (Fastboot).");
                break;
            case "fastboot":
                plan.Step("fb.info", "Read product and lock state.", "قراءة المنتج وحالة القفل.");
                break;
            default:
                Identify(plan, mode);
                break;
        }
        plan.Note("Use only official firmware for this exact model and region.",
                  "استخدم فقط السوفت الرسمي لنفس الموديل والمنطقة.");
    }

    private static void Edl(Plan plan, string mode, bool qualcomm)
    {
        if (mode == "edl")
        {
            plan.Summary("The phone is already in Qualcomm EDL / Diag mode.", "الهاتف بالفعل في وضع EDL / Diag.");
            plan.Step("device.info", "Show port and driver details.", "عرض تفاصيل المنفذ والتعريف.");
            plan.Step("usb.scan", "Confirm the 9008 port and driver.", "تأكيد منفذ 9008 والتعريف.");
            plan.Note("Use only the manufacturer's authorised firmware and programmer for this device.",
                      "استخدم فقط السوفت والبرمجة المعتمدة من الشركة لهذا الجهاز.");
            return;
        }

        if (!qualcomm)
        {
            plan.Summary("EDL is only for Qualcomm phones. This phone is not reported as Qualcomm.",
                         "وضع EDL خاص بمعالجات Qualcomm فقط. هذا الهاتف ليس Qualcomm حسب البيانات.");
            Identify(plan, mode);
            return;
        }

        if (mode == "adb")
        {
            plan.Summary("Switch the Qualcomm phone to EDL (9008).", "تحويل هاتف Qualcomm إلى وضع EDL (9008).");
            plan.Step("adb.info", "Confirm model and platform first.", "تأكيد الموديل والمعالج أولاً.");
            plan.Step("adb.reboot.edl", "Reboot to EDL (only works if the bootloader allows it).", "إعادة التشغيل إلى EDL (يعمل فقط إذا سمح البوت لودر).");
            plan.Note("To leave EDL hold the power button for 10–15 seconds.", "للخروج من EDL اضغط زر التشغيل 10–15 ثانية.");
        }
        else NotInMode(plan, mode, "ADB");
    }

    private static void NotInMode(Plan plan, string mode, string wanted)
    {
        plan.Summary($"This solution needs the phone in {wanted} mode; it is in {Display(mode)}.",
                     $"هذا الحل يحتاج الهاتف في وضع {wanted}؛ الهاتف الآن في وضع {Display(mode)}.");
        Identify(plan, mode);
    }

    // ------------------------------------------------------------------ classification

    public static string ModeGroup(string? mode) => (mode ?? "").Trim() switch
    {
        "" => "none",
        "Adb" => "adb",
        "Recovery" or "Sideload" => "recovery",
        "AdbUnauthorized" => "unauthorized",
        "AdbOffline" => "offline",
        "Fastboot" or "FastbootD" => "fastboot",
        "SamsungDownload" => "download",
        "QualcommEdl" or "QualcommDiag" => "edl",
        "MediaTekPreloader" or "MediaTekBrom" => "mtk",
        "AppleNormal" or "AppleRecovery" or "AppleDfu" => "apple",
        _ => "mtp"
    };

    private static string BrandGroup(AssistDevice? d)
    {
        var s = $"{d?.Brand} {d?.Manufacturer}".ToLowerInvariant();
        if (s.Contains("samsung")) return "samsung";
        if (s.Contains("xiaomi") || s.Contains("redmi") || s.Contains("poco")) return "xiaomi";
        return "other";
    }

    private static bool IsQualcomm(AssistDevice? d)
    {
        var c = (d?.Chipset ?? "").ToLowerInvariant();
        return c.Contains("qualcomm") || c.Contains("snapdragon") || c.StartsWith("msm") ||
               c.StartsWith("sdm") || c.StartsWith("sm") || c.StartsWith("qcom");
    }

    private static string Display(string group) => group switch
    {
        "adb" => "ADB", "recovery" => "Recovery", "fastboot" => "Fastboot", "download" => "Download",
        "edl" => "EDL", "mtk" => "MediaTek BROM", "apple" => "Apple", "unauthorized" => "ADB (Unauthorized)",
        "offline" => "ADB (Offline)", "none" => "—", _ => "MTP / Unknown"
    };

    // ------------------------------------------------------------------ builder

    private sealed class Plan
    {
        private readonly List<(string Id, string En, string Ar)> _steps = [];
        private readonly List<AssistNote> _notes = [];
        private string _summaryEn = "";
        private string _summaryAr = "";

        public void Step(string actionId, string reasonEn, string reasonAr)
        {
            if (!AssistActions.IsAllowed(actionId))
                throw new InvalidOperationException($"Engine produced a non-allow-listed action: {actionId}");
            if (_steps.Any(s => s.Id == actionId)) return;
            _steps.Add((actionId, reasonEn, reasonAr));
        }

        public void Note(string en, string ar) => _notes.Add(new AssistNote(en, ar));

        public void Summary(string en, string ar)
        {
            _summaryEn = en;
            _summaryAr = ar;
        }

        public AssistResponse Build(Guid sessionId, string problem, string mode) =>
            new(sessionId, problem, mode, _summaryEn, _summaryAr,
                _steps.Select((s, i) =>
                {
                    var a = AssistActions.All[s.Id];
                    return new AssistStep(i + 1, a.Id, a.TitleEn, a.TitleAr, s.En, s.Ar, a.Dangerous);
                }).ToList(),
                _notes);
    }
}

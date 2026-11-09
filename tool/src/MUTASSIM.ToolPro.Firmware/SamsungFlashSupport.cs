using System.IO.Compression;

namespace MUTASSIM.ToolPro.Firmware;

// ============================================================================
// ZIP import (e.g. the archive downloaded from a firmware site)
// ============================================================================

public sealed record ZipImportResult(
    string Folder,
    IReadOnlyDictionary<SamsungPart, string> Parts,
    IReadOnlyList<string> AlternativeCsc,
    string? Pit,
    IReadOnlyList<string> Ignored);

/// <summary>
/// Extracts only firmware files (*.tar.md5, *.tar, *.pit) from a ZIP, flattening folders (no path traversal),
/// opens one nested ZIP level if needed, and sorts the files into BL / AP / CP / CSC / USERDATA slots.
/// HOME_CSC is preferred for the CSC slot because it keeps user data.
/// </summary>
public static class FirmwareZip
{
    private static readonly string[] FirmwareExtensions = [".tar.md5", ".tar", ".pit"];

    public static bool IsFirmwareFile(string name) =>
        FirmwareExtensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    public static async Task<ZipImportResult> ImportAsync(string zipPath, Action<string>? log, IProgress<double>? progress, CancellationToken ct)
    {
        var folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(zipPath))!,
            Path.GetFileNameWithoutExtension(zipPath) + "_extracted");
        Directory.CreateDirectory(folder);

        var ignored = new List<string>();
        var extracted = await Task.Run(() => ExtractFirmware(zipPath, folder, ignored, log, progress, depth: 0, ct), ct);
        if (extracted.Count == 0)
            throw new InvalidDataException("No Samsung firmware files (.tar.md5 / .tar / .pit) were found in this ZIP.");

        return Classify(folder, extracted, ignored);
    }

    public static ZipImportResult Classify(string folder, IReadOnlyList<string> files, IReadOnlyList<string>? ignored = null)
    {
        var parts = new Dictionary<SamsungPart, string>();
        var altCsc = new List<string>();
        string? pit = null;

        foreach (var f in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            if (f.EndsWith(".pit", StringComparison.OrdinalIgnoreCase)) { pit ??= f; continue; }
            var part = SamsungFirmwareFile.FromPath(f).Part;
            if (part == SamsungPart.Unknown) continue;
            if (part is SamsungPart.CSC or SamsungPart.HOME_CSC) altCsc.Add(f);
            else parts.TryAdd(part, f);
        }

        // CSC slot: HOME_CSC (keeps data) first, CSC (erases data) only if nothing else.
        var csc = altCsc.FirstOrDefault(f => SamsungFirmwareFile.FromPath(f).Part == SamsungPart.HOME_CSC)
                  ?? altCsc.FirstOrDefault();
        if (csc is not null)
        {
            parts[SamsungFirmwareFile.FromPath(csc).Part] = csc;
            altCsc.Remove(csc);
        }

        return new ZipImportResult(folder, parts, altCsc, pit, ignored ?? []);
    }

    private static List<string> ExtractFirmware(string zipPath, string folder, List<string> ignored,
        Action<string>? log, IProgress<double>? progress, int depth, CancellationToken ct)
    {
        var result = new List<string>();
        using var zip = ZipFile.OpenRead(zipPath);

        var entries = zip.Entries.Where(e => e.Length > 0 || e.Name.Length > 0).ToList();
        var firmware = entries.Where(e => IsFirmwareFile(e.Name)).ToList();
        var nested = entries.Where(e => e.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToList();

        var total = firmware.Sum(e => e.Length);
        var free = new DriveInfo(Path.GetPathRoot(folder)!).AvailableFreeSpace;
        if (total > free) throw new IOException($"Not enough disk space to extract the ZIP ({total / (1 << 30)} GB needed).");

        long done = 0;
        foreach (var e in firmware)
        {
            ct.ThrowIfCancellationRequested();
            var target = Path.Combine(folder, e.Name); // file name only: folders inside the ZIP are flattened
            if (File.Exists(target) && new FileInfo(target).Length == e.Length)
            {
                log?.Invoke($"Already extracted: {e.Name}");
            }
            else
            {
                log?.Invoke($"Extract {e.Name}");
                using var input = e.Open();
                using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
                var buffer = new byte[4 << 20];
                int n;
                while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    output.Write(buffer, 0, n);
                    done += n;
                    progress?.Report(total == 0 ? 100 : done * 100.0 / total);
                }
            }
            result.Add(target);
        }

        foreach (var e in entries.Except(firmware).Except(nested))
            if (e.Name.Length > 0) ignored.Add(e.FullName);

        // Some downloads wrap the firmware in a second ZIP.
        if (result.Count == 0 && depth == 0)
        {
            foreach (var n in nested)
            {
                var tmp = Path.Combine(folder, n.Name);
                log?.Invoke($"Open inner ZIP {n.Name}");
                n.ExtractToFile(tmp, overwrite: true);
                try { result.AddRange(ExtractFirmware(tmp, folder, ignored, log, progress, depth + 1, ct)); }
                finally { File.Delete(tmp); }
            }
        }

        return result;
    }
}

// ============================================================================
// Working folder: Heimdall (C/C++ on Windows) can fail on non-English paths
// ============================================================================

public static class FlashWorkDir
{
    public static bool IsAsciiPath(string path) => path.All(c => c < 128);

    /// <summary>A temporary folder whose full path is plain ASCII (Arabic or other user names break Heimdall).</summary>
    public static string Choose(string firmwareFolder)
    {
        string[] candidates =
        [
            Path.Combine(firmwareFolder, "_mutassim_flash_tmp"),
            Path.Combine(Path.GetTempPath(), "mutassim_flash_tmp"),
            Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\", "MUTASSIM_FLASH_TMP")
        ];
        return candidates.FirstOrDefault(c => IsAsciiPath(Path.GetFullPath(c))) ?? candidates[^1];
    }
}

// ============================================================================
// Common errors → plain explanation (English / Arabic)
// ============================================================================

public sealed record FlashHint(string En, string Ar);

public static class SamsungFlashErrors
{
    private static readonly (string[] Match, FlashHint Hint)[] Rules =
    [
        (["Failed to detect compatible download-mode device"],
            new("Phone not found in Download mode. Check the cable, put the phone in Download mode, and install the WinUSB driver with Zadig.",
                "الهاتف غير موجود في وضع Download. افحص الكابل، ادخل وضع Download، وثبّت تعريف WinUSB ببرنامج Zadig.")),
        (["libusb error: -12", "LIBUSB_ERROR_NOT_SUPPORTED"],
            new("Windows is using the Samsung driver, not WinUSB. Open Zadig with the phone in Download mode and replace the driver with WinUSB.",
                "ويندوز يستخدم تعريف سامسنج وليس WinUSB. افتح Zadig والهاتف في وضع Download واستبدل التعريف بـ WinUSB.")),
        (["libusb error: -3", "LIBUSB_ERROR_ACCESS", "Failed to claim interface"],
            new("Another program is using the phone (Odin, Smart Switch, Kies, another tool). Close it, unplug and plug the cable again.",
                "برنامج آخر يستخدم الهاتف (Odin أو Smart Switch أو Kies أو أداة أخرى). أغلقه وافصل الكابل ثم أعد توصيله.")),
        (["libusb error: -4", "LIBUSB_ERROR_NO_DEVICE", "libusb error: -7", "LIBUSB_ERROR_TIMEOUT"],
            new("The USB connection dropped. Use the original cable on a rear USB 2.0 port (no hub), then re-enter Download mode.",
                "انقطع اتصال USB. استخدم الكابل الأصلي في منفذ USB 2.0 خلفي (بدون موزّع) ثم ادخل وضع Download من جديد.")),
        (["Protocol initialisation failed", "Failed to begin session", "Failed to receive handshake"],
            new("The phone did not accept the session. Restart Download mode (Vol Down + Power ~7 s, then the Download key combination) and try again.",
                "الهاتف لم يقبل الجلسة. أعد الدخول لوضع Download (صوت تحت + تشغيل 7 ثواني ثم تركيبة Download) وحاول مرة أخرى.")),
        (["Failed to download PIT", "Failed to receive PIT"],
            new("The partition table could not be read. Re-enter Download mode and retry; nothing was written.",
                "تعذّرت قراءة جدول البارتشن. أعد الدخول لوضع Download وحاول؛ لم يُكتب شيء.")),
        (["Failed to send file part", "Failed to upload", "Failed to confirm end of file transfer", "Failed to send data"],
            new("The transfer stopped while writing. Change cable / USB port and flash again right away; the phone stays in Download mode.",
                "توقف النقل أثناء الكتابة. غيّر الكابل أو المنفذ وفلّش مرة أخرى فوراً؛ الهاتف يبقى في وضع Download.")),
        (["Failed to receive session end confirmation", "Failed to send end session packet"],
            new("Heimdall could not close the session. This often happens after a successful write: look at the phone screen; if it shows PASS or reboots, it is done.",
                "لم يستطع Heimdall إغلاق الجلسة. يحدث هذا غالباً بعد كتابة ناجحة: انظر لشاشة الهاتف؛ إذا ظهر PASS أو أعاد التشغيل فقد تم.")),
        (["does not exist in the specified PIT", "Partition name", "not found in PIT"],
            new("The firmware does not match this phone's partitions. Use firmware for this exact model.",
                "السوفت لا يطابق بارتشن هذا الهاتف. استخدم سوفت لنفس الموديل بالضبط.")),
        (["Failed to open file", "Failed to read file", "No such file"],
            new("A file could not be opened. Keep firmware in a short English-only folder (e.g. C:\\FW) and close any program using it.",
                "تعذّر فتح ملف. ضع السوفت في مجلد قصير بأحرف إنجليزية فقط (مثل C:\\FW) وأغلق أي برنامج يستخدمه.")),
    ];

    /// <summary>Messages the phone shows on its own Download-mode screen.</summary>
    public static readonly IReadOnlyList<(string Screen, FlashHint Hint)> PhoneScreen =
    [
        ("SW REV CHECK FAIL", new("Downgrade blocked: the firmware binary is lower than the phone's. Use the same or newer binary.",
                                  "منع النزول: باينري السوفت أقل من باينري الهاتف. استخدم نفس الباينري أو أحدث.")),
        ("Secure Check Fail / Custom Binary Blocked", new("The phone accepts only original, signed Samsung files. Download the official firmware again.",
                                  "الهاتف يقبل فقط ملفات سامسنج الأصلية الموقّعة. أعد تنزيل السوفت الرسمي.")),
        ("FRP Lock / Blocked by FRP", new("The phone is locked to the owner's Google account. Only official firmware can be flashed, and the owner must sign in after.",
                                  "الهاتف مقفول بحساب Google للمالك. يمكن تفليش السوفت الرسمي فقط، ويجب أن يسجّل المالك الدخول بعدها.")),
        ("KG STATE / RMM: Locked / Prenormal", new("Knox Guard / leasing lock from the seller or carrier. Contact the owner or the carrier; it cannot be removed by flashing.",
                                  "قفل Knox Guard أو تقسيط من البائع أو شركة الاتصالات. تواصل مع المالك أو الشركة؛ لا يُزال بالتفليش.")),
        ("Only official released binaries are allowed", new("Same as above: use unmodified official firmware.",
                                  "كما سبق: استخدم السوفت الرسمي بدون تعديل.")),
    ];

    public static IReadOnlyList<FlashHint> Explain(string? heimdallOutput)
    {
        if (string.IsNullOrWhiteSpace(heimdallOutput)) return [];
        return Rules.Where(r => r.Match.Any(m => heimdallOutput.Contains(m, StringComparison.OrdinalIgnoreCase)))
                    .Select(r => r.Hint).ToList();
    }
}

using MUTASSIM.ToolPro.Adb;
using MUTASSIM.ToolPro.App.Converters;
using MUTASSIM.ToolPro.Firmware;
using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Core.Platform;
using MUTASSIM.ToolPro.Fastboot;
using MUTASSIM.ToolPro.Usb;

namespace MUTASSIM.ToolPro.App.Workspace;

/// <summary>Brand-specific Android properties that are printed only when the device reports them.</summary>
public sealed record BrandKeys(string Label, string[] Keys);

/// <summary>Implementations behind the buttons. Each prints only what the device/tool really returned.</summary>
public static class ActionLibrary
{
    // ---------------------------------------------------------------- ADB: information

    private static readonly (string Label, string Key)[] CommonProps =
    [
        ("Manufacturer", "ro.product.manufacturer"),
        ("Brand", "ro.product.brand"),
        ("Model", "ro.product.model"),
        ("Codename", "ro.product.device"),
        ("Product", "ro.product.name"),
        ("Android", "ro.build.version.release"),
        ("SDK", "ro.build.version.sdk"),
        ("Security patch", "ro.build.version.security_patch"),
        ("Build", "ro.build.display.id"),
        ("Fingerprint", "ro.build.fingerprint"),
        ("Platform", "ro.board.platform"),
        ("SoC", "ro.soc.model"),
        ("CPU ABI", "ro.product.cpu.abi"),
        ("Baseband", "gsm.version.baseband"),
        ("Bootloader", "ro.bootloader"),
        ("Verified boot", "ro.boot.verifiedbootstate"),
        ("Flash locked", "ro.boot.flash.locked"),
        ("Slot", "ro.boot.slot_suffix"),
        ("Treble", "ro.treble.enabled"),
        ("Encryption", "ro.crypto.state")
    ];

    public static ActionDefinition ReadInfoAdb(string id, params BrandKeys[] brandKeys) =>
        new(id, "Read Info (ADB)", "قراءة المعلومات (ADB)", DeviceRequirement.Adb, async ctx =>
        {
            ctx.Log.Info("Reading device properties...");
            ctx.AdbDetector.Invalidate(ctx.Serial);
            var props = await ctx.Adb.GetPropsAsync(ctx.Serial, ctx.Ct);
            if (props.Count == 0) return ActionResult.Fail("The device returned no properties.");

            string? P(string k) => props.TryGetValue(k, out var v) ? v : null;
            ctx.Log.Value("Serial", ctx.Serial);
            foreach (var (label, key) in CommonProps) ctx.Log.Value(label, P(key));

            var family = ChipsetDetector.FromProperties(props);
            if (family != ChipsetFamily.Unknown) ctx.Log.Value("Chipset family", family.ToString());

            foreach (var group in brandKeys)
            {
                var value = group.Keys.Select(P).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
                ctx.Log.Value(group.Label, value);
            }

            ctx.Log.Value("Kernel", await ctx.Adb.ShellAsync(ctx.Serial, ["uname", "-r"], ctx.Ct));
            ctx.Log.Value("RAM", MemTotal(await ctx.Adb.ShellAsync(ctx.Serial, ["cat", "/proc/meminfo"], ctx.Ct)));
            ctx.Log.Value("Storage /data", DfSize(await ctx.Adb.ShellAsync(ctx.Serial, ["df", "-h", "/data"], ctx.Ct)));
            ctx.Log.Value("Battery", BatteryLevel(await ctx.Adb.ShellAsync(ctx.Serial, ["dumpsys", "battery"], ctx.Ct)));
            return ActionResult.Ok($"{props.Count} properties read");
        });

    public static readonly BrandKeys[] SamsungKeys =
    [
        new("AP / PDA", ["ro.build.PDA"]),
        new("CSC", ["ro.csc.sales_code", "ril.sales_code"]),
        new("CSC country", ["ro.csc.country_code"]),
        new("Knox warranty bit", ["ro.boot.warranty_bit"]),
        new("One UI", ["ro.build.version.oneui"])
    ];

    public static readonly BrandKeys[] XiaomiKeys =
    [
        new("MIUI", ["ro.miui.ui.version.name"]),
        new("HyperOS", ["ro.mi.os.version.name"]),
        new("ROM version", ["ro.build.version.incremental"]),
        new("Mod device", ["ro.product.mod_device"]),
        new("Region", ["ro.miui.region", "ro.boot.hwc"])
    ];

    public static readonly BrandKeys[] HuaweiKeys =
    [
        new("EMUI", ["ro.build.version.emui"]),
        new("MagicOS", ["ro.build.version.magic"]),
        new("HW display id", ["ro.huawei.build.display.id"])
    ];

    public static readonly BrandKeys[] OppoKeys =
    [
        new("ColorOS", ["ro.build.version.oplusrom", "ro.build.version.opporom"]),
        new("realme UI", ["ro.build.version.realmeui"])
    ];

    public static readonly BrandKeys[] VivoKeys =
    [
        new("Funtouch/OriginOS", ["ro.vivo.os.build.display.id", "ro.vivo.os.version"]),
        new("Vivo model", ["ro.vivo.product.model"])
    ];

    // ---------------------------------------------------------------- Any mode: what Windows / the device reported

    public static readonly ActionDefinition ReadInfoDevice =
        new("device.info", "Read Info (USB / Device)", "قراءة معلومات الجهاز (USB)", DeviceRequirement.AnyDevice, ctx =>
        {
            var d = ctx.Device!;
            ctx.Log.Value("Mode", d.Mode.ToDisplayText());
            ctx.Log.Value("Manufacturer", d.Manufacturer);
            ctx.Log.Value("Brand", d.Brand);
            ctx.Log.Value("Model", d.Model);
            ctx.Log.Value("Product", d.Product);
            ctx.Log.Value("Serial", d.SerialNumber);
            ctx.Log.Value("Android", d.AndroidVersion);
            ctx.Log.Value("Chipset", d.Chipset);
            ctx.Log.Value("Bootloader", d.BootloaderState);
            ctx.Log.Value("USB VID:PID", d.UsbIdText);
            ctx.Log.Value("USB driver", d.DriverState == DriverState.Unknown ? null : d.DriverState.ToString());
            ctx.Log.Value("COM port", d.ComPort);
            ctx.Log.Value("Instance ID", d.UsbInstanceId);
            ctx.Log.Value("Detected by", d.Source);

            if (d.Mode is ConnectionMode.Mtp or ConnectionMode.Unknown)
                ctx.Log.Warn(Services.LocalizationService.Get("Run.EnableAdbHint"));
            else if (d.Mode == ConnectionMode.AdbUnauthorized)
                ctx.Log.Warn(Services.LocalizationService.Get("Run.Unauthorized"));
            return Task.FromResult(ActionResult.Ok("Device information shown"));
        });

    // ---------------------------------------------------------------- Model database

    /// <summary>Downloads Google's public supported-devices list and rebuilds the model catalog.</summary>
    public static readonly ActionDefinition UpdateCatalog =
        new("catalog.update", "Update model database", "تحديث قاعدة الموديلات", DeviceRequirement.None, async ctx =>
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"MutassimToolPro/{MUTASSIM.ToolPro.Core.AppInfo.Version.ToString(3)}");
            var paths = ctx.Get<MUTASSIM.ToolPro.Infrastructure.AppPaths>();
            var catalog = ctx.Get<MUTASSIM.ToolPro.Infrastructure.Catalog.DeviceCatalog>();
            var before = catalog.Models.Count;

            var result = await MUTASSIM.ToolPro.Infrastructure.Catalog.PlayDeviceListImporter
                .DownloadAndImportAsync(http, paths, line => ctx.Log.Muted(line), ctx.Ct);

            ctx.Log.Value("Source", result.Source);
            ctx.Log.Value("Rows read", result.RowsRead.ToString("N0"));
            foreach (var (brand, count) in result.PerBrand.OrderByDescending(kv => kv.Value))
                ctx.Log.Value(WorkspaceCatalog.Find(brand)?.Tab ?? brand, count.ToString("N0"));

            catalog.Reload();
            ctx.Log.Value("Saved to", result.OutputFile);
            return ActionResult.Ok($"Model database: {before:N0} → {catalog.Models.Count:N0} models");
        });

    // ---------------------------------------------------------------- Samsung firmware (tar.md5)

    public static readonly ActionDefinition SamsungFirmwareCheck =
        new("sam.fw.check", "Check Firmware (BL/AP/CP/CSC)", "فحص الفلاشة (BL/AP/CP/CSC)", DeviceRequirement.None, async ctx =>
        {
            var files = ctx.Dialogs.OpenFiles("Samsung firmware (*.tar.md5;*.tar)|*.tar.md5;*.tar|All files|*.*", null, multiselect: true);
            if (files.Length == 0) return ActionResult.Fail("No file selected");

            string? model = null, bootloader = null;
            if (ctx.Device is { Mode: ConnectionMode.Adb, SerialNumber: { } serial })
            {
                model = await ctx.Adb.ShellAsync(serial, ["getprop", "ro.product.model"], ctx.Ct);
                bootloader = await ctx.Adb.ShellAsync(serial, ["getprop", "ro.bootloader"], ctx.Ct);
                ctx.Log.Value("Device model", model);
                ctx.Log.Value("Device BL", bootloader);
                ctx.Log.Value("Device binary", SamsungBuild.Parse(bootloader)?.Binary?.ToString());
            }
            else
            {
                ctx.Log.Warn("No device in ADB mode selected: integrity is checked, model/binary comparison is skipped.");
            }

            var parts = files.Select(SamsungFirmwareFile.FromPath).ToList();
            var problems = 0;
            foreach (var f in parts)
            {
                ctx.Log.Header(Path.GetFileName(f.Path));
                ctx.Log.Value("Part", f.Part == SamsungPart.Unknown ? "not recognised from name" : f.Part.ToString());
                ctx.Log.Value("Build", f.Build?.Build);
                ctx.Log.Value("Binary", f.Build?.Binary?.ToString());
                ctx.Log.Value("Size", FileSizeConverter.Format(new FileInfo(f.Path).Length));

                var lastLogged = -100.0;
                var progress = new Progress<double>(p =>
                {
                    if (p - lastLogged < 25 && p < 100) return;
                    lastLogged = p;
                    ctx.Log.Muted($"MD5 {p:0}%");
                });
                var r = await TarMd5.AnalyzeAsync(f.Path, progress, ctx.Ct);

                if (r.Entries.Count == 0)
                {
                    ctx.Log.Error("Not a valid tar archive.");
                    problems++;
                }
                foreach (var e in r.Entries) ctx.Log.Muted($"  {e.Name}  ({FileSizeConverter.Format(e.Size)})");

                if (!r.HasEmbeddedMd5) ctx.Log.Warn("Plain .tar without embedded MD5: integrity cannot be verified.");
                else if (r.Md5Matches) ctx.Log.Ok($"MD5 OK  {r.ActualMd5}");
                else
                {
                    ctx.Log.Error($"MD5 MISMATCH  expected {r.ExpectedMd5}  got {r.ActualMd5} — file is damaged, do not use it.");
                    problems++;
                }

                if (model is not null)
                {
                    var c = SamsungCompatibility.Check(f.Build, model, bootloader);
                    switch (c.Verdict)
                    {
                        case CompatibilityVerdict.Compatible: ctx.Log.Ok(c.Message); break;
                        case CompatibilityVerdict.Unknown: ctx.Log.Warn(c.Message); break;
                        default:
                            ctx.Log.Error(c.Message);
                            problems++;
                            break;
                    }
                }
            }

            var present = parts.Select(p => p.Part == SamsungPart.HOME_CSC ? SamsungPart.CSC : p.Part).ToHashSet();
            var missing = new[] { SamsungPart.BL, SamsungPart.AP, SamsungPart.CP, SamsungPart.CSC }.Where(p => !present.Contains(p)).ToList();
            if (missing.Count > 0) ctx.Log.Warn($"Not selected: {string.Join(", ", missing)} (a full flash normally uses BL + AP + CP + CSC/HOME_CSC; some devices have no CP).");

            return problems == 0
                ? ActionResult.Ok($"{parts.Count} file(s) checked, no problem found")
                : ActionResult.Fail($"{problems} problem(s) found");
        });

    // ---------------------------------------------------------------- ADB: reboot

    public static ActionDefinition AdbReboot(AdbRebootTarget target)
    {
        var (en, ar) = target switch
        {
            AdbRebootTarget.System => ("Reboot System", "إعادة تشغيل النظام"),
            AdbRebootTarget.Recovery => ("Reboot Recovery", "إعادة التشغيل إلى Recovery"),
            AdbRebootTarget.Bootloader => ("Reboot Bootloader", "إعادة التشغيل إلى Bootloader"),
            AdbRebootTarget.FastbootD => ("Reboot FastbootD", "إعادة التشغيل إلى FastbootD"),
            AdbRebootTarget.Download => ("Reboot Download Mode", "إعادة التشغيل إلى Download"),
            AdbRebootTarget.Sideload => ("Reboot Sideload", "إعادة التشغيل إلى Sideload"),
            AdbRebootTarget.Edl => ("Reboot EDL (9008)", "إعادة التشغيل إلى EDL"),
            _ => (target.ToString(), target.ToString())
        };

        var def = new ActionDefinition($"adb.reboot.{target}".ToLowerInvariant(), en, ar, DeviceRequirement.Adb, async ctx =>
        {
            var r = await ctx.Adb.RebootAsync(ctx.Serial, target, ctx.Ct);
            if (!string.IsNullOrWhiteSpace(r.Combined)) ctx.Log.Muted(r.Combined);
            return r.Success
                ? ActionResult.Ok("Reboot command accepted by the device")
                : ActionResult.Fail(AdbClient.FirstLine(r.Combined));
        });

        return target == AdbRebootTarget.Edl
            ? def with
            {
                IsDangerous = true,
                ConfirmEn = "Reboot to Qualcomm EDL (9008)?\n\nOnly works on devices whose bootloader accepts it. To leave EDL, hold the power button (10–15 s). Continue?",
                ConfirmAr = "إعادة التشغيل إلى وضع EDL (9008)؟\n\nيعمل فقط على الأجهزة التي يقبل محمّل الإقلاع فيها هذا الأمر. للخروج من EDL اضغط زر التشغيل مطولاً (10–15 ثانية). متابعة؟"
            }
            : def;
    }

    // ---------------------------------------------------------------- ADB: apps

    public static ActionDefinition ListApps(bool userOnly) =>
        new(userOnly ? "adb.apps.user" : "adb.apps.all",
            userOnly ? "List User Apps" : "List All Apps",
            userOnly ? "عرض تطبيقات المستخدم" : "عرض كل التطبيقات",
            DeviceRequirement.Adb, async ctx =>
            {
                var apps = await ctx.Adb.ListPackagesAsync(ctx.Serial, userOnly, ctx.Ct);
                foreach (var a in apps) ctx.Log.Info(a);
                return ActionResult.Ok($"{apps.Count} packages");
            });

    public static readonly ActionDefinition InstallApk =
        new("adb.install", "Install APK", "تثبيت APK", DeviceRequirement.Adb, async ctx =>
        {
            var files = ctx.Dialogs.OpenFiles("Android package (*.apk)|*.apk", null, multiselect: false);
            if (files.Length == 0) return ActionResult.Fail("No file selected");
            ctx.Log.Value("File", Path.GetFileName(files[0]));
            var r = await ctx.Adb.InstallAsync(ctx.Serial, files[0], ctx.Ct, line => ctx.Log.Muted(line));
            return r.Success && r.Combined.Contains("Success", StringComparison.OrdinalIgnoreCase)
                ? ActionResult.Ok("APK installed")
                : ActionResult.Fail(AdbClient.FirstLine(r.Combined));
        });

    public static readonly ActionDefinition UninstallUserApp =
        new("adb.uninstall", "Uninstall User App", "إزالة تطبيق مستخدم", DeviceRequirement.Adb, async ctx =>
        {
            var package = ctx.Dialogs.Prompt(
                "Package name of the user app to uninstall (use “List User Apps” first):\nاسم حزمة التطبيق المراد إزالته:",
                "");
            if (string.IsNullOrWhiteSpace(package)) return ActionResult.Fail("No package entered");
            package = package.Trim();
            if (!ctx.Dialogs.Confirm($"Uninstall {package}? Its data will be deleted.\nإزالة {package}؟ ستُحذف بياناته.", dangerous: true))
                return ActionResult.Fail("Cancelled by user");

            var r = await ctx.Adb.UninstallUserAppAsync(ctx.Serial, package, ctx.Ct);
            return r.Success && r.Combined.Contains("Success", StringComparison.OrdinalIgnoreCase)
                ? ActionResult.Ok($"{package} uninstalled")
                : ActionResult.Fail(AdbClient.FirstLine(r.Combined));
        });

    // ---------------------------------------------------------------- Fastboot

    private static readonly string[] KeyVars =
    [
        "product", "serialno", "unlocked", "secure", "is-userspace", "current-slot", "slot-count",
        "version-bootloader", "version-baseband", "anti", "variant", "hw-revision", "max-download-size"
    ];

    public static ActionDefinition ReadInfoFastboot(string id) =>
        new(id, "Read Info (Fastboot)", "قراءة المعلومات (Fastboot)", DeviceRequirement.Fastboot, async ctx =>
        {
            ctx.Log.Info("Reading bootloader variables...");
            ctx.FastbootDetector.Invalidate(ctx.Serial);
            var vars = await ctx.Fastboot.GetVarsAsync(ctx.Serial, ctx.Ct);
            if (vars.Count == 0) return ActionResult.Fail("The bootloader returned no variables.");

            foreach (var key in KeyVars)
                ctx.Log.Value(key, vars.TryGetValue(key, out var v) ? v : null);

            var others = vars.Where(kv => !KeyVars.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)).OrderBy(kv => kv.Key).ToList();
            if (others.Count > 0)
            {
                ctx.Log.Muted($"— {others.Count} more variables —");
                foreach (var kv in others) ctx.Log.Muted($"{kv.Key}: {kv.Value}");
            }
            return ActionResult.Ok($"{vars.Count} variables read");
        });

    public static ActionDefinition FastbootReboot(FastbootRebootTarget target)
    {
        var (en, ar) = target switch
        {
            FastbootRebootTarget.System => ("Reboot System", "إعادة تشغيل النظام"),
            FastbootRebootTarget.Bootloader => ("Reboot Bootloader", "إعادة التشغيل إلى Bootloader"),
            FastbootRebootTarget.Recovery => ("Reboot Recovery", "إعادة التشغيل إلى Recovery"),
            FastbootRebootTarget.FastbootD => ("Reboot FastbootD", "إعادة التشغيل إلى FastbootD"),
            _ => (target.ToString(), target.ToString())
        };
        return new($"fb.reboot.{target}".ToLowerInvariant(), en, ar, DeviceRequirement.Fastboot, async ctx =>
        {
            var r = await ctx.Fastboot.RebootAsync(ctx.Serial, target, ctx.Ct);
            if (!string.IsNullOrWhiteSpace(r.Combined)) ctx.Log.Muted(r.Combined);
            return r.Success && !r.Combined.Contains("FAILED", StringComparison.OrdinalIgnoreCase)
                ? ActionResult.Ok("Reboot command accepted")
                : ActionResult.Fail(AdbClient.FirstLine(r.Combined));
        });
    }

    // ---------------------------------------------------------------- USB (Windows PnP)

    public static ActionDefinition ScanUsb(string id, string en, string ar, params ushort[] vendorIds) =>
        new(id, en, ar, DeviceRequirement.None, ctx =>
        {
            var entries = ctx.Usb.ListVendorEntries()
                .Where(e => UsbIdClassifier.TryParseInstanceId(e.InstanceId, out var vid, out _) &&
                            (vendorIds.Length == 0 || vendorIds.Contains(vid)))
                .ToList();

            foreach (var e in entries)
            {
                UsbIdClassifier.TryParseInstanceId(e.InstanceId, out var vid, out var pid);
                var c = UsbIdClassifier.Classify(vid, pid);
                var driver = e.ErrorCode switch { 0 => "driver OK", 28 => "DRIVER MISSING", _ => $"driver problem (code {e.ErrorCode})" };
                var kind = e.ErrorCode == 0 ? LogKind.Value : LogKind.Warn;
                ctx.Log.Write($"{vid:X4}:{pid:X4}  {e.Name ?? "(no name)"}  [{driver}]" +
                              (c is { Mode: not ConnectionMode.Unknown } ? $"  → {c.Mode.ToDisplayText()}" : "") +
                              (UsbDeviceDetector.ComPort(e.Name) is { } com ? $"  {com}" : ""), kind);
            }

            return Task.FromResult(entries.Count == 0
                ? ActionResult.Fail("No matching USB device found. Check the cable/port and the device mode.")
                : ActionResult.Ok($"{entries.Count} USB entries"));
        });

    // ---------------------------------------------------------------- FRP / MDM (read-only, owner assistance)

    public static readonly ActionDefinition FrpInfo =
        new("frp.info", "Read FRP / Setup State (ADB)", "قراءة حالة FRP والإعداد (ADB)", DeviceRequirement.Adb, async ctx =>
        {
            var pst = await ctx.Adb.ShellAsync(ctx.Serial, ["getprop", "ro.frp.pst"], ctx.Ct);
            ctx.Log.Value("FRP partition", string.IsNullOrWhiteSpace(pst) ? "not declared" : $"present ({pst})");
            ctx.Log.Value("Device provisioned", await ctx.Adb.ShellAsync(ctx.Serial, ["settings", "get", "global", "device_provisioned"], ctx.Ct));
            ctx.Log.Value("User setup complete", await ctx.Adb.ShellAsync(ctx.Serial, ["settings", "get", "secure", "user_setup_complete"], ctx.Ct));
            ctx.Log.Info("If the owner cannot sign in, use Google Account Recovery or the manufacturer's service with proof of purchase.");
            return ActionResult.Ok("State read");
        });

    public static readonly ActionDefinition OpenGoogleRecovery =
        new("frp.google", "Google Account Recovery (official)", "استعادة حساب Google (رسمي)", DeviceRequirement.None, ctx =>
        {
            ctx.Dialogs.OpenUrl("https://accounts.google.com/signin/recovery");
            return Task.FromResult(ActionResult.Ok("Opened accounts.google.com/signin/recovery"));
        });

    public static readonly ActionDefinition MdmInfo =
        new("mdm.info", "Check Device Management (ADB)", "فحص الإدارة المؤسسية (ADB)", DeviceRequirement.Adb, async ctx =>
        {
            var r = await ctx.Adb.RunForDeviceAsync(ctx.Serial, ["shell", "dumpsys", "device_policy"], TimeSpan.FromSeconds(20), ctx.Ct);
            if (!r.Success) return ActionResult.Fail(AdbClient.FirstLine(r.Combined));
            var admins = AdbParsers.ParseDeviceAdmins(r.StdOut);
            if (admins.Count == 0)
            {
                ctx.Log.Value("Management", "No device owner / profile owner reported");
                return ActionResult.Ok("Device is not enterprise-managed");
            }
            foreach (var a in admins) ctx.Log.Value("Managed by", a);
            ctx.Log.Info("De-enrolment must be done by the organisation's administrator from its MDM console.");
            return ActionResult.Ok($"{admins.Count} management entries");
        });

    // ---------------------------------------------------------------- Terminals

    public static ActionDefinition AdbTerminal(IReadOnlyList<string> args) =>
        new("adb.terminal", "adb " + string.Join(' ', args), "adb " + string.Join(' ', args), DeviceRequirement.Adb, async ctx =>
        {
            var r = await ctx.Adb.RunForDeviceAsync(ctx.Serial, args, TimeSpan.FromSeconds(30), ctx.Ct, line => ctx.Log.Muted(line));
            ctx.Log.Value("Exit code", r.TimedOut ? "timeout" : r.ExitCode.ToString());
            return r.Success ? ActionResult.Ok("Command finished") : ActionResult.Fail(AdbClient.FirstLine(r.StdErr.Length > 0 ? r.StdErr : r.StdOut));
        });

    public static ActionDefinition FastbootTerminal(IReadOnlyList<string> args) =>
        new("fb.terminal", "fastboot " + string.Join(' ', args), "fastboot " + string.Join(' ', args), DeviceRequirement.Fastboot, async ctx =>
        {
            var r = await ctx.Fastboot.RunForDeviceAsync(ctx.Serial, args, TimeSpan.FromSeconds(30), ctx.Ct, line => ctx.Log.Muted(line));
            ctx.Log.Value("Exit code", r.TimedOut ? "timeout" : r.ExitCode.ToString());
            return r.Success ? ActionResult.Ok("Command finished") : ActionResult.Fail(AdbClient.FirstLine(r.Combined));
        });

    // ---------------------------------------------------------------- helpers (pure, unit-testable)

    public static string? MemTotal(string? meminfo)
    {
        var line = meminfo?.Split('\n').FirstOrDefault(l => l.StartsWith("MemTotal", StringComparison.Ordinal));
        if (line is null) return null;
        var digits = new string(line.Where(char.IsDigit).ToArray());
        return long.TryParse(digits, out var kb) ? $"{kb / 1024.0 / 1024.0:0.0} GB ({kb:N0} kB)" : null;
    }

    public static string? DfSize(string? df)
    {
        var lines = df?.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines is null || lines.Length < 2) return null;
        var cols = lines[^1].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return cols.Length >= 4 ? $"size {cols[1]}, used {cols[2]}, free {cols[3]}" : null;
    }

    public static string? BatteryLevel(string? dumpsys)
    {
        var line = dumpsys?.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("level:", StringComparison.Ordinal));
        return line is null ? null : line["level:".Length..].Trim() + " %";
    }
}

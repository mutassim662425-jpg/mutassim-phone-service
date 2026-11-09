using MUTASSIM.ToolPro.Adb;
using MUTASSIM.ToolPro.App.Services;
using MUTASSIM.ToolPro.App.Converters;
using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Core.Platform;
using MUTASSIM.ToolPro.Firmware;

namespace MUTASSIM.ToolPro.App.Workspace;

/// <summary>
/// Samsung firmware flashing with Heimdall (EXPERIMENTAL).
/// Order of checks: MD5 of every package → model / binary match → plan from the package PIT
/// (protected partitions refused, no repartition) → battery and disk space → user confirmation
/// → phone PIT must match the plan → extract (LZ4) → one Heimdall flash session.
/// "Dry run" stops after the plan and writes nothing to the phone.
/// </summary>
public static class SamsungFlashActions
{
    private const int MinBattery = 50;

    /// <summary>
    /// Builds the action for the Samsung Flash page. <paramref name="paths"/> are the files from the slots
    /// (AP, BL, CP, CSC/HOME_CSC, USERDATA); <paramref name="pitPath"/> is an optional PIT chosen by the user.
    /// </summary>
    public static ActionDefinition Create(IReadOnlyList<string> paths, string? pitPath, bool dryRun) => dryRun
        ? new("sam.flash.plan", "Flash Plan (Dry Run, no writing)", "خطة التفليش (تجربة بدون كتابة)", DeviceRequirement.None,
            ctx => RunAsync(ctx, paths, pitPath, dryRun: true))
        : new("sam.flash.heimdall", "Flash Firmware (Heimdall, experimental)", "تفليش السوفت (Heimdall، تجريبي)", DeviceRequirement.None,
            ctx => RunAsync(ctx, paths, pitPath, dryRun: false))
        {
            IsDangerous = true,
            BillingCode = "samsung.flash"  // charged on start, refunded automatically if the flash fails
        };

    private static async Task<ActionResult> RunAsync(ActionContext ctx, IReadOnlyList<string> paths, string? pitPath, bool dryRun)
    {
        var log = ctx.Log;

        // 1) Files
        if (paths.Count == 0) return ActionResult.Fail("No file selected");
        foreach (var p in paths)
            if (!File.Exists(p)) return ActionResult.Fail($"File not found: {p}");

        var packages = new List<FirmwarePackage>();
        foreach (var path in paths)
        {
            var file = SamsungFirmwareFile.FromPath(path);
            log.Header($"{file.Part}  {Path.GetFileName(path)}");
            var lastPct = -100.0;
            var progress = new Progress<double>(p =>
            {
                if (p - lastPct < 25 && p < 100) return;
                lastPct = p;
                log.Muted($"MD5 {p:0}%");
            });
            var r = await TarMd5.AnalyzeAsync(path, progress, ctx.Ct);
            if (!r.HasEmbeddedMd5) return ActionResult.Fail($"{Path.GetFileName(path)} has no embedded MD5: use the original .tar.md5 file.");
            if (!r.Md5Matches) return ActionResult.Fail($"MD5 MISMATCH in {Path.GetFileName(path)}: the file is damaged or modified. Download it again.");
            log.Ok($"MD5 OK · {r.Entries.Count} file(s)");
            packages.Add(new FirmwarePackage(file, r.Entries));
        }

        // 2) Phone identity (model + bootloader binary + battery)
        var (model, bootloader, battery) = await ReadDeviceAsync(ctx);
        if (string.IsNullOrWhiteSpace(model)) return ActionResult.Fail("Phone model is required to check compatibility.");

        var ap = packages.FirstOrDefault(p => p.File.Part == SamsungPart.AP)?.File.Build;
        var compat = SamsungCompatibility.Check(ap, model, bootloader);
        log.Value("Compatibility", compat.Message);
        if (compat.Verdict is CompatibilityVerdict.WrongModel or CompatibilityVerdict.Downgrade or CompatibilityVerdict.Unknown)
            return ActionResult.Fail(compat.Message);

        // 3) Plan: PIT chosen by the user, otherwise the PIT inside CSC / HOME_CSC. The PIT is only READ, never written.
        IReadOnlyList<PitEntry>? pit;
        if (!string.IsNullOrWhiteSpace(pitPath))
        {
            pit = SamsungFlashPlanner.ReadPitFile(pitPath);
            log.Value("PIT", $"{Path.GetFileName(pitPath)} ({pit.Count} partitions, used for checking only)");
        }
        else
        {
            pit = await SamsungFlashPlanner.ReadPackagePitAsync(packages, ctx.Ct);
            if (pit is null) return ActionResult.Fail("No partition table: add the CSC / HOME_CSC file or choose a PIT file.");
        }

        var plan = SamsungFlashPlanner.Build(packages, pit);
        LogPlan(log, plan);
        if (!plan.CanFlash) return ActionResult.Fail("Plan refused: " + string.Join(" | ", plan.Errors));

        if (dryRun)
        {
            log.Ok("DRY RUN: all checks passed. Nothing was written to the phone.");
            return ActionResult.Ok($"Plan OK: {plan.Images.Count} partition(s), {(plan.WipesData ? "ERASES user data" : "keeps user data")}");
        }

        // 4) Battery and disk space
        if (battery is { } b && b < MinBattery) return ActionResult.Fail($"Battery is {b}%. Charge above {MinBattery}% before flashing.");
        if (battery is null) log.Warn("Battery level unknown (phone not in ADB): make sure it is above 50%.");

        var workDir = FlashWorkDir.Choose(Path.GetDirectoryName(Path.GetFullPath(paths[0]))!);
        log.Value("Work folder", workDir);
        var needed = plan.Images.Sum(i => i.Lz4 ? i.PackedSize * 3 : i.PackedSize) + (1L << 30);
        var free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(workDir))!).AvailableFreeSpace;
        log.Value("Disk space", $"{FileSizeConverter.Format(free)} free, ~{FileSizeConverter.Format(needed)} needed");
        if (free < needed) return ActionResult.Fail("Not enough free disk space to extract the images.");

        // 5) Locate Heimdall before asking anything
        var tool = ctx.Get<IExternalToolLocator>().Locate("heimdall");
        if (!tool.IsFound)
            return ActionResult.Fail("heimdall.exe not found. Put Heimdall in a folder named \"heimdall\" on the Desktop (or in the platform-tools folder), then retry.");
        var heimdall = new HeimdallClient(ctx.Get<IProcessRunner>(), tool.Path!);
        log.Value("Heimdall", tool.Path);

        // 6) Confirmation
        var summary =
            $"Flash {plan.FirmwareBuild} to {model}?\n\n" +
            $"Partitions: {string.Join(", ", plan.Images.Select(i => i.PartitionName))}\n" +
            (plan.WipesData ? "\n⚠ ALL USER DATA WILL BE ERASED (CSC / USERDATA).\n" : "\nUser data is kept.\n") +
            "\nEXPERIMENTAL (Heimdall). Do not unplug the cable or close the tool until it finishes." +
            "\nThe owner must agree to this operation.";
        if (!ctx.Dialogs.Confirm(summary, dangerous: true)) return ActionResult.Fail("Cancelled by user");
        if (plan.WipesData &&
            !string.Equals(ctx.Dialogs.Prompt("Type ERASE to confirm that all data on the phone will be deleted:", "")?.Trim(), "ERASE", StringComparison.Ordinal))
            return ActionResult.Fail("Data-wipe not confirmed. Use HOME_CSC (and no USERDATA) to keep data.");

        try
        {
            // 7) Extract and decompress everything BEFORE touching the phone: a damaged LZ4 or a full disk
            //    stops here, and the phone never waits in an open session while big images are unpacked.
            var files = new Dictionary<string, string>();
            foreach (var group in plan.Images.GroupBy(i => i.SourcePackage))
            {
                var extracted = await FirmwareExtractor.ExtractAsync(group.Key, group.ToList(), workDir, log.Muted, ctx.Ct);
                foreach (var (partition, file) in extracted) files[partition] = file;
            }
            log.Ok($"{files.Count} image(s) ready.");

            // 8) Download mode + detection
            if (ctx.Device is { Mode: ConnectionMode.Adb, SerialNumber: { } serial })
            {
                log.Info("Rebooting to Download mode…");
                await ctx.Adb.RebootAsync(serial, AdbRebootTarget.Download, ctx.Ct);
            }
            var (detected, detectOutput) = await WaitAsync(heimdall, log, ctx.Ct);
            if (!detected)
                return Explain(log, detectOutput,
                    "Heimdall cannot see the phone in Download mode. On Windows, Heimdall needs the WinUSB driver for the Samsung " +
                    "Download-mode device (install it with Zadig while the phone is in Download mode). See docs/SAMSUNG_FLASH.md.");

            // 9) The phone's own PIT must match the plan
            var devicePitPath = Path.Combine(workDir, "device.pit");
            var pitResult = await heimdall.DownloadPitAsync(devicePitPath, ctx.Ct);
            if (!pitResult.Success)
            {
                Explain(log, pitResult.Output, "");
                return Abort(log, "Could not read the partition table from the phone.", pitResult.Output);
            }

            var problems = SamsungFlashPlanner.CompareWithDevicePit(plan, SamsungPit.Parse(await File.ReadAllBytesAsync(devicePitPath, ctx.Ct)));
            if (problems.Count > 0) return Abort(log, "The firmware does not match this phone's partitions: " + string.Join(" | ", problems), null);
            log.Ok("Phone partition table matches the firmware.");

            // 10) Flash (not cancellable once started)
            log.Header("FLASHING — do not unplug");
            var lastLine = "";
            var result = await heimdall.FlashAsync(files, resume: true, line =>
            {
                var t = line.Trim();
                if (t.Length == 0 || t == lastLine) return;
                lastLine = t;
                if (t.EndsWith('%') && !t.EndsWith("0%", StringComparison.Ordinal)) return; // keep 10% steps only
                log.Muted(t);
            });

            if (!result.Success)
            {
                log.Error(result.Output);
                Explain(log, result.Output, "");
                log.Warn("The phone stays in Download mode. Retry, or recover it with Samsung Smart Switch (Emergency Software Recovery).");
                return ActionResult.Fail("Heimdall reported an error while flashing.");
            }

            log.Ok("Flash completed. The phone reboots by itself; the first start can take several minutes.");
            return ActionResult.Ok($"Flashed {plan.Images.Count} partition(s) with {plan.FirmwareBuild}");
        }
        finally
        {
            TryDelete(workDir, log);
        }
    }

    private static async Task<(string? Model, string? Bootloader, int? Battery)> ReadDeviceAsync(ActionContext ctx)
    {
        if (ctx.Device is { Mode: ConnectionMode.Adb, SerialNumber: { } serial })
        {
            var model = await ctx.Adb.ShellAsync(serial, ["getprop", "ro.product.model"], ctx.Ct);
            var bl = await ctx.Adb.ShellAsync(serial, ["getprop", "ro.bootloader"], ctx.Ct);
            var dump = await ctx.Adb.ShellAsync(serial, ["dumpsys", "battery"], ctx.Ct) ?? "";
            int? level = null;
            foreach (var line in dump.Split('\n'))
            {
                var t = line.Trim();
                if (t.StartsWith("level:", StringComparison.Ordinal) && int.TryParse(t[6..].Trim(), out var l)) level = l;
            }
            ctx.Log.Value("Phone model", model);
            ctx.Log.Value("Phone bootloader", bl);
            ctx.Log.Value("Battery", level is null ? null : $"{level}%");
            return (model?.Trim(), bl?.Trim(), level);
        }

        ctx.Log.Warn("Phone is not in ADB mode: model is entered by hand and the binary (downgrade) check is skipped.");
        var typed = ctx.Dialogs.Prompt("Type the MODEL NAME shown on the phone's Download-mode screen (e.g. SM-A515F):", "");
        return (typed?.Trim(), null, null);
    }

    private static async Task<(bool Ok, string Output)> WaitAsync(HeimdallClient heimdall, OperationLog log, CancellationToken ct)
    {
        var last = "";
        for (var i = 0; i < 30; i++)
        {
            var r = await heimdall.DetectAsync(ct);
            if (r.Success)
            {
                log.Ok("Phone detected in Download mode.");
                return (true, r.Output);
            }
            last = r.Output;
            if (i == 0) log.Info("Waiting for the phone in Download mode…");
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }
        return (false, last);
    }

    /// <summary>Turns Heimdall output into plain advice in the log (common errors).</summary>
    private static ActionResult Explain(OperationLog log, string? output, string failMessage)
    {
        var arabic = LocalizationService.Get("Lang.IsRtl") == "true";
        foreach (var hint in SamsungFlashErrors.Explain(output))
            log.Warn((arabic ? "حل مقترح: " + hint.Ar : "Suggested fix: " + hint.En));
        return ActionResult.Fail(failMessage);
    }

    private static void LogPlan(OperationLog log, FlashPlan plan)
    {
        log.Header($"Flash plan · {plan.FirmwareBuild}");
        foreach (var i in plan.Images)
            log.Value(i.PartitionName, $"{Path.GetFileName(i.EntryName)}  ({i.Part}, {FileSizeConverter.Format(i.PackedSize)})");
        foreach (var s in plan.Skipped)
            log.Muted($"skip {Path.GetFileName(s.EntryName)}: {s.Reason}");
        log.Value("User data", plan.WipesData ? "ERASED (CSC)" : "kept (HOME_CSC or no CSC)");
        foreach (var e in plan.Errors) log.Error(e);
    }

    private static ActionResult Abort(OperationLog log, string message, string? output)
    {
        if (!string.IsNullOrWhiteSpace(output)) log.Muted(output);
        log.Warn("Nothing was written. To leave Download mode hold Volume Down + Power for about 7 seconds.");
        return ActionResult.Fail(message);
    }

    private static void TryDelete(string dir, OperationLog log)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Muted($"Temporary folder not removed: {dir}");
        }
    }
}

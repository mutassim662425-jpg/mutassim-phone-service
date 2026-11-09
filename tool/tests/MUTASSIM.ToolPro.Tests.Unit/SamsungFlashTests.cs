using System.Security.Cryptography;
using MUTASSIM.ToolPro.Core.Platform;
using MUTASSIM.ToolPro.Firmware;
using Xunit;

namespace MUTASSIM.ToolPro.Tests.Unit;

public class Lz4FrameTests
{
    private static string F(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "SamsungFlash", name);

    [Theory]
    [InlineData("sample.linked_64k_cc.lz4", "sample.bin")]
    [InlineData("sample.indep_256k_bc.lz4", "sample.bin")]
    [InlineData("sample.linked_1m_all.lz4", "sample.bin")]
    [InlineData("sample.indep_4m_none.lz4", "sample.bin")]
    [InlineData("sample.legacy.lz4", "sample.bin")]
    [InlineData("multi.lz4", "multi.bin")]
    public void Decodes_exactly_like_the_reference_lz4(string compressed, string original)
    {
        using var input = File.OpenRead(F(compressed));
        using var output = new MemoryStream();
        var n = Lz4Frame.Decode(input, output);
        var expected = File.ReadAllBytes(F(original));
        Assert.Equal(expected.Length, n);
        Assert.Equal(SHA256.HashData(expected), SHA256.HashData(output.ToArray()));
    }

    [Fact]
    public void Corrupted_data_is_rejected_not_flashed()
    {
        var bytes = File.ReadAllBytes(F("sample.linked_64k_cc.lz4"));
        bytes[bytes.Length / 2] ^= 0x5A;
        Assert.ThrowsAny<Exception>(() => Lz4Frame.Decode(new MemoryStream(bytes), new MemoryStream()));
    }

    [Fact]
    public void Truncated_data_is_rejected()
    {
        var bytes = File.ReadAllBytes(F("sample.linked_1m_all.lz4"));
        Assert.Throws<Lz4FormatException>(() => Lz4Frame.Decode(new MemoryStream(bytes[..^100]), new MemoryStream()));
    }

    [Fact]
    public void Not_lz4_is_rejected() =>
        Assert.Throws<Lz4FormatException>(() => Lz4Frame.Decode(new MemoryStream("hello world"u8.ToArray()), new MemoryStream()));

    [Fact]
    public void XxHash32_matches_reference_vectors()
    {
        Assert.Equal(0x02CC5D05u, XxHash32.Hash([], 0));
        Assert.Equal(0x550D7456u, XxHash32.Hash("a"u8, 0));
        Assert.Equal(0x32D153FFu, XxHash32.Hash("abc"u8, 0));
    }
}

public class SamsungFlashPlannerTests
{
    private const string B = "A515FXXU8HWE1";
    private static string F(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "SamsungFlash", name);
    private static string Bl => F($"BL_{B}_CL1_user_low_ship.tar.md5");
    private static string Ap => F($"AP_{B}_CL1_user_low_ship_meta_OS13.tar.md5");
    private static string Cp => F($"CP_{B}_CP1_CL1_user_low_ship.tar.md5");
    private static string HomeCsc => F($"HOME_CSC_OXM_A515FOXM8HWE1_CL1_user_low_ship.tar.md5");
    private static string Csc => F($"CSC_OXM_A515FOXM8HWE1_CL1_user_low_ship.tar.md5");

    private static async Task<List<FirmwarePackage>> Load(params string[] paths)
    {
        var list = new List<FirmwarePackage>();
        foreach (var p in paths)
        {
            var r = await TarMd5.AnalyzeAsync(p);
            Assert.True(r.Md5Matches, p);
            list.Add(new FirmwarePackage(SamsungFirmwareFile.FromPath(p), r.Entries));
        }
        return list;
    }

    private static IReadOnlyList<PitEntry> DevicePit(string name = "device.pit") => SamsungPit.Parse(File.ReadAllBytes(F(name)));

    [Fact]
    public void Pit_is_parsed()
    {
        var pit = DevicePit();
        Assert.Equal(11, pit.Count);
        Assert.Contains(pit, e => e is { PartitionName: "SUPER", FlashFileName: "super.img" });
        Assert.Throws<PitFormatException>(() => SamsungPit.Parse(new byte[64]));
    }

    [Fact]
    public async Task Home_csc_plan_maps_every_image_and_keeps_data()
    {
        var pkgs = await Load(Bl, Ap, Cp, HomeCsc);
        var pit = await SamsungFlashPlanner.ReadPackagePitAsync(pkgs, default);
        var plan = SamsungFlashPlanner.Build(pkgs, pit!);

        Assert.True(plan.CanFlash, string.Join(" | ", plan.Errors));
        Assert.False(plan.WipesData);
        Assert.Equal(B, plan.FirmwareBuild);
        Assert.Equal(["BOOT", "BOOTLOADER", "CACHE", "OPTICS", "PARAM", "RADIO", "SUPER"],
            plan.Images.Select(i => i.PartitionName).Order());
        Assert.Contains(plan.Skipped, s => s.EntryName.EndsWith(".pit") && s.Reason.Contains("never"));
        Assert.Contains(plan.Skipped, s => s.EntryName.StartsWith("meta-data/"));
        Assert.Empty(SamsungFlashPlanner.CompareWithDevicePit(plan, DevicePit()));
    }

    [Fact]
    public async Task Csc_plan_is_marked_as_wiping_data()
    {
        var pkgs = await Load(Bl, Ap, Cp, Csc);
        var plan = SamsungFlashPlanner.Build(pkgs, (await SamsungFlashPlanner.ReadPackagePitAsync(pkgs, default))!);
        Assert.True(plan.CanFlash);
        Assert.True(plan.WipesData);
    }

    [Fact]
    public async Task Tampered_package_writing_frp_or_efs_is_refused()
    {
        var pkgs = await Load(Bl, F($"AP_{B}_TAMPERED.tar.md5"), HomeCsc);
        var plan = SamsungFlashPlanner.Build(pkgs, DevicePit());
        Assert.False(plan.CanFlash);
        Assert.Contains(plan.Errors, e => e.Contains("PERSISTENT"));
        Assert.Contains(plan.Errors, e => e.Contains("EFS"));
        Assert.DoesNotContain(plan.Images, i => SamsungFlashPlanner.IsProtected(i.PartitionName));
    }

    [Fact]
    public async Task Missing_bl_both_csc_and_mixed_models_are_refused()
    {
        var noBl = SamsungFlashPlanner.Build(await Load(Ap, HomeCsc), DevicePit());
        Assert.Contains(noBl.Errors, e => e.Contains("BL file is required"));

        var both = SamsungFlashPlanner.Build(await Load(Bl, Ap, HomeCsc, Csc), DevicePit());
        Assert.Contains(both.Errors, e => e.Contains("not both"));

        var mixed = SamsungFlashPlanner.Build(await Load(Bl, F("AP_G991BXXU5DWE1_OTHER_MODEL.tar.md5"), HomeCsc), DevicePit());
        Assert.Contains(mixed.Errors, e => e.Contains("different models"));
    }

    [Theory]
    [InlineData("CSC_OXM_A515FOXM8HWE1_CL1_user_low_ship.tar.md5", SamsungPart.CSC, "A515FOXM8HWE1")]
    [InlineData("HOME_CSC_OXM_A515FOXM8HWE1_CL1_user_low_ship.tar.md5", SamsungPart.HOME_CSC, "A515FOXM8HWE1")]
    [InlineData("HOME_CSC_OMC_OXM_A515FOXM5EVK1_CL1_QB1_REV00_user_low_ship.tar.md5", SamsungPart.HOME_CSC, "A515FOXM5EVK1")]
    [InlineData("CSC_OMC_OXM_A515FOXM5EVK1_CL1_QB1_REV00_user_low_ship.tar.md5", SamsungPart.CSC, "A515FOXM5EVK1")]
    [InlineData("AP_A515FXXU5EVK1_CL21976163_QB60061498_REV00_user_low_ship_MULTI_CERT_meta_OS13.tar.md5", SamsungPart.AP, "A515FXXU5EVK1")]
    [InlineData("CP_A515FXXU5EVK1_CP23976163_CL21976163_QB60061498_REV00_user_low_ship.tar.md5", SamsungPart.CP, "A515FXXU5EVK1")]
    [InlineData("AP_A515FXXU8HWE1_CL1_user_low_ship_meta_OS13.tar.md5", SamsungPart.AP, "A515FXXU8HWE1")]
    [InlineData("BL_A515FXXU8HWE1_CL1_user_low_ship.tar.md5", SamsungPart.BL, "A515FXXU8HWE1")]
    public void Real_samsung_file_names_are_parsed(string name, SamsungPart part, string build)
    {
        var f = SamsungFirmwareFile.FromPath(name);
        Assert.Equal(part, f.Part);
        Assert.Equal(build, f.Build?.Build);
        Assert.Equal("A515F", f.Build?.ModelCode);
    }

    [Fact]
    public async Task Damaged_package_fails_md5()
    {
        var r = await TarMd5.AnalyzeAsync(F($"CP_{B}_DAMAGED.tar.md5"));
        Assert.True(r.HasEmbeddedMd5);
        Assert.False(r.Md5Matches);
    }

    [Fact]
    public async Task Phone_with_different_partitions_is_detected()
    {
        var pkgs = await Load(Bl, Ap, Cp, HomeCsc);
        var plan = SamsungFlashPlanner.Build(pkgs, (await SamsungFlashPlanner.ReadPackagePitAsync(pkgs, default))!);
        var problems = SamsungFlashPlanner.CompareWithDevicePit(plan, DevicePit("device_other.pit"));
        Assert.Contains(problems, p => p.Contains("OPTICS"));
    }

    [Fact]
    public async Task Extracted_images_are_byte_identical_to_the_originals()
    {
        var pkgs = await Load(Bl, Ap, Cp, HomeCsc);
        var plan = SamsungFlashPlanner.Build(pkgs, (await SamsungFlashPlanner.ReadPackagePitAsync(pkgs, default))!);
        var expected = File.ReadAllLines(F("images.md5")).Select(l => l.Split(' ')).ToDictionary(p => p[0], p => p[1]);
        var dir = Path.Combine(Path.GetTempPath(), "mutassim-flash-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var g in plan.Images.GroupBy(i => i.SourcePackage))
            {
                var files = await FirmwareExtractor.ExtractAsync(g.Key, g.ToList(), dir, null, default);
                foreach (var (_, path) in files)
                    Assert.Equal(expected[Path.GetFileName(path)],
                        Convert.ToHexString(MD5.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
            }
            Assert.Equal(plan.Images.Count, Directory.GetFiles(dir).Length);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Compatibility_blocks_wrong_model_and_downgrade()
    {
        var fw = SamsungBuild.Parse(B);
        Assert.Equal(CompatibilityVerdict.Compatible, SamsungCompatibility.Check(fw, "SM-A515F", "A515FXXU8HVA1").Verdict);
        Assert.Equal(CompatibilityVerdict.WrongModel, SamsungCompatibility.Check(fw, "SM-G991B", null).Verdict);
        Assert.Equal(CompatibilityVerdict.Downgrade, SamsungCompatibility.Check(fw, "SM-A515F", "A515FXXU9HXA1").Verdict);
    }
}

public class HeimdallClientTests
{
    private sealed class FakeRunner : IProcessRunner
    {
        public List<(string File, IReadOnlyList<string> Args, CancellationToken Ct)> Calls { get; } = [];
        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout,
            CancellationToken cancellationToken = default, Action<string>? onOutputLine = null)
        {
            Calls.Add((fileName, arguments, cancellationToken));
            onOutputLine?.Invoke("Uploading BOOT");
            onOutputLine?.Invoke("100%");
            return Task.FromResult(new ProcessResult(0, "ok", "", TimeSpan.Zero, false));
        }
    }

    [Fact]
    public void Flash_arguments_are_one_pair_per_partition_and_never_repartition()
    {
        var args = HeimdallClient.BuildFlashArguments(
            new Dictionary<string, string> { ["SUPER"] = @"C:\t\super.img", ["BOOT"] = @"C:\t\boot.img" }, resume: true);
        Assert.Equal(["flash", "--resume", "--BOOT", @"C:\t\boot.img", "--SUPER", @"C:\t\super.img"], args);
        Assert.DoesNotContain(args, a => a.Contains("repartition") || a.Contains("--pit"));
    }

    [Theory]
    [InlineData("PERSISTENT")]
    [InlineData("EFS")]
    [InlineData("SEC_EFS")]
    [InlineData("PIT")]
    [InlineData("BOOT;rm")]
    public void Protected_or_odd_partitions_are_refused(string partition) =>
        Assert.Throws<InvalidOperationException>(() =>
            HeimdallClient.BuildFlashArguments(new Dictionary<string, string> { [partition] = "x.img" }, false));

    [Fact]
    public async Task Flash_cannot_be_cancelled_midway()
    {
        var runner = new FakeRunner();
        var lines = new List<string>();
        var r = await new HeimdallClient(runner, "heimdall.exe")
            .FlashAsync(new Dictionary<string, string> { ["BOOT"] = "boot.img" }, true, lines.Add);
        Assert.True(r.Success);
        Assert.False(runner.Calls[0].Ct.CanBeCanceled);
        Assert.Equal(["Uploading BOOT", "100%"], lines);
    }

    [Fact]
    public async Task Pit_download_keeps_the_session_open()
    {
        var runner = new FakeRunner();
        await new HeimdallClient(runner, "heimdall.exe").DownloadPitAsync("dev.pit", default);
        Assert.Equal(["download-pit", "--output", "dev.pit", "--no-reboot"], runner.Calls[0].Args);
    }
}

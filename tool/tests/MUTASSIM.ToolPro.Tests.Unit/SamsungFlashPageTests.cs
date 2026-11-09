using System.IO.Compression;
using MUTASSIM.ToolPro.Firmware;
using Xunit;

namespace MUTASSIM.ToolPro.Tests.Unit;

public class SamsungFlashPageTests : IDisposable
{
    private const string B = "A515FXXU8HWE1";
    private static string F(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "SamsungFlash", name);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mutassim-page-test-" + Guid.NewGuid().ToString("N"));

    public SamsungFlashPageTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    private static readonly string[] FirmwareSet =
    [
        $"AP_{B}_CL1_user_low_ship_meta_OS13.tar.md5", $"BL_{B}_CL1_user_low_ship.tar.md5",
        $"CP_{B}_CP1_CL1_user_low_ship.tar.md5", "HOME_CSC_OXM_A515FOXM8HWE1_CL1_user_low_ship.tar.md5",
        "CSC_OXM_A515FOXM8HWE1_CL1_user_low_ship.tar.md5"
    ];

    private string MakeZip(string name, Action<ZipArchive> fill)
    {
        var path = Path.Combine(_dir, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        fill(zip);
        return path;
    }

    [Fact]
    public async Task Zip_is_extracted_and_sorted_into_slots_home_csc_first()
    {
        var zip = MakeZip("SM-A515F_fw.zip", z =>
        {
            foreach (var f in FirmwareSet) z.CreateEntryFromFile(F(f), "SM-A515F_OXM/" + f);
            z.CreateEntryFromFile(F("device.pit"), "SM-A515F_OXM/A51_EUR_OPEN.pit");
            using var w = new StreamWriter(z.CreateEntry("readme.txt").Open());
            w.Write("hello");
        });

        var r = await FirmwareZip.ImportAsync(zip, null, null, default);

        Assert.Equal(SamsungPart.HOME_CSC, SamsungFirmwareFile.FromPath(r.Parts[SamsungPart.HOME_CSC]).Part);
        Assert.False(r.Parts.ContainsKey(SamsungPart.CSC));
        Assert.Single(r.AlternativeCsc);
        Assert.True(r.Parts.ContainsKey(SamsungPart.AP) && r.Parts.ContainsKey(SamsungPart.BL) && r.Parts.ContainsKey(SamsungPart.CP));
        Assert.EndsWith("A51_EUR_OPEN.pit", r.Pit);
        Assert.Contains("readme.txt", r.Ignored);
        Assert.All(r.Parts.Values, p => Assert.Equal(r.Folder, Path.GetDirectoryName(p)));  // folders flattened
        Assert.True((await TarMd5.AnalyzeAsync(r.Parts[SamsungPart.AP])).Md5Matches);        // extracted intact
    }

    [Fact]
    public async Task Zip_inside_zip_is_opened()
    {
        var inner = MakeZip("inner.zip", z => { foreach (var f in FirmwareSet[..4]) z.CreateEntryFromFile(F(f), f); });
        var outer = MakeZip("outer.zip", z => z.CreateEntryFromFile(inner, "inner.zip"));
        var r = await FirmwareZip.ImportAsync(outer, null, null, default);
        Assert.Equal(4, r.Parts.Count);
    }

    [Fact]
    public async Task Zip_entry_with_dotdot_cannot_escape_the_folder()
    {
        var zip = MakeZip("evil.zip", z => z.CreateEntryFromFile(F(FirmwareSet[0]), "../../" + FirmwareSet[0]));
        var r = await FirmwareZip.ImportAsync(zip, null, null, default);
        Assert.StartsWith(r.Folder, r.Parts[SamsungPart.AP]);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_dir)!, FirmwareSet[0])));
    }

    [Fact]
    public async Task Zip_without_firmware_is_refused()
    {
        var zip = MakeZip("photos.zip", z => { using var w = new StreamWriter(z.CreateEntry("a.jpg").Open()); w.Write("x"); });
        await Assert.ThrowsAsync<InvalidDataException>(() => FirmwareZip.ImportAsync(zip, null, null, default));
    }

    private static async Task<List<FirmwarePackage>> Load(params string[] names)
    {
        var list = new List<FirmwarePackage>();
        foreach (var n in names)
            list.Add(new FirmwarePackage(SamsungFirmwareFile.FromPath(F(n)), (await TarMd5.AnalyzeAsync(F(n))).Entries));
        return list;
    }

    [Fact]
    public async Task Userdata_package_and_factory_ap_are_detected_as_wiping_data()
    {
        var pit = SamsungFlashPlanner.ReadPitFile(F("device.pit"));
        var withUserdata = SamsungFlashPlanner.Build(await Load(FirmwareSet[0], FirmwareSet[1], FirmwareSet[3], $"USERDATA_{B}_CL1_user_low_ship.tar.md5"), pit);
        Assert.True(withUserdata.CanFlash, string.Join(" | ", withUserdata.Errors));
        Assert.True(withUserdata.WipesData);
        Assert.Contains(withUserdata.Images, i => i.PartitionName == "USERDATA");

        // HOME_CSC alone would keep data, but this AP writes USERDATA: must be reported as erasing.
        var factory = SamsungFlashPlanner.Build(await Load($"AP_{B}_FACTORY.tar.md5", FirmwareSet[1], FirmwareSet[3]), pit);
        Assert.True(factory.WipesData);
    }

    [Fact]
    public async Task Same_file_twice_is_refused()
    {
        var plan = SamsungFlashPlanner.Build(await Load(FirmwareSet[0], FirmwareSet[0], FirmwareSet[1]), SamsungFlashPlanner.ReadPitFile(F("device.pit")));
        Assert.Contains(plan.Errors, e => e.Contains("twice") || e.Contains("More than one"));
    }

    [Fact]
    public void Pit_file_is_validated()
    {
        Assert.Equal(11, SamsungFlashPlanner.ReadPitFile(F("device.pit")).Count);
        Assert.Throws<PitFormatException>(() => SamsungFlashPlanner.ReadPitFile(F("images.md5")));
        Assert.Throws<PitFormatException>(() => SamsungFlashPlanner.ReadPitFile(F("sample.bin")));
    }

    [Fact]
    public void Userdata_file_name_is_recognised() =>
        Assert.Equal(SamsungPart.USERDATA, SamsungFirmwareFile.FromPath($"USERDATA_{B}_CL1_user_low_ship.tar.md5").Part);

    [Fact]
    public void Work_folder_is_always_plain_ascii()
    {
        Assert.True(FlashWorkDir.IsAsciiPath(FlashWorkDir.Choose("/fw/A515F")));
        var arabic = FlashWorkDir.Choose("/home/معتصم/سوفت");
        Assert.True(FlashWorkDir.IsAsciiPath(Path.GetFullPath(arabic)));
    }

    [Theory]
    [InlineData("ERROR: Failed to access device. libusb error: -12", "WinUSB")]
    [InlineData("ERROR: Failed to detect compatible download-mode device.", "Download")]
    [InlineData("ERROR: Failed to claim interface", "Another program")]
    [InlineData("ERROR: Protocol initialisation failed!", "session")]
    [InlineData("ERROR: Failed to receive session end confirmation!", "PASS")]
    [InlineData("ERROR: Failed to send file part packet!", "cable")]
    public void Common_heimdall_errors_are_explained(string output, string expected)
    {
        var hints = SamsungFlashErrors.Explain(output);
        Assert.Contains(hints, h => h.En.Contains(expected, StringComparison.OrdinalIgnoreCase));
        Assert.All(hints, h => Assert.False(string.IsNullOrWhiteSpace(h.Ar)));
    }

    [Fact]
    public void Unknown_output_gives_no_false_advice() => Assert.Empty(SamsungFlashErrors.Explain("Uploading BOOT 100%"));
}

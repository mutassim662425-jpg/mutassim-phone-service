using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text;
using MUTASSIM.ToolPro.Firmware;
using MUTASSIM.ToolPro.Usb;

namespace MUTASSIM.ToolPro.Tests.Unit;

public class SamsungFirmwareTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mtp-fw-" + Guid.NewGuid().ToString("N"));

    public SamsungFirmwareTests() => Directory.CreateDirectory(_dir);

    [Theory]
    [InlineData("A515FXXU5EVK1", "A515F", 5)]
    [InlineData("S918BXXS3BWK5", "S918B", 3)]
    [InlineData("G991BXXUFFXA1", "G991B", 15)] // letter binary: F = 15
    public void Parses_build(string build, string model, int binary)
    {
        var b = SamsungBuild.Parse(build)!;
        Assert.Equal(model, b.ModelCode);
        Assert.Equal(binary, b.Binary);
    }

    [Fact]
    public void Parses_file_names()
    {
        var ap = SamsungFirmwareFile.FromPath(@"C:\fw\AP_A515FXXU5EVK1_CL21976163_QB60061498_REV00_user_low_ship_MULTI_CERT_meta_OS13.tar.md5");
        Assert.Equal(SamsungPart.AP, ap.Part);
        Assert.Equal("A515FXXU5EVK1", ap.Build!.Build);
        Assert.Equal(SamsungPart.HOME_CSC, SamsungFirmwareFile.FromPath("HOME_CSC_OMC_OXM_A515FOXM5EVK1_CL1_QB1_REV00_user_low_ship.tar.md5").Part);
        Assert.Equal(SamsungPart.Unknown, SamsungFirmwareFile.FromPath("random.zip").Part);
    }

    [Theory]
    [InlineData("A515FXXU5EVK1", "SM-A515F", "A515FXXU5EVK1", CompatibilityVerdict.Compatible)]
    [InlineData("A515FXXU6FWK1", "SM-A515F", "A515FXXU5EVK1", CompatibilityVerdict.Compatible)]
    [InlineData("A515FXXU4DVA1", "SM-A515F", "A515FXXU5EVK1", CompatibilityVerdict.Downgrade)]
    [InlineData("A525FXXU5EVK1", "SM-A515F", "A515FXXU5EVK1", CompatibilityVerdict.WrongModel)]
    public void Compatibility_rules(string fwBuild, string model, string deviceBl, CompatibilityVerdict expected) =>
        Assert.Equal(expected, SamsungCompatibility.Check(SamsungBuild.Parse(fwBuild), model, deviceBl).Verdict);

    [Fact]
    public async Task Verifies_tar_md5_and_lists_entries()
    {
        var path = await CreateTarMd5("AP_A515FXXU5EVK1_test.tar.md5", corrupt: false);
        var r = await TarMd5.AnalyzeAsync(path);

        Assert.True(r.HasEmbeddedMd5);
        Assert.True(r.Md5Matches);
        Assert.Equal(new[] { "boot.img.lz4", "super.img.lz4" }, r.Entries.Select(e => e.Name));
        Assert.Equal(3000, r.Entries[0].Size);
    }

    [Fact]
    public async Task Detects_damaged_package()
    {
        var path = await CreateTarMd5("AP_bad.tar.md5", corrupt: true);
        var r = await TarMd5.AnalyzeAsync(path);
        Assert.True(r.HasEmbeddedMd5);
        Assert.False(r.Md5Matches);
    }

    [Fact]
    public async Task Plain_tar_has_no_md5()
    {
        var path = Path.Combine(_dir, "BL_plain.tar");
        await File.WriteAllBytesAsync(path, BuildTar());
        var r = await TarMd5.AnalyzeAsync(path);
        Assert.False(r.HasEmbeddedMd5);
        Assert.Equal(2, r.Entries.Count);
    }

    [Theory]
    [InlineData("TECNO CAMON 40 Pro 5G", "tecno")]
    [InlineData("SAMSUNG Android", "samsung")]
    [InlineData("moto g54 5G", "motorola")]
    [InlineData("MediaTek PreLoader USB VCOM (Android)", null)]
    [InlineData(null, null)]
    public void Brand_from_usb_product_name(string? name, string? brand) =>
        Assert.Equal(brand, UsbDeviceDetector.BrandFromProductName(name));

    private byte[] BuildTar()
    {
        using var ms = new MemoryStream();
        using (var writer = new TarWriter(ms, TarEntryFormat.Ustar, leaveOpen: true))
        {
            foreach (var (name, size) in new[] { ("boot.img.lz4", 3000), ("super.img.lz4", 700) })
            {
                var entry = new UstarTarEntry(TarEntryType.RegularFile, name)
                {
                    DataStream = new MemoryStream(Enumerable.Range(0, size).Select(i => (byte)(i % 251)).ToArray())
                };
                writer.WriteEntry(entry);
            }
        }
        return ms.ToArray();
    }

    private async Task<string> CreateTarMd5(string name, bool corrupt)
    {
        var tar = BuildTar();
        var md5 = Convert.ToHexString(MD5.HashData(tar)).ToLowerInvariant();
        if (corrupt) tar[600] ^= 0xFF; // damage file content after computing the hash
        var path = Path.Combine(_dir, name);
        await using var fs = File.Create(path);
        await fs.WriteAsync(tar);
        await fs.WriteAsync(Encoding.ASCII.GetBytes($"{md5}  {name[..^4]}\n"));
        return path;
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }
}

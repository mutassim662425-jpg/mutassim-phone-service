using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MUTASSIM.ToolPro.Infrastructure;
using MUTASSIM.ToolPro.Infrastructure.Catalog;

namespace MUTASSIM.ToolPro.Tests.Unit;

public class PlayImporterTests : IDisposable
{
    private const string Csv = """
        Retail Branding,Marketing Name,Device,Model
        Samsung,Galaxy A51,a51,SM-A515F
        Samsung,Galaxy A51,a51,SM-A515F/DS
        Xiaomi,Redmi Note 12,topaz,23021RAAEG
        POCO,"POCO X5 5G",moonstone,22111317PG
        TECNO,"TECNO CAMON 20, Pro",CK7n,TECNO CK7n
        motorola,moto g54 5G,cancunf,XT2343-2
        UnknownBrand,Some Phone,dev1,M1
        Google,,blank,X
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mtp-play-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Parses_utf16_csv_with_bom_and_quotes()
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(Csv)).ToArray();
        var rows = PlayDeviceListImporter.ParseCsv(PlayDeviceListImporter.Decode(bytes));

        Assert.Equal(8, rows.Count);
        Assert.Equal(new PlayDeviceRow("Samsung", "Galaxy A51", "a51", "SM-A515F"), rows[0]);
        Assert.Equal("TECNO CAMON 20, Pro", rows[4].MarketingName); // comma inside quotes
    }

    [Fact]
    public void Rejects_unexpected_header() =>
        Assert.Throws<InvalidDataException>(() => PlayDeviceListImporter.ParseCsv("a,b,c\n1,2,3"));

    [Fact]
    public void Parses_html_table()
    {
        const string html = """
            <table><tr><th>Retail Branding</th><th>Marketing Name</th><th>Device</th><th>Model</th></tr>
            <tr><td>Samsung</td><td>Galaxy S23</td><td>dm1q</td><td>SM-S911B</td></tr>
            <tr><td>OPPO</td><td>Reno8 &amp; 5G</td><td>OP4F2F</td><td>CPH2359</td></tr></table>
            """;
        var rows = PlayDeviceListImporter.ParseHtml(html);
        Assert.Equal(2, rows.Count);
        Assert.Equal("Reno8 & 5G", rows[1].MarketingName);
    }

    [Fact]
    public void Builds_models_per_device_and_skips_unknown_brands()
    {
        var models = PlayDeviceListImporter.BuildModels(PlayDeviceListImporter.ParseCsv(Csv));

        var a51 = Assert.Single(models, m => m.Codename == "a51");
        Assert.Equal("ws.samsung", a51.Brand);
        Assert.Equal("Galaxy A", a51.Series);
        Assert.Equal("SM-A515F, SM-A515F/DS", a51.ModelCode);

        Assert.Equal("ws.xiaomi", models.Single(m => m.Codename == "moonstone").Brand);
        Assert.Equal("ws.tecno", models.Single(m => m.Codename == "CK7n").Brand);
        Assert.Equal("ws.moto", models.Single(m => m.Codename == "cancunf").Brand);
        Assert.DoesNotContain(models, m => m.Codename is "dev1" or "blank");
    }

    [Theory]
    [InlineData("Xiaomi", "Redmi Note 12 4G", "Redmi Note")]
    [InlineData("Samsung", "Galaxy A51", "Galaxy A")]
    [InlineData("motorola", "moto g54 5G", "moto g")]
    [InlineData("POCO", "POCO X5 5G", "POCO X")]
    [InlineData("Google", "Pixel 7", "Pixel")]
    [InlineData("Xiaomi", "11T", "Xiaomi")]
    public void Series_from_name(string brand, string name, string series) =>
        Assert.Equal(series, PlayDeviceListImporter.SeriesOf(brand, name));

    [Theory]
    [InlineData("Xiaomi", "11T", "Xiaomi 11T")]
    [InlineData("Samsung", "Galaxy A51", "Galaxy A51")]
    [InlineData("TECNO", "TECNO CAMON 20", "TECNO CAMON 20")]
    public void Display_name_adds_brand_only_when_missing(string brand, string name, string expected) =>
        Assert.Equal(expected, PlayDeviceListImporter.DisplayName(brand, name));

    [Fact]
    public void Imported_list_merges_with_built_in_by_codename_and_keeps_chipset()
    {
        var dir = Path.Combine(_root, "catalog");
        Directory.CreateDirectory(dir);
        var imported = PlayDeviceListImporter.BuildModels(PlayDeviceListImporter.ParseCsv(Csv));
        File.WriteAllText(Path.Combine(dir, PlayDeviceListImporter.OutputFileName), JsonSerializer.Serialize(imported));

        var catalog = DeviceCatalog.Load(new AppPaths(_root), NullLogger.Instance);

        // Google calls it "Redmi Note 12"; the built-in entry "Redmi Note 12 4G" (topaz) absorbs it.
        var topaz = Assert.Single(catalog.Models, m => m.Codename == "topaz");
        Assert.Equal("Redmi Note 12 4G", topaz.Name);
        Assert.Equal("Snapdragon 685", topaz.Chipset);
        Assert.Equal("23021RAAEG", topaz.ModelCode);

        // New device from the list appears.
        Assert.Contains(catalog.Models, m => m.Codename == "CK7n");

        // Built-in Samsung entry gains the model numbers but keeps its chipset.
        var a51 = catalog.Models.Single(m => m.Brand == "ws.samsung" && m.Name == "Galaxy A51");
        Assert.Equal("Exynos 9611", a51.Chipset);
    }

    [Fact]
    public void Reload_picks_up_new_files_and_raises_changed()
    {
        var catalog = DeviceCatalog.Load(new AppPaths(_root), NullLogger.Instance);
        var before = catalog.Models.Count;
        var raised = false;
        catalog.Changed += (_, _) => raised = true;

        var dir = Path.Combine(_root, "catalog");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "extra.json"),
            """[{ "brand": "ws.vivo", "series": "vivo", "name": "vivo TEST 1", "codename": "test1" }]""");
        catalog.Reload();

        Assert.True(raised);
        Assert.Equal(before + 1, catalog.Models.Count);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { } // includes DirectoryNotFoundException
    }
}

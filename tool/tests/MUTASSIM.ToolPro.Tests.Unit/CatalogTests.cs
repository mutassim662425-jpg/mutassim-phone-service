using Microsoft.Extensions.Logging.Abstractions;
using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Infrastructure;
using MUTASSIM.ToolPro.Infrastructure.Catalog;

namespace MUTASSIM.ToolPro.Tests.Unit;

public class CatalogTests : IDisposable
{
    private static readonly string[] KnownRoutes =
    [
        "ws.samsung", "ws.xiaomi", "ws.huawei", "ws.oppo", "ws.vivo", "ws.tecno", "ws.moto", "ws.other", "ws.apple"
    ];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mtp-cat-" + Guid.NewGuid().ToString("N"));

    private DeviceCatalog Load() => DeviceCatalog.Load(new AppPaths(_root), NullLogger.Instance);

    [Fact]
    public void Built_in_catalog_loads_and_is_consistent()
    {
        var catalog = Load();
        Assert.True(catalog.Models.Count >= 100, $"only {catalog.Models.Count} models");
        Assert.All(catalog.Models, m =>
        {
            Assert.Contains(m.Brand, KnownRoutes);
            Assert.False(string.IsNullOrWhiteSpace(m.Series));
            Assert.False(string.IsNullOrWhiteSpace(m.Name));
        });
        var duplicates = catalog.Models.GroupBy(m => (m.Brand, m.Name)).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(duplicates);
    }

    [Fact]
    public void Every_brand_page_has_models_and_chipset_pages_are_populated()
    {
        var catalog = Load();
        foreach (var route in KnownRoutes) Assert.NotEmpty(catalog.ForBrand(route));
        Assert.NotEmpty(catalog.ForFamily(ChipsetFamily.Qualcomm));
        Assert.NotEmpty(catalog.ForFamily(ChipsetFamily.MediaTek));
    }

    [Fact]
    public void Finds_connected_device_by_codename_model_code_or_name()
    {
        var catalog = Load();
        var xiaomi = new DeviceInfo { Key = "adb:1", Mode = ConnectionMode.Adb, Source = "ADB", ModelNumber = "topaz", Model = "23021RAAEG" };
        Assert.Equal("Redmi Note 12 4G", catalog.FindForDevice(xiaomi)?.Name);

        var samsung = new DeviceInfo { Key = "adb:2", Mode = ConnectionMode.Adb, Source = "ADB", Model = "SM-A515F", ModelNumber = "a51" };
        Assert.Equal("Galaxy A51", catalog.FindForDevice(samsung)?.Name);

        var tecno = new DeviceInfo { Key = "usb:3", Mode = ConnectionMode.Mtp, Source = "USB", Manufacturer = "TECNO", Model = "TECNO CAMON 40 Pro 5G" };
        Assert.Equal("TECNO CAMON 40 Pro 5G", catalog.FindForDevice(tecno)?.Name);

        var unknown = new DeviceInfo { Key = "adb:4", Mode = ConnectionMode.Adb, Source = "ADB", Model = "Nothing" };
        Assert.Null(catalog.FindForDevice(unknown));
    }

    [Theory]
    [InlineData("redmi note 12", true)]
    [InlineData("topaz", true)]
    [InlineData("snapdragon 685", true)]
    [InlineData("galaxy", false)]
    [InlineData("", true)]
    public void Search_matches_all_terms(string query, bool expected)
    {
        var m = new CatalogModel { Brand = "ws.xiaomi", Series = "Redmi Note", Name = "Redmi Note 12 4G", Codename = "topaz", Chipset = "Snapdragon 685" };
        Assert.Equal(expected, DeviceCatalog.Matches(m, query));
    }

    [Theory]
    [InlineData("Snapdragon 8 Gen 2", ChipsetFamily.Qualcomm)]
    [InlineData("Helio G85", ChipsetFamily.MediaTek)]
    [InlineData("Dimensity 1080", ChipsetFamily.MediaTek)]
    [InlineData("Exynos 9611", ChipsetFamily.SamsungExynos)]
    [InlineData("Kirin 980", ChipsetFamily.HiSilicon)]
    [InlineData("Unisoc T606", ChipsetFamily.Unisoc)]
    [InlineData("Tensor G3", ChipsetFamily.GoogleTensor)]
    [InlineData("A15 Bionic", ChipsetFamily.Apple)]
    [InlineData(null, ChipsetFamily.Unknown)]
    public void Chipset_family(string? chipset, ChipsetFamily expected) => Assert.Equal(expected, DeviceCatalog.FamilyOf(chipset));

    [Fact]
    public void User_catalog_files_add_and_replace_entries()
    {
        var dir = Path.Combine(_root, "catalog");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "extra.json"), """
            [
              { "brand": "ws.xiaomi", "series": "Redmi Note", "name": "Redmi Note 12 4G", "codename": "topaz", "chipset": "Snapdragon 685 (edited)" },
              { "brand": "ws.tecno", "series": "TECNO", "name": "TECNO TEST PHONE", "model": "XX1" }
            ]
            """);
        File.WriteAllText(Path.Combine(dir, "broken.json"), "{ not json");

        var catalog = Load();
        Assert.Single(catalog.Models, m => m.Name == "Redmi Note 12 4G");
        Assert.Equal("Snapdragon 685 (edited)", catalog.Models.Single(m => m.Name == "Redmi Note 12 4G").Chipset);
        Assert.Contains(catalog.Models, m => m.Name == "TECNO TEST PHONE");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { } // includes DirectoryNotFoundException
    }
}

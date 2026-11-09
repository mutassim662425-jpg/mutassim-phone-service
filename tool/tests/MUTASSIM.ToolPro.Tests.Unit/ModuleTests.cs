using Microsoft.Extensions.Logging.Abstractions;
using MUTASSIM.ToolPro.Adb;
using MUTASSIM.ToolPro.Api;
using MUTASSIM.ToolPro.Apple;
using MUTASSIM.ToolPro.Assistance;
using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Core.Modules;
using MUTASSIM.ToolPro.Fastboot;
using MUTASSIM.ToolPro.Firmware;
using MUTASSIM.ToolPro.Honor;
using MUTASSIM.ToolPro.Huawei;
using MUTASSIM.ToolPro.Infrastructure;
using MUTASSIM.ToolPro.MediaTek;
using MUTASSIM.ToolPro.MultiBrand;
using MUTASSIM.ToolPro.Qualcomm;
using MUTASSIM.ToolPro.Samsung;
using MUTASSIM.ToolPro.Usb;
using MUTASSIM.ToolPro.Xiaomi;

namespace MUTASSIM.ToolPro.Tests.Unit;

public class ModuleTests
{
    private static IReadOnlyList<IServiceModule> BuiltIn() =>
    [
        new SamsungModule(), new XiaomiModule(), new HuaweiModule(), new HonorModule(), new AppleModule(),
        .. BrandProfileModule.CreateBuiltIn(),
        new QualcommModule(), new MediaTekModule(), new AdbModule(), new FastbootModule(),
        new FrpModule(), new MdmModule(), new FirmwareModule(), new UsbDriverModule(), new ApiServicesModule()
    ];

    private static ModuleRegistry RegistryWithAll()
    {
        var registry = new ModuleRegistry(NullLogger<ModuleRegistry>.Instance);
        foreach (var m in BuiltIn()) registry.Register(m);
        return registry;
    }

    [Fact]
    public void All_built_in_modules_register_with_unique_ids() => Assert.Equal(BuiltIn().Count, RegistryWithAll().Modules.Count);

    [Fact]
    public void Every_requested_brand_has_a_module()
    {
        var registry = RegistryWithAll();
        string[] brands =
        [
            "samsung", "xiaomi", "redmi", "poco", "huawei", "honor", "oppo", "realme", "vivo", "tecno", "infinix",
            "motorola", "nokia", "google", "oneplus", "zte", "lenovo", "sony", "lg", "apple"
        ];
        foreach (var brand in brands)
        {
            var device = new DeviceInfo { Key = "t:" + brand, Mode = ConnectionMode.Adb, Source = "t", Manufacturer = brand };
            Assert.True(registry.ForDevice(device).Any(m => m.Category == ModuleCategory.Brand), $"No brand module for '{brand}'");
        }
    }

    [Fact]
    public void Duplicate_module_id_is_rejected()
    {
        var registry = new ModuleRegistry(NullLogger<ModuleRegistry>.Instance);
        registry.Register(new SamsungModule());
        Assert.Throws<InvalidOperationException>(() => registry.Register(new SamsungModule()));
    }

    [Fact]
    public void Unknown_device_is_not_claimed_by_any_brand()
    {
        var registry = RegistryWithAll();
        var device = new DeviceInfo { Key = "t:x", Mode = ConnectionMode.Unknown, Source = "t", Manufacturer = "acme" };
        Assert.DoesNotContain(registry.ForDevice(device), m => m.Category == ModuleCategory.Brand);
    }

    [Fact]
    public void Edl_device_maps_to_qualcomm_by_mode()
    {
        var registry = RegistryWithAll();
        var device = new DeviceInfo { Key = "usb:edl", Mode = ConnectionMode.QualcommEdl, Source = "t", UsbVendorId = 0x05C6, UsbProductId = 0x9008 };
        Assert.Contains(registry.ForDevice(device), m => m.Id == ModuleIds.Qualcomm);
    }

    // ---- Honesty guards: these tests fail if anyone marks work as done that is not. ----

    [Fact]
    public void Nothing_is_marked_tested_before_real_hardware_testing()
    {
        var tested = BuiltIn().SelectMany(m => m.Services).Where(s => s.Status == ImplementationStatus.CompletedTested).ToList();
        Assert.Empty(tested);
    }

    [Theory]
    [InlineData(ModuleIds.Frp, "frp.bypass")]
    [InlineData(ModuleIds.Mdm, "mdm.bypass")]
    [InlineData(ModuleIds.Apple, "ios.bypass")]
    public void Protection_bypass_is_never_offered(string moduleId, string serviceId)
    {
        var service = RegistryWithAll().Find(moduleId)!.Services.Single(s => s.Id == serviceId);
        Assert.Equal(ImplementationStatus.NotSupported, service.Status);
    }

    [Theory]
    [InlineData("fb.flash")]
    [InlineData("fb.unlock")]
    [InlineData("sam.flash")]
    [InlineData("qc.firehose")]
    [InlineData("mtk.restore")]
    [InlineData("mi.unlock")]
    [InlineData("mi.flash")]
    public void Partition_writes_and_unlocks_are_not_available_until_verified(string serviceId)
    {
        var service = BuiltIn().SelectMany(m => m.Services).Single(s => s.Id == serviceId);
        Assert.False(service.IsAvailableNow);
    }
}

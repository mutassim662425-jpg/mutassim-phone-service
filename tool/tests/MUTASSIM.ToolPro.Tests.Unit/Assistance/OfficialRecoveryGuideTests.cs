using MUTASSIM.ToolPro.Assistance;

namespace MUTASSIM.ToolPro.Tests.Unit.Assistance;

public class OfficialRecoveryGuideTests
{
    [Theory]
    [InlineData("samsung", DeviceBrand.Samsung)]
    [InlineData("Redmi", DeviceBrand.Xiaomi)]
    [InlineData("POCO", DeviceBrand.Xiaomi)]
    [InlineData("HONOR", DeviceBrand.Honor)]      // honor must win over huawei
    [InlineData("HUAWEI", DeviceBrand.Huawei)]
    [InlineData("TECNO MOBILE LIMITED", DeviceBrand.Tecno)]
    [InlineData("Infinix", DeviceBrand.Infinix)]
    [InlineData("Google", DeviceBrand.Google)]
    [InlineData("", DeviceBrand.Unknown)]
    [InlineData("SomethingElse", DeviceBrand.Other)]
    public void Brand_is_detected_from_manufacturer(string input, DeviceBrand expected) =>
        Assert.Equal(expected, OfficialRecoveryGuide.BrandFrom(input));

    [Fact]
    public void Samsung_guidance_points_to_find_my_mobile()
    {
        var g = OfficialRecoveryGuide.For(DeviceBrand.Samsung, "SM-A515F", "13");
        Assert.Contains(g.OemProcedure, s => s.Contains("findmymobile.samsung.com"));
        Assert.Contains("SM-A515F", g.Summary);
    }

    [Fact]
    public void Every_brand_has_complete_official_guidance()
    {
        foreach (DeviceBrand brand in Enum.GetValues<DeviceBrand>())
        {
            var g = OfficialRecoveryGuide.For(brand);
            Assert.NotEmpty(g.GoogleRecovery);
            Assert.NotEmpty(g.OemProcedure);
            Assert.NotEmpty(g.OwnershipChecklist);
            Assert.StartsWith("https://", g.SupportUrl);
        }
    }

    [Fact]
    public void Guidance_is_owner_recovery_only_never_bypass()
    {
        var g = OfficialRecoveryGuide.ForManufacturer("samsung");
        // The Google steps make the no-bypass principle explicit, and the summary states owner-verification only.
        Assert.Contains(g.GoogleRecovery, s => s.Contains("no legitimate way", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("no bypass", g.Summary, StringComparison.OrdinalIgnoreCase);
    }
}

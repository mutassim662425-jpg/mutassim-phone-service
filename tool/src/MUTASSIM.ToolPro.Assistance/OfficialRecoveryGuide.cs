namespace MUTASSIM.ToolPro.Assistance;

/// <summary>Device makers this tool has an official-recovery path for.</summary>
public enum DeviceBrand
{
    Unknown, Samsung, Xiaomi, Huawei, Honor, Oppo, Realme, OnePlus, Tecno, Infinix, Google, Other
}

/// <summary>
/// Owner-driven, official FRP / Google-account recovery guidance for one device. Every step routes the
/// device owner through an official channel (Google account recovery, the maker's own service) and requires
/// proof of ownership. There is deliberately no account-bypass content here — removing another person's
/// Google account is not a legitimate operation and is not provided.
/// </summary>
public sealed record RecoveryGuidance(
    DeviceBrand Brand,
    string Summary,
    IReadOnlyList<string> GoogleRecovery,
    IReadOnlyList<string> OemProcedure,
    IReadOnlyList<string> OwnershipChecklist,
    string SupportUrl);

/// <summary>
/// Produces <see cref="RecoveryGuidance"/> for a device. Pure, data-driven and unit-tested; the FRP module
/// surfaces it for the <c>frp.diagnose</c> and <c>frp.oem</c> services.
/// </summary>
public static class OfficialRecoveryGuide
{
    /// <summary>Maps a manufacturer / brand string (e.g. ro.product.manufacturer) to a known brand.</summary>
    public static DeviceBrand BrandFrom(string? manufacturerOrBrand)
    {
        var v = (manufacturerOrBrand ?? "").Trim().ToLowerInvariant();
        if (v.Length == 0) return DeviceBrand.Unknown;
        if (v.Contains("samsung")) return DeviceBrand.Samsung;
        if (v.Contains("xiaomi") || v.Contains("redmi") || v.Contains("poco")) return DeviceBrand.Xiaomi;
        if (v.Contains("honor")) return DeviceBrand.Honor;          // check honor before huawei
        if (v.Contains("huawei")) return DeviceBrand.Huawei;
        if (v.Contains("oneplus")) return DeviceBrand.OnePlus;
        if (v.Contains("realme")) return DeviceBrand.Realme;
        if (v.Contains("oppo")) return DeviceBrand.Oppo;
        if (v.Contains("tecno")) return DeviceBrand.Tecno;
        if (v.Contains("infinix")) return DeviceBrand.Infinix;
        if (v.Contains("google") || v.Contains("pixel")) return DeviceBrand.Google;
        return DeviceBrand.Other;
    }

    private static readonly IReadOnlyList<string> GoogleSteps =
    [
        "On the device, connect to Wi-Fi and reach the \"Verify your account\" screen after the reset.",
        "Sign in with the Google account that was signed in on this device before it was reset.",
        "Forgot the account or password? Recover it at https://accounts.google.com/signin/recovery using a recovery phone/email you set up.",
        "Once the original Google account is verified, the device passes the check and continues setup.",
        "Note: there is no legitimate way to remove or skip someone else's Google account — only the account owner can clear this."
    ];

    private static readonly IReadOnlyList<string> Checklist =
    [
        "Original purchase invoice / receipt showing the store and date.",
        "IMEI on the invoice (or box) matching the device IMEI (dial *#06# or read it in the tool).",
        "A valid photo ID whose name matches the invoice / account holder.",
        "Original packaging where available."
    ];

    public static RecoveryGuidance For(DeviceBrand brand, string? model = null, string? androidVersion = null)
    {
        var (oem, url) = brand switch
        {
            DeviceBrand.Samsung => (new[]
            {
                "If the Samsung account was set up with Find My Mobile, the owner can sign in at https://findmymobile.samsung.com and use Unlock / remote actions.",
                "Otherwise bring the device to a Samsung authorised service centre with proof of ownership."
            }, "https://www.samsung.com/support/"),
            DeviceBrand.Xiaomi => (new[]
            {
                "Recover the Mi Account the owner used at https://account.xiaomi.com (account & password recovery).",
                "For account/verification issues use a Mi authorised service centre with proof of ownership."
            }, "https://www.mi.com/global/support/"),
            DeviceBrand.Huawei => (new[]
            {
                "Recover the Huawei ID at https://id5.cloud.huawei.com, then verify on device.",
                "For account removal with proof of ownership, use an authorised Huawei service centre / HiCare."
            }, "https://consumer.huawei.com/en/support/"),
            DeviceBrand.Honor => (new[]
            {
                "Recover the Honor ID the owner used, then verify on device.",
                "For account removal with proof of ownership, use an authorised Honor service centre."
            }, "https://www.honor.com/global/support/"),
            DeviceBrand.Oppo or DeviceBrand.Realme or DeviceBrand.OnePlus => (new[]
            {
                "Recover the Google / brand account the owner used.",
                "For account/verification help, use the maker's authorised service centre with proof of ownership."
            }, "https://support.oppo.com/"),
            DeviceBrand.Tecno or DeviceBrand.Infinix => (new[]
            {
                "Recover the Google account the owner used.",
                "For account/verification help, use a Carlcare (Transsion) authorised service centre with proof of ownership."
            }, "https://www.carlcare.com/"),
            DeviceBrand.Google => (new[]
            {
                "Recover the Google account at https://accounts.google.com/signin/recovery — Pixel devices use the Google account directly."
            }, "https://support.google.com/pixelphone/"),
            _ => (new[]
            {
                "Recover the Google account the owner used at https://accounts.google.com/signin/recovery.",
                "For account/verification help, use the device maker's authorised service centre with proof of ownership."
            }, "https://support.google.com/accounts/")
        };

        var what = string.IsNullOrWhiteSpace(model) ? brand.ToString() : $"{brand} {model}";
        var os = string.IsNullOrWhiteSpace(androidVersion) ? "" : $" (Android {androidVersion})";
        return new RecoveryGuidance(
            brand,
            $"Official account-recovery path for {what}{os}. Owner verification only — no bypass.",
            GoogleSteps,
            oem,
            Checklist,
            url);
    }

    /// <summary>Convenience overload from a manufacturer string.</summary>
    public static RecoveryGuidance ForManufacturer(string? manufacturer, string? model = null, string? androidVersion = null) =>
        For(BrandFrom(manufacturer), model, androidVersion);
}

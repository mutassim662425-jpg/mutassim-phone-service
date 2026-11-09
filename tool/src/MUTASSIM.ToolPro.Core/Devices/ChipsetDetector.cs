namespace MUTASSIM.ToolPro.Core.Devices;

public enum ChipsetFamily
{
    Unknown,
    Qualcomm,
    MediaTek,
    SamsungExynos,
    Unisoc,
    HiSilicon,
    GoogleTensor,
    Apple
}

/// <summary>
/// Determines the chipset family from Android properties. Returns Unknown rather than guessing.
/// Order: ro.soc.manufacturer (Android 12+) → ro.hardware.chipname → ro.board.platform → ro.hardware.
/// </summary>
public static class ChipsetDetector
{
    public static ChipsetFamily FromProperties(IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("ro.soc.manufacturer", out var socMan) && FromManufacturer(socMan) is var m and not ChipsetFamily.Unknown)
            return m;

        foreach (var key in new[] { "ro.hardware.chipname", "ro.board.platform", "ro.hardware", "ro.soc.model" })
        {
            if (props.TryGetValue(key, out var value) && FromPlatform(value) is var f and not ChipsetFamily.Unknown)
                return f;
        }
        return ChipsetFamily.Unknown;
    }

    public static string? DescribeChip(IReadOnlyDictionary<string, string> props)
    {
        string? Get(string k) => props.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
        var model = Get("ro.soc.model") ?? Get("ro.hardware.chipname") ?? Get("ro.board.platform");
        var maker = Get("ro.soc.manufacturer");
        return (maker, model) switch
        {
            (null, null) => null,
            (null, _) => model,
            (_, null) => maker,
            _ => $"{maker} {model}"
        };
    }

    private static ChipsetFamily FromManufacturer(string value)
    {
        var v = value.Trim().ToLowerInvariant();
        if (v.Contains("qualcomm") || v == "qti") return ChipsetFamily.Qualcomm;
        if (v.Contains("mediatek") || v == "mtk") return ChipsetFamily.MediaTek;
        if (v.Contains("samsung")) return ChipsetFamily.SamsungExynos;
        if (v.Contains("unisoc") || v.Contains("spreadtrum")) return ChipsetFamily.Unisoc;
        if (v.Contains("hisilicon")) return ChipsetFamily.HiSilicon;
        if (v.Contains("google")) return ChipsetFamily.GoogleTensor;
        return ChipsetFamily.Unknown;
    }

    private static ChipsetFamily FromPlatform(string value)
    {
        var v = value.Trim().ToLowerInvariant();
        if (v.Length == 0) return ChipsetFamily.Unknown;
        if (v.StartsWith("mt") && v.Length > 2 && char.IsDigit(v[2])) return ChipsetFamily.MediaTek;
        if (v.StartsWith("msm") || v.StartsWith("sdm") || v.StartsWith("sm") && v.Length > 2 && char.IsDigit(v[2]) ||
            v.StartsWith("qcom") ||
            v is "kona" or "lahaina" or "taro" or "kalama" or "pineapple" or "lito" or "bengal" or "holi" or
                "trinket" or "atoll" or "msmnile" or "sun" or "parrot" or "crow" or "blair" or "khaje")
            return ChipsetFamily.Qualcomm;
        if (v.StartsWith("exynos") || v.StartsWith("universal") || v.StartsWith("s5e")) return ChipsetFamily.SamsungExynos;
        if (v.StartsWith("ums") || v.StartsWith("sp9") || v.StartsWith("sc9") || v.StartsWith("unisoc")) return ChipsetFamily.Unisoc;
        if (v.StartsWith("kirin") || v.StartsWith("hi3") || v.StartsWith("hi6")) return ChipsetFamily.HiSilicon;
        if (v.StartsWith("gs1") || v.StartsWith("gs2") || v is "zuma" or "zumapro" or "laguna") return ChipsetFamily.GoogleTensor;
        return ChipsetFamily.Unknown;
    }
}

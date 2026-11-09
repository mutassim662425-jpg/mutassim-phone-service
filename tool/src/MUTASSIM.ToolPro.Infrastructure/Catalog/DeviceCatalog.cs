using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using MUTASSIM.ToolPro.Core.Devices;

namespace MUTASSIM.ToolPro.Infrastructure.Catalog;

/// <summary>
/// One reference model shown in the brand model list. Reference data only — live values always come
/// from the connected device. <see cref="Brand"/> is the workspace route (e.g. "ws.xiaomi").
/// </summary>
public sealed record CatalogModel
{
    [JsonPropertyName("brand")] public string Brand { get; init; } = "";
    [JsonPropertyName("series")] public string Series { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("codename")] public string? Codename { get; init; }
    [JsonPropertyName("model")] public string? ModelCode { get; init; }
    [JsonPropertyName("chipset")] public string? Chipset { get; init; }

    [JsonIgnore] public ChipsetFamily Family => DeviceCatalog.FamilyOf(Chipset);

    /// <summary>"(topaz) · Snapdragon 685" — only the parts that are known.</summary>
    [JsonIgnore]
    public string Subtitle => string.Join(" · ", new[]
    {
        Codename is { Length: > 0 } c ? $"({c})" : ModelCode,
        Chipset
    }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <summary>
/// Searchable reference catalog: built-in list (embedded models.json) plus optional user files
/// (%LOCALAPPDATA%\MUTASSIM\ToolPro\catalog\*.json, same format) that add or replace entries.
/// </summary>
public sealed class DeviceCatalog
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private readonly AppPaths? _paths;
    private readonly ILogger? _logger;

    public DeviceCatalog(IReadOnlyList<CatalogModel> models) => Models = models;

    private DeviceCatalog(IReadOnlyList<CatalogModel> models, AppPaths paths, ILogger logger) : this(models)
    {
        _paths = paths;
        _logger = logger;
    }

    public IReadOnlyList<CatalogModel> Models { get; private set; }

    /// <summary>Raised after <see cref="Reload"/> replaced the model list.</summary>
    public event EventHandler? Changed;

    /// <summary>Re-reads the built-in list and the user catalog folder (after an import).</summary>
    public void Reload()
    {
        if (_paths is null || _logger is null) return;
        Models = LoadModels(_paths, _logger);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<CatalogModel> ForBrand(string route) =>
        Models.Where(m => m.Brand.Equals(route, StringComparison.OrdinalIgnoreCase)).ToArray();

    public IReadOnlyList<CatalogModel> ForFamily(ChipsetFamily family) =>
        Models.Where(m => m.Family == family).ToArray();

    /// <summary>Finds the catalog entry of a connected device (codename, model code or exact name). Null when unknown.</summary>
    public CatalogModel? FindForDevice(DeviceInfo? device)
    {
        if (device is null) return null;
        bool Eq(string? a, string? b) => !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
                                         a.Trim().Equals(b.Trim(), StringComparison.OrdinalIgnoreCase);

        return Models.FirstOrDefault(m => Eq(m.Codename, device.ModelNumber) || Eq(m.Codename, device.Product))
            ?? Models.FirstOrDefault(m => Codes(m).Any(c => Eq(c, device.Model) || Eq(c, device.ModelNumber)))
            ?? Models.FirstOrDefault(m => Eq(m.Name, device.Model) ||
                                          Eq($"{device.DisplayManufacturer} {m.Name}", device.Model));
    }

    private static IEnumerable<string> Codes(CatalogModel m) =>
        m.ModelCode?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

    public static bool Matches(CatalogModel m, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var terms = query.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var haystack = $"{m.Name} {m.Codename} {m.ModelCode} {m.Chipset} {m.Series}";
        return terms.All(t => haystack.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    public static ChipsetFamily FamilyOf(string? chipset)
    {
        if (string.IsNullOrWhiteSpace(chipset)) return ChipsetFamily.Unknown;
        var c = chipset.ToLowerInvariant();
        if (c.Contains("snapdragon") || c.Contains("qualcomm")) return ChipsetFamily.Qualcomm;
        if (c.Contains("helio") || c.Contains("dimensity") || c.Contains("mediatek")) return ChipsetFamily.MediaTek;
        if (c.Contains("exynos")) return ChipsetFamily.SamsungExynos;
        if (c.Contains("kirin")) return ChipsetFamily.HiSilicon;
        if (c.Contains("unisoc") || c.Contains("sc9863") || c.Contains("tiger")) return ChipsetFamily.Unisoc;
        if (c.Contains("tensor")) return ChipsetFamily.GoogleTensor;
        if (c.StartsWith("apple", StringComparison.Ordinal) || System.Text.RegularExpressions.Regex.IsMatch(c, @"^a\d{2}\b")) return ChipsetFamily.Apple;
        return ChipsetFamily.Unknown;
    }

    public static IReadOnlyList<CatalogModel> Parse(Stream json) =>
        (JsonSerializer.Deserialize<List<CatalogModel>>(json, Json) ?? [])
            .Where(m => !string.IsNullOrWhiteSpace(m.Brand) && !string.IsNullOrWhiteSpace(m.Name))
            .ToArray();

    public static DeviceCatalog Load(AppPaths paths, ILogger logger) => new(LoadModels(paths, logger), paths, logger);

    /// <summary>
    /// Built-in list first, then every user/imported *.json in the catalog folder. An entry with the same brand
    /// and name replaces the earlier one but keeps fields the newer one leaves empty (e.g. a known chipset).
    /// </summary>
    private static List<CatalogModel> LoadModels(AppPaths paths, ILogger logger)
    {
        var map = new Dictionary<(string, string), CatalogModel>();
        var byCodename = new Dictionary<(string, string), (string, string)>();
        var order = new List<(string, string)>();

        void Merge(IEnumerable<CatalogModel> models)
        {
            foreach (var item in models)
            {
                var m = item;
                var key = (m.Brand.ToLowerInvariant(), m.Name.ToLowerInvariant());
                // Same brand + same codename = same device even when the names differ: keep the first name.
                if (!map.ContainsKey(key) && m.Codename is { Length: > 0 } cn &&
                    byCodename.TryGetValue((key.Item1, cn.ToLowerInvariant()), out var existing))
                {
                    key = existing;
                    m = m with { Name = map[existing].Name };
                }
                if (m.Codename is { Length: > 0 } code) byCodename.TryAdd((key.Item1, code.ToLowerInvariant()), key);

                if (map.TryGetValue(key, out var old))
                {
                    map[key] = m with
                    {
                        Codename = m.Codename ?? old.Codename,
                        ModelCode = m.ModelCode ?? old.ModelCode,
                        Chipset = m.Chipset ?? old.Chipset,
                        Series = string.IsNullOrWhiteSpace(m.Series) ? old.Series : m.Series
                    };
                }
                else
                {
                    map[key] = m;
                    order.Add(key);
                }
            }
        }

        using (var builtIn = typeof(DeviceCatalog).Assembly.GetManifestResourceStream("MUTASSIM.ToolPro.Catalog.models.json"))
        {
            if (builtIn is not null) Merge(Parse(builtIn));
            else logger.LogWarning("Built-in device catalog resource not found");
        }

        var userDir = Path.Combine(paths.Root, "catalog");
        if (Directory.Exists(userDir))
        {
            // Imported Google list first, then hand-made files (so manual corrections win).
            var files = Directory.EnumerateFiles(userDir, "*.json")
                .OrderBy(f => Path.GetFileName(f).Equals(PlayDeviceListImporter.OutputFileName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(f => f, StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                try
                {
                    using var fs = File.OpenRead(file);
                    Merge(Parse(fs));
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
                {
                    logger.LogWarning(ex, "Catalog file {File} ignored", file);
                }
            }
        }

        var list = order.Select(k => map[k]).ToList();
        logger.LogInformation("Device catalog: {Count} models", list.Count);
        return list;
    }
}

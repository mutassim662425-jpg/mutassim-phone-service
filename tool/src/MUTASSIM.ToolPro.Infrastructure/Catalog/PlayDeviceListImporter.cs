using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MUTASSIM.ToolPro.Infrastructure.Catalog;

public sealed record PlayDeviceRow(string Branding, string MarketingName, string Device, string Model);

public sealed record ImportResult(int RowsRead, int ModelsWritten, IReadOnlyDictionary<string, int> PerBrand, string Source, string OutputFile);

/// <summary>
/// Builds the model catalog from Google's public "supported devices" list (Retail Branding, Marketing Name,
/// Device = codename, Model). Downloaded on demand from Google's official storage; nothing is invented.
/// </summary>
public static partial class PlayDeviceListImporter
{
    public static readonly string[] SourceUrls =
    [
        "https://storage.googleapis.com/play_public/supported_devices.csv",
        "https://storage.googleapis.com/play_public/supported_devices.html"
    ];

    public const string OutputFileName = "google_play_devices.json";

    /// <summary>Retail Branding (lower-case) → workspace route. Brands not listed are skipped.</summary>
    private static readonly Dictionary<string, string> BrandRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["samsung"] = "ws.samsung",
        ["xiaomi"] = "ws.xiaomi", ["redmi"] = "ws.xiaomi", ["poco"] = "ws.xiaomi", ["pocophone"] = "ws.xiaomi",
        ["huawei"] = "ws.huawei", ["honor"] = "ws.huawei",
        ["oppo"] = "ws.oppo", ["realme"] = "ws.oppo",
        ["vivo"] = "ws.vivo", ["iqoo"] = "ws.vivo",
        ["tecno"] = "ws.tecno", ["tecno mobile"] = "ws.tecno", ["infinix"] = "ws.tecno", ["itel"] = "ws.tecno",
        ["motorola"] = "ws.moto", ["nokia"] = "ws.moto", ["hmd global"] = "ws.moto", ["hmd"] = "ws.moto",
        ["google"] = "ws.other", ["oneplus"] = "ws.other", ["zte"] = "ws.other", ["nubia"] = "ws.other",
        ["lenovo"] = "ws.other", ["sony"] = "ws.other", ["lge"] = "ws.other", ["lg"] = "ws.other"
    };

    public static string? RouteFor(string branding) =>
        BrandRoutes.TryGetValue(branding.Trim(), out var r) ? r : null;

    // ------------------------------------------------------------------ download

    public static async Task<ImportResult> DownloadAndImportAsync(
        HttpClient http, AppPaths paths, Action<string>? log, CancellationToken ct)
    {
        Exception? last = null;
        foreach (var url in SourceUrls)
        {
            try
            {
                log?.Invoke($"Downloading {url}");
                using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (response.StatusCode != HttpStatusCode.OK)
                    throw new HttpRequestException($"HTTP {(int)response.StatusCode}");

                var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                log?.Invoke($"Received {bytes.Length / 1024:N0} KB");
                var text = Decode(bytes);
                var rows = url.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ? ParseHtml(text) : ParseCsv(text);
                if (rows.Count < 1000)
                    throw new InvalidDataException($"Only {rows.Count} rows parsed; the list format may have changed.");

                var models = BuildModels(rows);
                var dir = Path.Combine(paths.Root, "catalog");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, OutputFileName);
                var tmp = file + ".tmp";
                await using (var fs = File.Create(tmp))
                    await JsonSerializer.SerializeAsync(fs, models, cancellationToken: ct).ConfigureAwait(false);
                File.Move(tmp, file, overwrite: true);

                var perBrand = models.GroupBy(m => m.Brand).ToDictionary(g => g.Key, g => g.Count());
                return new ImportResult(rows.Count, models.Count, perBrand, url, file);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or IOException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                last = ex;
                log?.Invoke($"Failed: {ex.Message}");
            }
        }
        throw new InvalidOperationException("Could not download the device list: " + last?.Message, last);
    }

    /// <summary>The CSV is published as UTF-16 LE with BOM; HTML as UTF-8. Honour the BOM, default to UTF-8.</summary>
    public static string Decode(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    // ------------------------------------------------------------------ parsing

    public static List<PlayDeviceRow> ParseCsv(string text)
    {
        var rows = new List<PlayDeviceRow>();
        var lines = SplitCsv(text);
        if (lines.Count == 0) return rows;

        var header = lines[0].Select(h => h.Trim()).ToList();
        int Col(string name) => header.FindIndex(h => h.Equals(name, StringComparison.OrdinalIgnoreCase));
        int b = Col("Retail Branding"), n = Col("Marketing Name"), d = Col("Device"), m = Col("Model");
        if (b < 0 || n < 0 || d < 0 || m < 0) throw new InvalidDataException("Unexpected CSV header: " + string.Join(",", header));

        foreach (var f in lines.Skip(1))
        {
            if (f.Count <= Math.Max(Math.Max(b, n), Math.Max(d, m))) continue;
            rows.Add(new PlayDeviceRow(f[b].Trim(), f[n].Trim(), f[d].Trim(), f[m].Trim()));
        }
        return rows;
    }

    /// <summary>RFC 4180 CSV split (quotes, doubled quotes, commas and newlines inside quotes).</summary>
    public static List<List<string>> SplitCsv(string text)
    {
        var result = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
                continue;
            }

            switch (c)
            {
                case '"': inQuotes = true; break;
                case ',': row.Add(field.ToString()); field.Clear(); break;
                case '\r': break;
                case '\n':
                    row.Add(field.ToString()); field.Clear();
                    if (row.Count > 1 || row[0].Length > 0) result.Add(row);
                    row = [];
                    break;
                default: field.Append(c); break;
            }
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            result.Add(row);
        }
        return result;
    }

    [GeneratedRegex(@"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex RowRegex();

    [GeneratedRegex(@"<t[dh][^>]*>(.*?)</t[dh]>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex CellRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    public static List<PlayDeviceRow> ParseHtml(string html)
    {
        var rows = new List<PlayDeviceRow>();
        foreach (Match tr in RowRegex().Matches(html))
        {
            var cells = CellRegex().Matches(tr.Groups[1].Value)
                .Select(c => WebUtility.HtmlDecode(TagRegex().Replace(c.Groups[1].Value, "")).Trim())
                .ToList();
            if (cells.Count < 4 || cells[0].Equals("Retail Branding", StringComparison.OrdinalIgnoreCase)) continue;
            rows.Add(new PlayDeviceRow(cells[0], cells[1], cells[2], cells[3]));
        }
        return rows;
    }

    // ------------------------------------------------------------------ catalog building

    /// <summary>
    /// One catalog entry per (route, marketing name, codename); model numbers of the same device are joined.
    /// Rows without a marketing name, or of brands this program has no page for, are skipped.
    /// </summary>
    public static List<CatalogModel> BuildModels(IEnumerable<PlayDeviceRow> rows)
    {
        var map = new Dictionary<(string Route, string Name, string Device), (CatalogModel Model, SortedSet<string> Codes)>();
        foreach (var r in rows)
        {
            var route = RouteFor(r.Branding);
            if (route is null || string.IsNullOrWhiteSpace(r.MarketingName) || string.IsNullOrWhiteSpace(r.Device)) continue;

            var name = DisplayName(r.Branding, r.MarketingName);
            var key = (route, name.ToLowerInvariant(), r.Device.ToLowerInvariant());
            if (!map.TryGetValue(key, out var entry))
            {
                entry = (new CatalogModel
                {
                    Brand = route,
                    Series = SeriesOf(r.Branding, name),
                    Name = name,
                    Codename = r.Device
                }, new SortedSet<string>(StringComparer.OrdinalIgnoreCase));
                map[key] = entry;
            }
            if (!string.IsNullOrWhiteSpace(r.Model)) entry.Codes.Add(r.Model);
        }

        return map.Values
            .Select(v => v.Model with { ModelCode = v.Codes.Count == 0 ? null : string.Join(", ", v.Codes.Take(6)) })
            .OrderBy(m => m.Brand).ThenBy(m => m.Series).ThenBy(m => m.Name)
            .ToList();
    }

    /// <summary>Prefixes the brand when the marketing name lacks it ("Galaxy A51" stays, "11T" → "Xiaomi 11T").</summary>
    public static string DisplayName(string branding, string marketingName)
    {
        var name = marketingName.Trim();
        var brand = branding.Trim();
        var sub = new[] { "galaxy", "redmi", "poco", "pixel", "moto", "nord", "honor", "realme", "iqoo", "nokia" };
        var first = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant() ?? "";
        if (name.StartsWith(brand, StringComparison.OrdinalIgnoreCase) || sub.Contains(first)) return name;
        return $"{brand} {name}";
    }

    /// <summary>"Redmi Note 12 4G" → "Redmi Note"; "Galaxy A51" → "Galaxy A"; "moto g54" → "moto g".</summary>
    public static string SeriesOf(string branding, string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var series = new List<string>();
        foreach (var w in words)
        {
            if (w.Any(char.IsDigit))
            {
                var prefix = new string(w.TakeWhile(char.IsLetter).ToArray());
                if (prefix.Length is > 0 and <= 4 && series.Count > 0) series.Add(prefix);
                break;
            }
            series.Add(w);
            if (series.Count == 3) break;
        }
        return series.Count == 0 ? branding.Trim() : string.Join(' ', series);
    }
}

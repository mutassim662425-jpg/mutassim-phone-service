using System.Collections.Concurrent;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Infrastructure;
using MUTASSIM.ToolPro.Infrastructure.Catalog;

namespace MUTASSIM.ToolPro.App.Services;

/// <summary>
/// Loads images supplied by the owner of the installation. Nothing third-party is shipped with the program.
///   brands\samsung.png, brands\xiaomi.png …              (brand bar / tiles)
///   devices\topaz.png, devices\SM-A515F.png, devices\redmi-note-12-4g.png …  (codename, model code or name)
/// Searched next to the executable first, then in %LOCALAPPDATA%\MUTASSIM\ToolPro.
/// </summary>
public sealed class ImageLibrary(AppPaths paths)
{
    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg"];
    private readonly ConcurrentDictionary<string, ImageSource> _cache = new(StringComparer.OrdinalIgnoreCase);

    public string BrandsFolder => Ensure(Path.Combine(paths.Root, "brands"));
    public string DevicesFolder => Ensure(Path.Combine(paths.Root, "devices"));

    public ImageSource? Brand(string route) =>
        Find("brands", [route.Replace("ws.", "", StringComparison.Ordinal)], 96);

    public ImageSource? Device(CatalogModel? model, DeviceInfo? device = null, int decodeHeight = 320) =>
        Find("devices",
        [
            model?.Codename,
            .. (model?.ModelCode?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? []),
            Slug(model?.Name),
            device?.ModelNumber, device?.Model, Slug(device?.Model), device?.Product
        ], decodeHeight);

    private ImageSource? Find(string folder, IEnumerable<string?> keys, int decodeHeight)
    {
        var index = Index(folder);
        if (index.Count == 0) return null;

        foreach (var key in keys.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k!.Trim()))
        {
            if (!index.TryGetValue(key, out var file)) continue;
            var cacheKey = $"{file}|{decodeHeight}";
            if (_cache.TryGetValue(cacheKey, out var cached)) return cached;
            var img = Load(file, decodeHeight);
            if (img is null) continue;
            _cache[cacheKey] = img;
            return img;
        }
        return null;
    }

    private readonly ConcurrentDictionary<string, (DateTime Stamp, Dictionary<string, string> Files)> _index = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// File-name index (name without extension → path) of the two folders, rebuilt when a folder changes.
    /// One directory listing replaces thousands of File.Exists calls when a page lists many models.
    /// </summary>
    private Dictionary<string, string> Index(string folder)
    {
        var dirs = new[] { Path.Combine(AppContext.BaseDirectory, folder), Path.Combine(paths.Root, folder) };
        var stamp = dirs.Where(Directory.Exists).Select(Directory.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max();
        if (_index.TryGetValue(folder, out var cached) && cached.Stamp == stamp) return cached.Files;

        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in dirs.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                if (!Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) continue;
                files.TryAdd(Path.GetFileNameWithoutExtension(file), file); // program folder wins over data folder
            }
        }
        _index[folder] = (stamp, files);
        return files;
    }

    private static ImageSource? Load(string file, int decodeHeight)
    {
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.UriSource = new Uri(file, UriKind.Absolute);
            img.DecodePixelHeight = decodeHeight;
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>"Redmi Note 12 4G" → "redmi-note-12-4g".</summary>
    public static string? Slug(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var sb = new StringBuilder();
        foreach (var c in text.Trim().ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(c) ? c : '-');
        var slug = sb.ToString();
        while (slug.Contains("--", StringComparison.Ordinal)) slug = slug.Replace("--", "-", StringComparison.Ordinal);
        return slug.Trim('-');
    }

    private static string Ensure(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }
}

/// <summary>Brand colours used by the brand bar and panels (plain colours, no brand artwork).</summary>
public static class BrandStyle
{
    private static readonly Dictionary<string, string> Colors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ws.samsung"] = "#1747C9", ["ws.xiaomi"] = "#F25C05", ["ws.huawei"] = "#C8102E", ["ws.oppo"] = "#138A5E",
        ["ws.vivo"] = "#2D55E8", ["ws.tecno"] = "#0E7FD1", ["ws.moto"] = "#3A4A9E", ["ws.other"] = "#4B5563",
        ["ws.apple"] = "#2B2E33", ["ws.qualcomm"] = "#C0262D", ["ws.mediatek"] = "#E0861A", ["ws.adb"] = "#178A4C",
        ["ws.fastboot"] = "#6B2FC9", ["ws.frp"] = "#B7791F", ["ws.mdm"] = "#0F8FA8"
    };

    private static readonly ConcurrentDictionary<string, Brush> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Brush Accent(string? route) => Cache.GetOrAdd(route ?? "", r =>
    {
        var hex = Colors.TryGetValue(r, out var h) ? h : "#4B5563";
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    });
}

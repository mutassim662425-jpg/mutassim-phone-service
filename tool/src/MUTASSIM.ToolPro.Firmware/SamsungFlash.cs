using System.Buffers.Binary;
using System.Formats.Tar;
using System.Text;
using MUTASSIM.ToolPro.Core.Platform;

namespace MUTASSIM.ToolPro.Firmware;

// ============================================================================
// PIT (Samsung partition information table)
// ============================================================================

public sealed record PitEntry(
    uint BinaryType,
    uint DeviceType,
    uint Identifier,
    uint Attributes,
    string PartitionName,
    string FlashFileName);

public sealed class PitFormatException(string message) : Exception(message);

/// <summary>Reads a Samsung PIT file: 28-byte header (magic 0x12349876, entry count) + 132-byte entries.</summary>
public static class SamsungPit
{
    public const uint Magic = 0x12349876;
    private const int HeaderSize = 28;
    private const int EntrySize = 132;

    public static IReadOnlyList<PitEntry> Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(data) != Magic)
            throw new PitFormatException("Not a Samsung PIT file.");

        var count = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        if (count == 0 || count > 512 || HeaderSize + count * EntrySize > data.Length)
            throw new PitFormatException("PIT entry count is invalid or the file is truncated.");

        var list = new List<PitEntry>((int)count);
        for (var i = 0; i < count; i++)
        {
            var e = data.Slice(HeaderSize + i * EntrySize, EntrySize);
            list.Add(new PitEntry(
                BinaryPrimitives.ReadUInt32LittleEndian(e),
                BinaryPrimitives.ReadUInt32LittleEndian(e[4..]),
                BinaryPrimitives.ReadUInt32LittleEndian(e[8..]),
                BinaryPrimitives.ReadUInt32LittleEndian(e[12..]),
                CString(e.Slice(36, 32)),
                CString(e.Slice(68, 32))));
        }
        return list;
    }

    private static string CString(ReadOnlySpan<byte> b)
    {
        var end = b.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? b : b[..end]).Trim();
    }
}

// ============================================================================
// Flash plan: which image goes to which partition, with the safety rules
// ============================================================================

public sealed record FlashImage(
    string PartitionName,
    string EntryName,
    string SourcePackage,
    SamsungPart Part,
    long PackedSize,
    bool Lz4);

public sealed record SkippedImage(string EntryName, string SourcePackage, string Reason);

public sealed record FlashPlan(
    IReadOnlyList<FlashImage> Images,
    IReadOnlyList<SkippedImage> Skipped,
    IReadOnlyList<string> Errors,
    bool WipesData,
    string? FirmwareBuild)
{
    public bool CanFlash => Errors.Count == 0 && Images.Count > 0;
}

public sealed record FirmwarePackage(SamsungFirmwareFile File, IReadOnlyList<TarEntry> Entries);

/// <summary>
/// Builds the list of partitions to write. Rules (all enforced in code, not only shown to the user):
///  - BL and AP are required, at most one of CSC / HOME_CSC, all parts from the same build.
///  - Only files whose name matches a PIT entry are flashed; nothing else.
///  - The PIT is NEVER re-written (no repartition).
///  - Partitions that hold device identity or anti-theft data are never written
///    (EFS / IMEI, FRP "persistent", keys…), even if a modified package contains them.
/// </summary>
public static class SamsungFlashPlanner
{
    private static readonly string[] ProtectedExact =
        ["PERSISTENT", "FRP", "STEADY", "KEYDATA", "KEYREFUGE", "PERSIST", "SEC_EFS", "CPEFS", "EFS", "PARAM_EFS", "PIT", "BOTA0", "BOTA1"];

    public static bool IsProtected(string partition)
    {
        var p = partition.ToUpperInvariant();
        return ProtectedExact.Contains(p) || p.Contains("EFS") || p.Contains("IMEI");
    }

    public static FlashPlan Build(IReadOnlyList<FirmwarePackage> packages, IReadOnlyList<PitEntry> pit)
    {
        var errors = new List<string>();
        var images = new List<FlashImage>();
        var skipped = new List<SkippedImage>();

        var byPart = packages.GroupBy(p => p.File.Part).ToDictionary(g => g.Key, g => g.ToList());
        if (byPart.ContainsKey(SamsungPart.Unknown))
            errors.Add("Unrecognised file name (expected BL_, AP_, CP_, CSC_ or HOME_CSC_ …tar.md5).");
        foreach (var (part, list) in byPart)
            if (part != SamsungPart.Unknown && list.Count > 1) errors.Add($"More than one {part} file selected.");
        if (!byPart.ContainsKey(SamsungPart.BL)) errors.Add("BL file is required.");
        if (!byPart.ContainsKey(SamsungPart.AP)) errors.Add("AP file is required.");
        if (byPart.ContainsKey(SamsungPart.CSC) && byPart.ContainsKey(SamsungPart.HOME_CSC))
            errors.Add("Choose CSC (erases data) OR HOME_CSC (keeps data), not both.");
        var paths = packages.Select(p => Path.GetFullPath(p.File.Path)).ToList();
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Count)
            errors.Add("The same file is selected twice.");

        var models = packages.Select(p => p.File.Build?.ModelCode).Where(m => m is not null).Distinct().ToList();
        if (models.Count > 1) errors.Add("The files belong to different models: " + string.Join(", ", models));
        if (packages.Any(p => p.File.Build is null)) errors.Add("Build number could not be read from a file name.");

        var byFile = pit
            .Where(e => e.FlashFileName.Length > 0)
            .GroupBy(e => e.FlashFileName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pkg in packages.Where(p => p.File.Part != SamsungPart.Unknown))
        {
            foreach (var entry in pkg.Entries)
            {
                var name = Path.GetFileName(entry.Name);
                if (name.Length == 0 || entry.Name.Contains("meta-data/", StringComparison.OrdinalIgnoreCase))
                {
                    skipped.Add(new SkippedImage(entry.Name, pkg.File.Path, "Metadata (not a partition image)"));
                    continue;
                }
                if (name.EndsWith(".pit", StringComparison.OrdinalIgnoreCase))
                {
                    skipped.Add(new SkippedImage(entry.Name, pkg.File.Path, "Partition table: never re-written"));
                    continue;
                }

                var lz4 = name.EndsWith(".lz4", StringComparison.OrdinalIgnoreCase);
                var imageName = lz4 ? name[..^4] : name;

                if (!byFile.TryGetValue(imageName, out var target))
                {
                    skipped.Add(new SkippedImage(entry.Name, pkg.File.Path, "No matching partition in the PIT"));
                    continue;
                }
                if (IsProtected(target.PartitionName))
                {
                    errors.Add($"Package contains {imageName} for protected partition {target.PartitionName} (device identity / anti-theft). Refused: this is not a clean official package.");
                    continue;
                }
                if (!used.Add(target.PartitionName))
                {
                    errors.Add($"Two files target partition {target.PartitionName}.");
                    continue;
                }

                images.Add(new FlashImage(target.PartitionName, entry.Name, pkg.File.Path, pkg.File.Part, entry.Size, lz4));
            }
        }

        if (images.Count == 0 && errors.Count == 0) errors.Add("No image in the selected files matches the partition table.");

        // Data is erased by CSC, by a USERDATA package, or by any package (e.g. a "full/factory" AP) that writes USERDATA.
        var wipes = byPart.ContainsKey(SamsungPart.CSC) || byPart.ContainsKey(SamsungPart.USERDATA) ||
                    images.Any(i => i.PartitionName.Equals("USERDATA", StringComparison.OrdinalIgnoreCase));

        return new FlashPlan(images, skipped, errors,
            WipesData: wipes,
            FirmwareBuild: packages.FirstOrDefault(p => p.File.Part == SamsungPart.AP)?.File.Build?.Build);
    }

    public static IReadOnlyList<PitEntry> ReadPitFile(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("PIT file not found.", path);
        if (info.Length > 1 << 20) throw new PitFormatException("This is not a PIT file (too large).");
        return SamsungPit.Parse(File.ReadAllBytes(path));
    }

    /// <summary>Finds the PIT shipped inside the CSC / HOME_CSC package (used for planning and the dry run).</summary>
    public static async Task<IReadOnlyList<PitEntry>?> ReadPackagePitAsync(IReadOnlyList<FirmwarePackage> packages, CancellationToken ct)
    {
        foreach (var pkg in packages.Where(p => p.File.Part is SamsungPart.CSC or SamsungPart.HOME_CSC))
        {
            var pitEntry = pkg.Entries.FirstOrDefault(e => e.Name.EndsWith(".pit", StringComparison.OrdinalIgnoreCase));
            if (pitEntry is null) continue;

            using var ms = new MemoryStream();
            await FirmwareExtractor.CopyEntryAsync(pkg.File.Path, pitEntry.Name, ms, ct);
            return SamsungPit.Parse(ms.ToArray());
        }
        return null;
    }

    /// <summary>Every planned partition must exist, with the same file name, in the PIT read from the phone.</summary>
    public static IReadOnlyList<string> CompareWithDevicePit(FlashPlan plan, IReadOnlyList<PitEntry> devicePit)
    {
        var problems = new List<string>();
        var device = devicePit.GroupBy(e => e.PartitionName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var img in plan.Images)
        {
            if (!device.TryGetValue(img.PartitionName, out var d))
                problems.Add($"Partition {img.PartitionName} does not exist on this phone.");
            else
            {
                var file = Path.GetFileName(img.EntryName);
                if (file.EndsWith(".lz4", StringComparison.OrdinalIgnoreCase)) file = file[..^4];
                if (!string.Equals(d.FlashFileName, file, StringComparison.OrdinalIgnoreCase))
                    problems.Add($"Phone expects {d.FlashFileName} on {img.PartitionName}, firmware has {file}.");
            }
        }
        return problems;
    }
}

// ============================================================================
// Extraction (tar → raw image, lz4 decoded on the fly)
// ============================================================================

public static class FirmwareExtractor
{
    public static async Task CopyEntryAsync(string tarPath, string entryName, Stream destination, CancellationToken ct)
    {
        await using var fs = new FileStream(tarPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        await using var reader = new TarReader(fs, leaveOpen: false);
        while (await reader.GetNextEntryAsync(copyData: false, ct) is { } entry)
        {
            if (entry.Name != entryName) continue;
            if (entry.DataStream is null) throw new InvalidDataException($"{entryName} has no data.");
            await entry.DataStream.CopyToAsync(destination, ct);
            return;
        }
        throw new FileNotFoundException($"{entryName} not found in {Path.GetFileName(tarPath)}.");
    }

    /// <summary>Extracts the planned images of one package into <paramref name="folder"/>. Returns the files written.</summary>
    public static async Task<IReadOnlyDictionary<string, string>> ExtractAsync(
        string tarPath, IReadOnlyCollection<FlashImage> images, string folder,
        Action<string>? log, CancellationToken ct)
    {
        Directory.CreateDirectory(folder);
        var wanted = images.ToDictionary(i => i.EntryName);
        var written = new Dictionary<string, string>();

        await using var fs = new FileStream(tarPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        await using var reader = new TarReader(fs, leaveOpen: false);
        while (written.Count < wanted.Count && await reader.GetNextEntryAsync(copyData: false, ct) is { } entry)
        {
            if (!wanted.TryGetValue(entry.Name, out var img) || entry.DataStream is null) continue;

            var outName = Path.GetFileName(img.Lz4 ? img.EntryName[..^4] : img.EntryName);
            var outPath = Path.Combine(folder, outName);
            log?.Invoke($"Extract {outName}{(img.Lz4 ? " (LZ4)" : "")}");

            if (img.Lz4)
            {
                await using var output = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
                var source = entry.DataStream;
                await Task.Run(() => Lz4Frame.Decode(source, output, ct), ct);
            }
            else
            {
                await using var output = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
                await entry.DataStream.CopyToAsync(output, ct);
            }
            written[img.PartitionName] = outPath;
        }

        var missing = images.Where(i => !written.ContainsKey(i.PartitionName)).Select(i => i.EntryName).ToList();
        if (missing.Count > 0) throw new InvalidDataException("Not found in package: " + string.Join(", ", missing));
        return written;
    }
}

// ============================================================================
// Heimdall (open-source Samsung Download-mode protocol, MIT licence)
// ============================================================================

public sealed record HeimdallResult(bool Success, string Output);

/// <summary>
/// Thin wrapper over heimdall.exe. Arguments are passed as a list (no shell). The flash command is built only
/// from a validated <see cref="FlashPlan"/>: one --PARTITION file pair per planned image, never --repartition.
/// </summary>
public sealed class HeimdallClient(IProcessRunner runner, string heimdallPath)
{
    public string Path { get; } = heimdallPath;

    public async Task<HeimdallResult> DetectAsync(CancellationToken ct)
    {
        var r = await runner.RunAsync(Path, ["detect"], TimeSpan.FromSeconds(20), ct);
        return new HeimdallResult(r.Success, r.Combined.Trim());
    }

    /// <summary>Reads the phone's PIT without rebooting (the session stays open for the flash).</summary>
    public async Task<HeimdallResult> DownloadPitAsync(string outputFile, CancellationToken ct)
    {
        var r = await runner.RunAsync(Path, ["download-pit", "--output", outputFile, "--no-reboot"], TimeSpan.FromMinutes(2), ct);
        return new HeimdallResult(r.Success && File.Exists(outputFile), r.Combined.Trim());
    }

    public static IReadOnlyList<string> BuildFlashArguments(IReadOnlyDictionary<string, string> partitionFiles, bool resume)
    {
        var args = new List<string> { "flash" };
        if (resume) args.Add("--resume");
        foreach (var (partition, file) in partitionFiles.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (SamsungFlashPlanner.IsProtected(partition))
                throw new InvalidOperationException($"Refusing to flash protected partition {partition}.");
            if (partition.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-')))
                throw new InvalidOperationException($"Unexpected partition name {partition}.");
            args.Add("--" + partition);
            args.Add(file);
        }
        return args;
    }

    /// <summary>
    /// Writes the images. Deliberately NOT cancellable: stopping in the middle of a write can leave the phone unbootable.
    /// </summary>
    public async Task<HeimdallResult> FlashAsync(IReadOnlyDictionary<string, string> partitionFiles, bool resume, Action<string> onLine)
    {
        var r = await runner.RunAsync(Path, BuildFlashArguments(partitionFiles, resume), TimeSpan.FromHours(2),
            CancellationToken.None, onLine);
        return new HeimdallResult(r.Success, r.Combined.Trim());
    }
}

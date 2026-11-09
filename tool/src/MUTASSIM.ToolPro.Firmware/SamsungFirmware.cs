using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MUTASSIM.ToolPro.Firmware;

public enum SamsungPart
{
    Unknown,
    BL,
    AP,
    CP,
    CSC,
    HOME_CSC,
    USERDATA
}

/// <summary>
/// A Samsung build id such as "A515FXXU5EVK1": model code "A515F", region "XX", bootloader binary (rev) at
/// the 5th character from the end ('5' here; letters A..Z mean 10..35).
/// </summary>
public sealed record SamsungBuild(string Build, string ModelCode, int? Binary)
{
    private static readonly Regex BuildPattern = new(@"^(?<model>[A-Z]\d{3}[A-Z0-9]{0,3}?)(?<region>[A-Z]{2,3})[A-Z0-9]{5}$", RegexOptions.Compiled);

    public static SamsungBuild? Parse(string? build)
    {
        if (string.IsNullOrWhiteSpace(build)) return null;
        build = build.Trim().ToUpperInvariant();
        if (build.Length < 10 || build.Any(c => !char.IsLetterOrDigit(c))) return null;

        var m = BuildPattern.Match(build);
        var model = m.Success ? m.Groups["model"].Value : build[..^8];
        return new SamsungBuild(build, model, BinaryOf(build));
    }

    public static int? BinaryOf(string build)
    {
        if (build.Length < 5) return null;
        var c = char.ToUpperInvariant(build[^5]);
        return c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'A' and <= 'Z' => c - 'A' + 10,
            _ => null
        };
    }

    /// <summary>"SM-A515F" (ro.product.model) matches build "A515FXXU5EVK1".</summary>
    public bool MatchesModel(string? deviceModel)
    {
        if (string.IsNullOrWhiteSpace(deviceModel)) return false;
        var code = deviceModel.Trim().ToUpperInvariant();
        if (code.StartsWith("SM-", StringComparison.Ordinal)) code = code[3..];
        if (code.Length < 4) return false;
        return Build.StartsWith(code, StringComparison.Ordinal);
    }
}

public sealed record SamsungFirmwareFile(string Path, SamsungPart Part, SamsungBuild? Build)
{
    // CSC files carry one or more 3-letter codes first: "CSC_OXM_A515FOXM8HWE1_…", "HOME_CSC_OMC_OXM_A515FOXM5EVK1_…".
    private static readonly Regex NamePattern = new(@"^(?<part>HOME_CSC|USERDATA|BL|AP|CP|CSC)_(?:[A-Za-z0-9]{3}_)*(?<build>[A-Za-z0-9]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static SamsungFirmwareFile FromPath(string path)
    {
        // Take the segment after the last separator, handling BOTH '/' and '\\' regardless of the host OS.
        // System.IO.Path.GetFileName only treats '\\' as a separator on Windows, so a Windows-style path
        // (e.g. "C:\fw\AP_...") would otherwise fail to parse when the tool or its tests run on Linux.
        var sep = path.LastIndexOfAny(['/', '\\']);
        var name = sep >= 0 ? path[(sep + 1)..] : path;
        var m = NamePattern.Match(name);
        if (!m.Success) return new SamsungFirmwareFile(path, SamsungPart.Unknown, null);

        var part = Enum.TryParse<SamsungPart>(m.Groups["part"].Value.ToUpperInvariant(), out var p) ? p : SamsungPart.Unknown;
        return new SamsungFirmwareFile(path, part, SamsungBuild.Parse(m.Groups["build"].Value));
    }
}

public sealed record TarEntry(string Name, long Size);

public sealed record TarMd5Result(
    bool HasEmbeddedMd5,
    string? ExpectedMd5,
    string? ActualMd5,
    IReadOnlyList<TarEntry> Entries,
    long TarLength)
{
    public bool Md5Matches => HasEmbeddedMd5 && string.Equals(ExpectedMd5, ActualMd5, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Verifies Samsung .tar.md5 packages: the file is a tar archive followed by a text line "md5  name".
/// The MD5 is computed over the tar bytes only and compared with the embedded value.
/// </summary>
public static class TarMd5
{
    private const int Block = 512;
    private static readonly Regex Md5Line = new(@"(?<md5>[0-9a-fA-F]{32})\s+[^\r\n]+\r?\n?$", RegexOptions.Compiled);

    public static async Task<TarMd5Result> AnalyzeAsync(string path, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var length = fs.Length;

        // 1) Locate the embedded md5 line in the last bytes of the file.
        var tailSize = (int)Math.Min(1024, length);
        var tail = new byte[tailSize];
        fs.Seek(length - tailSize, SeekOrigin.Begin);
        await fs.ReadExactlyAsync(tail, ct);
        var tailText = Encoding.ASCII.GetString(tail);
        var m = Md5Line.Match(tailText);

        long tarLength = length;
        string? expected = null;
        if (m.Success)
        {
            expected = m.Groups["md5"].Value.ToLowerInvariant();
            var lineBytes = Encoding.ASCII.GetByteCount(tailText[m.Index..]);
            tarLength = length - lineBytes;
        }

        // 2) Read tar headers.
        var entries = await ReadEntriesAsync(fs, tarLength, ct);

        // 3) MD5 over the tar part (only when an embedded value exists).
        string? actual = null;
        if (expected is not null)
        {
            fs.Seek(0, SeekOrigin.Begin);
            using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
            var buffer = new byte[4 << 20];
            long done = 0;
            var lastPct = -1;
            while (done < tarLength)
            {
                var want = (int)Math.Min(buffer.Length, tarLength - done);
                var read = await fs.ReadAsync(buffer.AsMemory(0, want), ct);
                if (read == 0) break;
                md5.AppendData(buffer, 0, read);
                done += read;
                var pct = (int)(done * 100 / Math.Max(1, tarLength));
                if (pct != lastPct)
                {
                    lastPct = pct;
                    progress?.Report(pct);
                }
            }
            actual = Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant();
        }

        return new TarMd5Result(expected is not null, expected, actual, entries, tarLength);
    }

    public static async Task<IReadOnlyList<TarEntry>> ReadEntriesAsync(Stream fs, long tarLength, CancellationToken ct)
    {
        var list = new List<TarEntry>();
        var header = new byte[Block];
        long pos = 0;
        while (pos + Block <= tarLength)
        {
            fs.Seek(pos, SeekOrigin.Begin);
            await fs.ReadExactlyAsync(header, ct);
            if (header.All(b => b == 0)) break; // end-of-archive marker

            var name = Encoding.ASCII.GetString(header, 0, 100).TrimEnd('\0', ' ');
            var size = ParseOctal(header, 124, 12);
            if (size < 0) break; // not a valid tar header
            var type = (char)header[156];
            if (type is '0' or '\0') list.Add(new TarEntry(name, size));

            pos += Block + (size + Block - 1) / Block * Block;
        }
        return list;
    }

    private static long ParseOctal(byte[] buffer, int offset, int count)
    {
        var text = Encoding.ASCII.GetString(buffer, offset, count).Trim('\0', ' ');
        if (text.Length == 0) return 0;
        long value = 0;
        foreach (var c in text)
        {
            if (c is < '0' or > '7') return -1;
            value = value * 8 + (c - '0');
        }
        return value;
    }
}

public enum CompatibilityVerdict
{
    Compatible,
    WrongModel,
    Downgrade,
    Unknown
}

public sealed record CompatibilityResult(CompatibilityVerdict Verdict, string Message);

public static class SamsungCompatibility
{
    /// <summary>
    /// Compares a firmware build with the connected device (ro.product.model + ro.bootloader).
    /// A lower bootloader binary than the device's is refused (the bootloader blocks downgrades).
    /// </summary>
    public static CompatibilityResult Check(SamsungBuild? firmware, string? deviceModel, string? deviceBootloader)
    {
        if (firmware is null) return new(CompatibilityVerdict.Unknown, "Firmware build not recognised from the file name.");
        if (string.IsNullOrWhiteSpace(deviceModel)) return new(CompatibilityVerdict.Unknown, "No device model to compare with.");
        if (!firmware.MatchesModel(deviceModel))
            return new(CompatibilityVerdict.WrongModel, $"Firmware {firmware.Build} is NOT for {deviceModel}.");

        var deviceBinary = SamsungBuild.Parse(deviceBootloader)?.Binary;
        if (firmware.Binary is { } fw && deviceBinary is { } dev && fw < dev)
            return new(CompatibilityVerdict.Downgrade, $"Binary {fw} is lower than the device binary {dev}: downgrade is blocked by the bootloader.");

        return new(CompatibilityVerdict.Compatible,
            deviceBinary is null
                ? $"Model matches {deviceModel} (device binary unknown)."
                : $"Model matches {deviceModel}; binary {firmware.Binary} ≥ device binary {deviceBinary}.");
    }
}

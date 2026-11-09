using System.Security.Cryptography;

namespace MUTASSIM.ToolPro.Firmware;

public sealed record HashResult(string FilePath, long SizeBytes, string Sha256, TimeSpan Elapsed);

/// <summary>Streaming SHA-256 for multi-GB firmware files, with progress and cancellation.</summary>
public static class FileHasher
{
    private const int BufferSize = 4 * 1024 * 1024;

    public static async Task<HashResult> ComputeSha256Async(
        string filePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var started = System.Diagnostics.Stopwatch.StartNew();

        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await ComputeSha256Async(stream, progress, cancellationToken).ConfigureAwait(false);
        return new HashResult(filePath, stream.Length, hash, started.Elapsed);
    }

    public static async Task<string> ComputeSha256Async(
        Stream stream,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize];
        var total = stream.CanSeek ? stream.Length : -1;
        long done = 0;
        var lastReported = -1;

        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            hasher.AppendData(buffer, 0, read);
            done += read;
            if (progress is not null && total > 0)
            {
                var pct = (int)(done * 100 / total);
                if (pct != lastReported)
                {
                    lastReported = pct;
                    progress.Report(pct);
                }
            }
        }

        progress?.Report(100);
        return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>Case/whitespace-insensitive comparison of two hex digests.</summary>
    public static bool DigestsMatch(string? expected, string actual)
    {
        if (string.IsNullOrWhiteSpace(expected)) return false;
        var cleaned = new string(expected.Where(Uri.IsHexDigit).ToArray());
        return cleaned.Length == 64 && cleaned.Equals(actual, StringComparison.OrdinalIgnoreCase);
    }
}

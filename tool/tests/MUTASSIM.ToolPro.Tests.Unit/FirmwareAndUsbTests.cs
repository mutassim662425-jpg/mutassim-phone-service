using System.Text;
using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Firmware;
using MUTASSIM.ToolPro.Updates;
using MUTASSIM.ToolPro.Usb;

namespace MUTASSIM.ToolPro.Tests.Unit;

public class FileHasherTests
{
    [Fact]
    public async Task Sha256_of_abc_matches_NIST_vector()
    {
        await using var stream = new MemoryStream(Encoding.ASCII.GetBytes("abc"));
        var hash = await FileHasher.ComputeSha256Async(stream);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
    }

    [Fact]
    public async Task Sha256_of_empty_input()
    {
        await using var stream = new MemoryStream();
        var hash = await FileHasher.ComputeSha256Async(stream);
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", hash);
    }

    [Fact]
    public async Task File_hash_reports_size_and_final_progress()
    {
        var path = Path.GetTempFileName();
        try
        {
            var data = new byte[5 * 1024 * 1024 + 123]; // spans more than one buffer
            new Random(42).NextBytes(data);
            await File.WriteAllBytesAsync(path, data);

            var reports = new List<double>();
            var result = await FileHasher.ComputeSha256Async(path, new SyncProgress(reports.Add));

            Assert.Equal(data.Length, result.SizeBytes);
            Assert.Equal(64, result.Sha256.Length);
            Assert.Equal(100, reports[^1]);
            Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant(), result.Sha256);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Cancellation_is_honoured()
    {
        await using var stream = new MemoryStream(new byte[10 * 1024 * 1024]);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FileHasher.ComputeSha256Async(stream, null, cts.Token));
    }

    [Theory]
    [InlineData("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", true)]
    [InlineData(" ba7816bf 8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad ", true)]
    [InlineData("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ae", false)]
    [InlineData("ba7816", false)]
    [InlineData("", false)]
    public void Digest_comparison(string expected, bool match) =>
        Assert.Equal(match, FileHasher.DigestsMatch(expected, "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"));

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}

public class UsbIdClassifierTests
{
    [Theory]
    [InlineData(0x04E8, 0x685D, ConnectionMode.SamsungDownload)]
    [InlineData(0x05C6, 0x9008, ConnectionMode.QualcommEdl)]
    [InlineData(0x0E8D, 0x0003, ConnectionMode.MediaTekBrom)]
    [InlineData(0x0E8D, 0x2000, ConnectionMode.MediaTekPreloader)]
    [InlineData(0x05AC, 0x1227, ConnectionMode.AppleDfu)]
    [InlineData(0x05AC, 0x1281, ConnectionMode.AppleRecovery)]
    [InlineData(0x05AC, 0x12A8, ConnectionMode.AppleNormal)]
    [InlineData(0x18D1, 0xD00D, ConnectionMode.Fastboot)]
    public void Known_ids_map_to_modes(int vid, int pid, ConnectionMode expected)
    {
        var result = UsbIdClassifier.Classify((ushort)vid, (ushort)pid);
        Assert.NotNull(result);
        Assert.Equal(expected, result.Mode);
    }

    [Fact]
    public void Known_vendor_with_unknown_pid_does_not_guess_a_mode()
    {
        var result = UsbIdClassifier.Classify(0x04E8, 0x6860);
        Assert.NotNull(result);
        Assert.Equal("Samsung", result.VendorName);
        Assert.Equal(ConnectionMode.Unknown, result.Mode);
    }

    [Fact]
    public void Unknown_vendor_returns_null() => Assert.Null(UsbIdClassifier.Classify(0x1234, 0x5678));

    [Fact]
    public void Parses_windows_instance_id()
    {
        Assert.True(UsbIdClassifier.TryParseInstanceId(@"USB\VID_05C6&PID_9008\5&2A1B3C4D&0&2", out var vid, out var pid));
        Assert.Equal(0x05C6, vid);
        Assert.Equal(0x9008, pid);
        Assert.False(UsbIdClassifier.TryParseInstanceId("ROOT\\SYSTEM\\0001", out _, out _));
        Assert.False(UsbIdClassifier.TryParseInstanceId(null, out _, out _));
    }
}

public class UpdateVersionTests
{
    [Theory]
    [InlineData("1.0", "1.0.0.0", 0)]
    [InlineData("1.1", "1.0.9", 1)]
    [InlineData("1.0.0", "1.0.1", -1)]
    [InlineData("2.0", "1.99.99.99", 1)]
    public void Compare_normalizes_missing_parts(string a, string b, int sign) =>
        Assert.Equal(sign, Math.Sign(UpdateService.Compare(Version.Parse(a), Version.Parse(b))));
}

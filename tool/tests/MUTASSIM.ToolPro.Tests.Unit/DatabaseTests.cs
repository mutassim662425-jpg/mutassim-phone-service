using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Core.Operations;
using MUTASSIM.ToolPro.Core.Security;
using MUTASSIM.ToolPro.Database;
using MUTASSIM.ToolPro.Infrastructure;

namespace MUTASSIM.ToolPro.Tests.Unit;

public sealed class TempDatabase : IDisposable
{
    public TempDatabase()
    {
        Directory = Path.Combine(Path.GetTempPath(), "mtp-tests-" + Guid.NewGuid().ToString("N"));
        Db = new SqliteDatabase(Path.Combine(Directory, "test.db"));
        Db.InitializeAsync().GetAwaiter().GetResult();
    }

    public string Directory { get; }
    public SqliteDatabase Db { get; }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { System.IO.Directory.Delete(Directory, true); } catch (IOException) { }
    }
}

public class DatabaseTests : IDisposable
{
    private readonly TempDatabase _temp = new();

    [Fact]
    public async Task Initialize_is_idempotent_and_reports_latest_schema()
    {
        var v1 = await _temp.Db.InitializeAsync();
        var v2 = await _temp.Db.InitializeAsync();
        Assert.Equal(SqliteDatabase.LatestSchemaVersion, v1);
        Assert.Equal(v1, v2);
    }

    [Fact]
    public async Task Insert_update_query_delete_roundtrip()
    {
        var repo = new SqliteOperationRepository(_temp.Db);
        var record = new OperationRecord
        {
            ServiceId = "fw.sha256",
            ServiceName = "Firmware SHA-256: AP_test.tar.md5",
            Manufacturer = "samsung",
            Model = "SM-A515F",
            Mode = ConnectionMode.SamsungDownload
        };

        var id = await repo.InsertAsync(record);
        Assert.True(id > 0);

        record.Status = OperationStatus.Succeeded;
        record.FinishedAt = record.StartedAt.AddSeconds(3);
        record.ResultMessage = "ok";
        await repo.UpdateAsync(record);

        var all = await repo.QueryAsync(new OperationQuery());
        var row = Assert.Single(all);
        Assert.Equal(OperationStatus.Succeeded, row.Status);
        Assert.Equal(ConnectionMode.SamsungDownload, row.Mode);
        Assert.Equal(3.0, row.Duration!.Value.TotalSeconds, 2);

        Assert.Single(await repo.QueryAsync(new OperationQuery(Text: "SM-A515")));
        Assert.Empty(await repo.QueryAsync(new OperationQuery(Text: "nothing-like-this")));
        Assert.Empty(await repo.QueryAsync(new OperationQuery(Status: OperationStatus.Failed)));
        Assert.Empty(await repo.QueryAsync(new OperationQuery(Text: "%")));   // LIKE wildcards are escaped

        Assert.Equal(1, await repo.DeleteAsync(id));
        Assert.Empty(await repo.QueryAsync(new OperationQuery()));
    }

    [Fact]
    public async Task Tracker_never_records_success_without_explicit_result()
    {
        var repo = new SqliteOperationRepository(_temp.Db);
        var tracker = new OperationTracker(repo, NullLogger<OperationTracker>.Instance);

        await using (await tracker.BeginAsync("x", "Abandoned operation", null))
        {
            // no SucceedAsync / FailAsync call
        }

        var row = Assert.Single(await repo.QueryAsync(new OperationQuery()));
        Assert.Equal(OperationStatus.Failed, row.Status);
        Assert.NotNull(row.FinishedAt);
    }

    [Fact]
    public async Task Tracker_result_is_final()
    {
        var repo = new SqliteOperationRepository(_temp.Db);
        var tracker = new OperationTracker(repo, NullLogger<OperationTracker>.Instance);

        await using (var op = await tracker.BeginAsync("x", "Op", null))
        {
            await op.FailAsync("real failure");
            await op.SucceedAsync("must be ignored");
        }

        var row = Assert.Single(await repo.QueryAsync(new OperationQuery()));
        Assert.Equal(OperationStatus.Failed, row.Status);
        Assert.Equal("real failure", row.ResultMessage);
    }

    [Fact]
    public async Task Device_history_stores_hash_not_serial()
    {
        var repo = new SqliteDeviceHistoryRepository(_temp.Db);
        var device = new DeviceInfo { Key = "adb:R58N12ABCDE", Mode = ConnectionMode.Adb, Source = "test", Manufacturer = "samsung", Model = "SM-A515F" };

        await repo.UpsertAsync(device);
        await repo.UpsertAsync(device with { Mode = ConnectionMode.Recovery });

        var entry = Assert.Single(await repo.RecentAsync(10));
        Assert.Equal(DeviceKeyHasher.Hash(device.Key), entry.KeyHash);
        Assert.DoesNotContain("R58N12ABCDE", entry.KeyHash);
        Assert.Equal(ConnectionMode.Recovery, entry.Mode);
    }

    [Theory]
    [InlineData("=cmd|' /C calc'!A0", "'=cmd|' /C calc'!A0")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("plain", "plain")]
    public void Csv_escaping_blocks_formula_injection(string input, string expected) =>
        Assert.Equal(expected, OperationCsvExporter.Escape(input));

    public void Dispose() => _temp.Dispose();
}

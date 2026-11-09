using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Core.Operations;
using MUTASSIM.ToolPro.Core.Security;

namespace MUTASSIM.ToolPro.Database;

/// <summary>Stores which models were connected. The device key (may contain a serial) is stored only as a hash.</summary>
public sealed class SqliteDeviceHistoryRepository(SqliteDatabase db) : IDeviceHistoryRepository
{
    public async Task UpsertAsync(DeviceInfo device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        var now = SqliteOperationRepository.Iso(DateTimeOffset.Now);

        await using var c = await db.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO devices_seen (device_key, manufacturer, model, mode, first_seen, last_seen)
            VALUES ($key, $man, $model, $mode, $now, $now)
            ON CONFLICT(device_key) DO UPDATE SET
                manufacturer = COALESCE(excluded.manufacturer, devices_seen.manufacturer),
                model        = COALESCE(excluded.model, devices_seen.model),
                mode         = excluded.mode,
                last_seen    = excluded.last_seen;
            """;
        cmd.Parameters.AddWithValue("$key", DeviceKeyHasher.Hash(device.Key));
        cmd.Parameters.AddWithValue("$man", (object?)device.Manufacturer ?? device.Brand ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$model", (object?)device.Model ?? device.Product ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$mode", (int)device.Mode);
        cmd.Parameters.AddWithValue("$now", now);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DeviceHistoryEntry>> RecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        await using var c = await db.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT device_key, manufacturer, model, mode, last_seen
            FROM devices_seen ORDER BY last_seen DESC LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));

        var list = new List<DeviceHistoryEntry>();
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await r.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new DeviceHistoryEntry(
                r.GetString(0),
                r.IsDBNull(1) ? null : r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                (ConnectionMode)r.GetInt32(3),
                SqliteOperationRepository.ParseIso(r.GetString(4))));
        }
        return list;
    }
}

using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Core.Operations;

namespace MUTASSIM.ToolPro.Database;

public sealed class SqliteOperationRepository(SqliteDatabase db) : IOperationRepository
{
    public async Task<long> InsertAsync(OperationRecord r, CancellationToken cancellationToken = default)
    {
        await using var c = await db.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO operations (started_at, finished_at, service_id, service_name, device_key,
                                    manufacturer, model, mode, status, result_message, error_details)
            VALUES ($started, $finished, $sid, $sname, $key, $man, $model, $mode, $status, $msg, $err);
            SELECT last_insert_rowid();
            """;
        Bind(cmd, r);
        var id = Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        r.Id = id;
        return id;
    }

    public async Task UpdateAsync(OperationRecord r, CancellationToken cancellationToken = default)
    {
        if (r.Id <= 0) throw new ArgumentException("Record has not been inserted.", nameof(r));
        await using var c = await db.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            UPDATE operations SET started_at=$started, finished_at=$finished, service_id=$sid, service_name=$sname,
                   device_key=$key, manufacturer=$man, model=$model, mode=$mode, status=$status,
                   result_message=$msg, error_details=$err
            WHERE id=$id;
            """;
        Bind(cmd, r);
        cmd.Parameters.AddWithValue("$id", r.Id);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OperationRecord>> QueryAsync(OperationQuery q, CancellationToken cancellationToken = default)
    {
        var sql = new StringBuilder("SELECT * FROM operations WHERE 1=1");
        await using var c = await db.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = c.CreateCommand();

        if (!string.IsNullOrWhiteSpace(q.Text))
        {
            sql.Append(" AND (service_name LIKE $t ESCAPE '\\' OR model LIKE $t ESCAPE '\\' OR manufacturer LIKE $t ESCAPE '\\' OR result_message LIKE $t ESCAPE '\\')");
            cmd.Parameters.AddWithValue("$t", "%" + EscapeLike(q.Text.Trim()) + "%");
        }
        if (q.Status is { } status)
        {
            sql.Append(" AND status = $s");
            cmd.Parameters.AddWithValue("$s", (int)status);
        }
        if (q.From is { } from)
        {
            sql.Append(" AND started_at >= $from");
            cmd.Parameters.AddWithValue("$from", Iso(from));
        }
        if (q.To is { } to)
        {
            sql.Append(" AND started_at <= $to");
            cmd.Parameters.AddWithValue("$to", Iso(to));
        }

        sql.Append(" ORDER BY started_at DESC, id DESC LIMIT $limit;");
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(q.Limit, 1, 100_000));
        cmd.CommandText = sql.ToString();

        var list = new List<OperationRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new OperationRecord
            {
                Id = reader.GetInt64(reader.GetOrdinal("id")),
                StartedAt = ParseIso(reader.GetString(reader.GetOrdinal("started_at"))),
                FinishedAt = ReadNullable(reader, "finished_at") is { } f ? ParseIso(f) : null,
                ServiceId = reader.GetString(reader.GetOrdinal("service_id")),
                ServiceName = reader.GetString(reader.GetOrdinal("service_name")),
                DeviceKeyHash = ReadNullable(reader, "device_key"),
                Manufacturer = ReadNullable(reader, "manufacturer"),
                Model = ReadNullable(reader, "model"),
                Mode = (ConnectionMode)reader.GetInt32(reader.GetOrdinal("mode")),
                Status = (OperationStatus)reader.GetInt32(reader.GetOrdinal("status")),
                ResultMessage = ReadNullable(reader, "result_message"),
                ErrorDetails = ReadNullable(reader, "error_details")
            });
        }
        return list;
    }

    public async Task<int> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var c = await db.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM operations WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        await using var c = await db.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM operations;";
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void Bind(SqliteCommand cmd, OperationRecord r)
    {
        cmd.Parameters.AddWithValue("$started", Iso(r.StartedAt));
        cmd.Parameters.AddWithValue("$finished", r.FinishedAt is { } f ? Iso(f) : DBNull.Value);
        cmd.Parameters.AddWithValue("$sid", r.ServiceId);
        cmd.Parameters.AddWithValue("$sname", r.ServiceName);
        cmd.Parameters.AddWithValue("$key", (object?)r.DeviceKeyHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$man", (object?)r.Manufacturer ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$model", (object?)r.Model ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$mode", (int)r.Mode);
        cmd.Parameters.AddWithValue("$status", (int)r.Status);
        cmd.Parameters.AddWithValue("$msg", (object?)r.ResultMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$err", (object?)r.ErrorDetails ?? DBNull.Value);
    }

    internal static string Iso(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    internal static DateTimeOffset ParseIso(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToLocalTime();

    private static string? ReadNullable(SqliteDataReader r, string column)
    {
        var ordinal = r.GetOrdinal(column);
        return r.IsDBNull(ordinal) ? null : r.GetString(ordinal);
    }

    private static string EscapeLike(string text) =>
        text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}

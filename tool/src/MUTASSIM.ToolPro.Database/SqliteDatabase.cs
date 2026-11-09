using Microsoft.Data.Sqlite;

namespace MUTASSIM.ToolPro.Database;

/// <summary>Local SQLite store with forward-only, versioned schema migrations.</summary>
public sealed class SqliteDatabase
{
    // Append new migrations at the end. Never edit a shipped migration.
    private static readonly string[] Migrations =
    [
        // v1
        """
        CREATE TABLE operations (
            id             INTEGER PRIMARY KEY AUTOINCREMENT,
            started_at     TEXT    NOT NULL,
            finished_at    TEXT    NULL,
            service_id     TEXT    NOT NULL,
            service_name   TEXT    NOT NULL,
            device_key     TEXT    NULL,
            manufacturer   TEXT    NULL,
            model          TEXT    NULL,
            mode           INTEGER NOT NULL,
            status         INTEGER NOT NULL,
            result_message TEXT    NULL,
            error_details  TEXT    NULL
        );
        CREATE INDEX ix_operations_started ON operations(started_at DESC);
        CREATE INDEX ix_operations_device  ON operations(device_key);

        CREATE TABLE devices_seen (
            device_key   TEXT PRIMARY KEY,
            manufacturer TEXT NULL,
            model        TEXT NULL,
            mode         INTEGER NOT NULL,
            first_seen   TEXT NOT NULL,
            last_seen    TEXT NOT NULL
        );
        CREATE INDEX ix_devices_last_seen ON devices_seen(last_seen DESC);
        """
    ];

    public SqliteDatabase(string databaseFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseFile);
        var dir = Path.GetDirectoryName(Path.GetFullPath(databaseFile));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databaseFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true
        }.ToString();
    }

    public string ConnectionString { get; }

    public static int LatestSchemaVersion => Migrations.Length;

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    /// <summary>Creates/updates the schema. Safe to call on every start.</summary>
    public async Task<int> InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

        await using (var wal = connection.CreateCommand())
        {
            wal.CommandText = "PRAGMA journal_mode = WAL;";
            await wal.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var getVersion = connection.CreateCommand())
        {
            getVersion.CommandText = "PRAGMA user_version;";
            var current = Convert.ToInt32(await getVersion.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));

            for (var v = current; v < Migrations.Length; v++)
            {
                await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
                    .ConfigureAwait(false);
                await using var cmd = connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = Migrations[v] + $"\nPRAGMA user_version = {v + 1};";
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        return Migrations.Length;
    }
}

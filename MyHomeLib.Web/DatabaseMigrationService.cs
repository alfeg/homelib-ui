using DuckDB.NET.Data;
using Microsoft.Extensions.Options;

namespace MyHomeLib.Web;

public sealed class DatabaseMigrationService(
    IOptions<LibraryConfig> options,
    ILogger<DatabaseMigrationService> logger)
{
    private readonly LibraryConfig _config = options.Value;

    public async Task MigrateAsync(CancellationToken ct = default)
    {
        await MigrateQueueDbAsync(ct);
        await MigrateAuditDbAsync(ct);
    }

    private async Task MigrateQueueDbAsync(CancellationToken ct)
    {
        var path = ResolvePath(_config.QueueDbPath, "queue.db");
        if (path is null) return;

        var migrations = new[]
        {
            new Migration(
                "queue-0001",
                "Create download queue table",
                """
                CREATE TABLE IF NOT EXISTS download_queue (
                    id            VARCHAR PRIMARY KEY,
                    user_id       VARCHAR,
                    book_id       INTEGER,
                    title         VARCHAR,
                    authors       VARCHAR,
                    archive       VARCHAR,
                    file_name     VARCHAR,
                    status        VARCHAR DEFAULT 'Pending',
                    error         VARCHAR,
                    file_path     VARCHAR,
                    download_name VARCHAR,
                    content_type  VARCHAR,
                    added_at      TIMESTAMP DEFAULT now(),
                    completed_at  TIMESTAMP
                )
                """)
            ,
            new Migration(
                "queue-0002",
                "Add user_id column to existing queue table",
                "ALTER TABLE download_queue ADD COLUMN user_id VARCHAR")
        };

        await ApplyMigrationsAsync(path, migrations, ct);
    }

    private async Task MigrateAuditDbAsync(CancellationToken ct)
    {
        var path = ResolvePath(_config.AuditDbPath, "audit.db");
        if (path is null) return;

        var migrations = new[]
        {
            new Migration(
                "audit-0001",
                "Create audit log table",
                """
                CREATE TABLE IF NOT EXISTS audit_log (
                    id           VARCHAR PRIMARY KEY,
                    event_type   VARCHAR NOT NULL,
                    timestamp    TIMESTAMPTZ DEFAULT now(),
                    query        VARCHAR,
                    result_count INTEGER,
                    book_id      INTEGER,
                    title        VARCHAR,
                    authors      VARCHAR,
                    archive      VARCHAR,
                    file_name    VARCHAR,
                    job_id       VARCHAR
                )
                """)
        };

        await ApplyMigrationsAsync(path, migrations, ct);
    }

    private async Task ApplyMigrationsAsync(string dbPath, IReadOnlyList<Migration> migrations, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        await using var db = new DuckDBConnection($"DataSource={dbPath}");
        await db.OpenAsync(ct);

        using (var create = db.CreateCommand())
        {
            create.CommandText = """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    id          VARCHAR PRIMARY KEY,
                    description VARCHAR,
                    applied_at  TIMESTAMPTZ DEFAULT now()
                )
                """;
            await create.ExecuteNonQueryAsync(ct);
        }

        var applied = new HashSet<string>(StringComparer.Ordinal);
        using (var readApplied = db.CreateCommand())
        {
            readApplied.CommandText = "SELECT id FROM schema_migrations";
            using var reader = await readApplied.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                applied.Add(reader.GetString(0));
        }

        foreach (var migration in migrations)
        {
            if (applied.Contains(migration.Id))
                continue;

            try
            {
                using var cmd = db.CreateCommand();
                cmd.CommandText = migration.Sql;
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (Exception ex)
            {
                if (!IsAlreadyAppliedError(ex))
                    throw;

                logger.LogInformation("Migration {Id} already applied on {DbPath}", migration.Id, dbPath);
            }

            using var insert = db.CreateCommand();
            insert.CommandText = $"INSERT INTO schema_migrations (id, description) VALUES ('{Esc(migration.Id)}', '{Esc(migration.Description)}')";
            await insert.ExecuteNonQueryAsync(ct);
            logger.LogInformation("Applied migration {Id} on {DbPath}", migration.Id, dbPath);
        }
    }

    private string? ResolvePath(string configuredPath, string fallbackFileName)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
            return configuredPath;

        if (string.IsNullOrWhiteSpace(_config.DownloadsDirectory))
        {
            logger.LogWarning("Skipping {FileName} migration because Library:DownloadsDirectory is empty.", fallbackFileName);
            return null;
        }

        return Path.Combine(_config.DownloadsDirectory, fallbackFileName);
    }

    private static string Esc(string value) => value.Replace("'", "''");

    private static bool IsAlreadyAppliedError(Exception ex)
    {
        var message = ex.Message;
        return message.Contains("already exists", StringComparison.OrdinalIgnoreCase)
               || message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Column with name", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record Migration(string Id, string Description, string Sql);
}

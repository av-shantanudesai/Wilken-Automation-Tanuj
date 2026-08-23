using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WilkenAutomation.Infrastructure.Database;

namespace WilkenAutomation.Infrastructure.Database;

/// <summary>
/// EnsureCreated does not alter an existing database. Applies additive schema
/// (AppUsers, UserId, RefreshTokens) on first start after upgrade.
/// </summary>
public static class DatabaseSchemaPatcher
{
    public static async Task ApplyAsync(AutomationDbContext db, ILogger logger, CancellationToken ct = default)
    {
        await db.Database.EnsureCreatedAsync(ct);
        if (!db.Database.IsRelational()) return;

        try
        {
            if (db.Database.IsMySql())
                await PatchMySqlAsync(db, ct);
            else if (db.Database.IsSqlite())
                await PatchSqliteAsync(db, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Schema patch failed. Recreate the database or apply AppUsers / UserId / RefreshTokens manually.");
        }
    }

    private static async Task PatchMySqlAsync(AutomationDbContext db, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS AppUsers (
                Id bigint NOT NULL AUTO_INCREMENT,
                Email varchar(256) NOT NULL,
                DisplayName varchar(128) NOT NULL,
                PasswordHash varchar(256) NOT NULL,
                CreatedAt datetime(6) NOT NULL,
                PRIMARY KEY (Id),
                UNIQUE KEY IX_AppUsers_Email (Email)
            )
            """, ct);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS RefreshTokens (
                Id bigint NOT NULL AUTO_INCREMENT,
                UserId bigint NOT NULL,
                TokenHash varchar(64) NOT NULL,
                FamilyId varchar(32) NOT NULL,
                ExpiresAt datetime(6) NOT NULL,
                CreatedAt datetime(6) NOT NULL,
                RevokedAt datetime(6) NULL,
                ReplacedByTokenHash varchar(64) NULL,
                CreatedByIp varchar(64) NULL,
                PRIMARY KEY (Id),
                UNIQUE KEY IX_RefreshTokens_TokenHash (TokenHash),
                KEY IX_RefreshTokens_UserId_FamilyId (UserId, FamilyId)
            )
            """, ct);

        var hasUserId = await ScalarAsync(db,
            "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'AutomationRuns' AND COLUMN_NAME = 'UserId'",
            ct);
        if (hasUserId == 0)
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE AutomationRuns ADD COLUMN UserId bigint NOT NULL DEFAULT 0", ct);
            await db.Database.ExecuteSqlRawAsync(
                "CREATE INDEX IX_AutomationRuns_UserId ON AutomationRuns (UserId)", ct);
        }

        await EnsureColumnAsync(db, "ExportJobs", "ExportDefinition", "varchar(64) NOT NULL DEFAULT ''", ct);
        await EnsureColumnAsync(db, "ExportJobs", "ExecutorType", "varchar(16) NOT NULL DEFAULT 'SPOOL'", ct);
        await EnsureColumnAsync(db, "ExportJobs", "Period", "varchar(32) NOT NULL DEFAULT ''", ct);
        await EnsureColumnAsync(db, "ExportJobs", "AccountingLaw", "varchar(64) NOT NULL DEFAULT ''", ct);
        await EnsureColumnAsync(db, "ExportJobs", "Company", "varchar(64) NOT NULL DEFAULT ''", ct);
        await EnsureColumnAsync(db, "ExportJobs", "SpoolId", "varchar(96) NULL", ct);
    }

    private static async Task EnsureColumnAsync(AutomationDbContext db, string table, string column, string typeSql, CancellationToken ct)
    {
        var exists = await ScalarAsync(db,
            $"SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{table}' AND COLUMN_NAME = '{column}'",
            ct);
        if (exists == 0)
            await db.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} ADD COLUMN {column} {typeSql}", ct);
    }

    private static async Task PatchSqliteAsync(AutomationDbContext db, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS AppUsers (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Email TEXT NOT NULL,
                DisplayName TEXT NOT NULL,
                PasswordHash TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            )
            """, ct);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_AppUsers_Email ON AppUsers (Email)", ct);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS RefreshTokens (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                UserId INTEGER NOT NULL,
                TokenHash TEXT NOT NULL,
                FamilyId TEXT NOT NULL,
                ExpiresAt TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                RevokedAt TEXT NULL,
                ReplacedByTokenHash TEXT NULL,
                CreatedByIp TEXT NULL
            )
            """, ct);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_RefreshTokens_TokenHash ON RefreshTokens (TokenHash)", ct);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_RefreshTokens_UserId_FamilyId ON RefreshTokens (UserId, FamilyId)", ct);

        var hasUserId = await ScalarAsync(db,
            "SELECT COUNT(*) FROM pragma_table_info('AutomationRuns') WHERE name = 'UserId'", ct);
        if (hasUserId == 0)
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE AutomationRuns ADD COLUMN UserId INTEGER NOT NULL DEFAULT 0", ct);

        await EnsureSqliteColumnAsync(db, "ExportJobs", "ExportDefinition", "TEXT NOT NULL DEFAULT ''", ct);
        await EnsureSqliteColumnAsync(db, "ExportJobs", "ExecutorType", "TEXT NOT NULL DEFAULT 'SPOOL'", ct);
        await EnsureSqliteColumnAsync(db, "ExportJobs", "Period", "TEXT NOT NULL DEFAULT ''", ct);
        await EnsureSqliteColumnAsync(db, "ExportJobs", "AccountingLaw", "TEXT NOT NULL DEFAULT ''", ct);
        await EnsureSqliteColumnAsync(db, "ExportJobs", "Company", "TEXT NOT NULL DEFAULT ''", ct);
        await EnsureSqliteColumnAsync(db, "ExportJobs", "SpoolId", "TEXT NULL", ct);
    }

    private static async Task EnsureSqliteColumnAsync(AutomationDbContext db, string table, string column, string typeSql, CancellationToken ct)
    {
        var exists = await ScalarAsync(db,
            $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}'", ct);
        if (exists == 0)
            await db.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} ADD COLUMN {column} {typeSql}", ct);
    }

    private static async Task<long> ScalarAsync(AutomationDbContext db, string sql, CancellationToken ct)
    {
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = sql;
        if (cmd.Connection!.State != System.Data.ConnectionState.Open)
            await cmd.Connection.OpenAsync(ct);
        var value = await cmd.ExecuteScalarAsync(ct);
        return value is null or DBNull ? 0 : Convert.ToInt64(value);
    }
}

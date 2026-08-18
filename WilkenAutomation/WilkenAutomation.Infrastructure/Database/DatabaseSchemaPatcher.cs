using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WilkenAutomation.Infrastructure.Database;

namespace WilkenAutomation.Infrastructure.Database;

/// <summary>
/// EnsureCreated does not alter an existing database. This applies the auth
/// schema (AppUsers table + AutomationRuns.UserId) on first start after upgrade.
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
            logger.LogWarning(ex, "Auth schema patch failed. If login fails, recreate the database or add AppUsers / AutomationRuns.UserId manually.");
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

        var hasUserId = await ScalarAsync(db,
            "SELECT COUNT(*) FROM pragma_table_info('AutomationRuns') WHERE name = 'UserId'", ct);
        if (hasUserId == 0)
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE AutomationRuns ADD COLUMN UserId INTEGER NOT NULL DEFAULT 0", ct);
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

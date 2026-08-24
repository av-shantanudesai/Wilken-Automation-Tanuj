using Microsoft.Extensions.Configuration;

namespace WilkenAutomation.Infrastructure.Configuration;

/// <summary>
/// Resolves the MySQL connection string from environment variables first, then configuration.
/// <list type="bullet">
/// <item><c>WILKEN_DB_CONNECTION</c> — full connection string override</item>
/// <item><c>WILKEN_DB_PASSWORD</c> or <c>Database:Password</c> — injected when the base string has no password</item>
/// </list>
/// </summary>
public static class DatabaseConnection
{
    public const string ConnectionEnvironmentVariable = "WILKEN_DB_CONNECTION";
    public const string PasswordEnvironmentVariable = "WILKEN_DB_PASSWORD";

    public static string Resolve(IConfiguration configuration)
    {
        var fromEnv = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv.Trim();

        var connectionString = configuration.GetConnectionString("Default")
            ?? configuration["Database:ConnectionString"]
            ?? throw new InvalidOperationException("Database connection string not configured.");

        var password = Environment.GetEnvironmentVariable(PasswordEnvironmentVariable)
            ?? configuration["Database:Password"];
        if (string.IsNullOrWhiteSpace(password))
            return connectionString;

        return WithPassword(connectionString, password);
    }

    internal static string WithPassword(string connectionString, string password)
    {
        var parts = connectionString
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part =>
                !part.StartsWith("Password=", StringComparison.OrdinalIgnoreCase)
                && !part.StartsWith("Pwd=", StringComparison.OrdinalIgnoreCase))
            .ToList();
        parts.Add("Password=" + password);
        return string.Join(";", parts);
    }
}

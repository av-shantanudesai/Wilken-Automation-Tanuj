using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Infrastructure.Database;
using WilkenAutomation.Infrastructure.FileSystem;
using WilkenAutomation.Infrastructure.Repositories;

namespace WilkenAutomation.Infrastructure.Configuration;

/// <summary>
/// Shared registration for API and Worker.
/// Database:Provider = MySql (production) | Sqlite (local testing when no MySQL
/// server is available - a file-based DB both processes can share).
/// </summary>
public static class InfrastructureRegistration
{
    public static IServiceCollection AddWilkenInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"] ?? "MySql";
        var connectionString = configuration.GetConnectionString("Default")
            ?? configuration["Database:ConnectionString"]
            ?? throw new InvalidOperationException("Database connection string not configured.");

        services.AddDbContext<AutomationDbContext>(options =>
        {
            if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
                options.UseSqlite(connectionString);
            else
                options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString),
                    mysql => mysql.EnableRetryOnFailure(5));
        });

        services.AddScoped<IRunRepository, RunRepository>();
        services.AddScoped<IJobRepository, JobRepository>();
        services.AddScoped<ILogRepository, LogRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddSingleton<IChecksumService, ChecksumService>();

        return services;
    }
}

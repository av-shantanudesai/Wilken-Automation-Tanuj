using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WilkenAutomation.Application.Configuration;
using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Application.Services;
using WilkenAutomation.Infrastructure.Configuration;
using WilkenAutomation.Infrastructure.Database;

namespace WilkenAutomation.Worker.Wilken;

/// <summary>
/// Creates a small Running run in the Worker DB so a published Worker can claim
/// jobs without the dashboard. Usage:
///   WilkenAutomation.Worker.exe --seed-jobs --client 02 --year 2020 --department Handelsrecht --export Zugangsliste
/// </summary>
internal static class SeedJobsRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        var client = Arg(args, "--client") ?? "02";
        var yearText = Arg(args, "--year") ?? "2020";
        if (!int.TryParse(yearText, out var year))
        {
            Console.Error.WriteLine($"Invalid --year '{yearText}'.");
            return 2;
        }
        var department = Arg(args, "--department") ?? "Handelsrecht";
        var export = Arg(args, "--export") ?? "Zugangsliste";
        var startRunning = !args.Any(a => a.Equals("--paused", StringComparison.OrdinalIgnoreCase));

        var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
        builder.Configuration.AddJsonFile("appsettings.json", optional: true);
        builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true);
        builder.Configuration.AddEnvironmentVariables();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddWilkenInfrastructure(builder.Configuration);
        builder.Services.AddSingleton(ExportDefinitionCatalog.Load(builder.Environment.ContentRootPath));
        builder.Services.AddSingleton(builder.Configuration.GetSection(RunDefaults.Section).Get<RunDefaults>() ?? new RunDefaults());
        builder.Services.AddScoped<JobGeneratorService>();

        using var host = builder.Build();
        using var scope = host.Services.CreateScope();
        try
        {
            var db = scope.ServiceProvider.GetRequiredService<AutomationDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Schema");
            await DatabaseSchemaPatcher.ApplyAsync(db, logger);

            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var user = await users.GetFirstAsync(CancellationToken.None)
                ?? await users.CreateAsync(new AppUser
                {
                    Email = "worker-capture@local",
                    DisplayName = "Worker capture",
                    PasswordHash = PasswordHasher.Hash(Guid.NewGuid().ToString("N")),
                    CreatedAt = DateTime.UtcNow
                }, CancellationToken.None);

            var runs = scope.ServiceProvider.GetRequiredService<IRunRepository>();
            if (startRunning)
            {
                foreach (var existing in await runs.ListByStatusAsync(RunStatus.Running, CancellationToken.None))
                {
                    existing.Status = RunStatus.Paused;
                    await runs.UpdateAsync(existing, CancellationToken.None);
                    Console.WriteLine($"Paused existing run {existing.RunId} so the new run is claimed first.");
                }
            }

            var generator = scope.ServiceProvider.GetRequiredService<JobGeneratorService>();
            var config = generator.BuildConfig(new CreateRunRequestDto
            {
                Clients = [client],
                Years = [year],
                Departments = [department],
                ExportDefinitions = [export],
                MaxAttempts = 3,
                EnableContentValidation = false,
                EnableChecksum = true,
                AutoStart = startRunning,
                Notes = "Seeded from published Worker --seed-jobs"
            });
            var run = await generator.GenerateRunAsync(
                config,
                "Seeded from published Worker --seed-jobs",
                autoStart: startRunning,
                CancellationToken.None,
                user.Id);

            Console.WriteLine();
            Console.WriteLine($"Created run {run.RunId} ({run.TotalJobs} job(s), status {run.Status}).");
            Console.WriteLine($"  Client={client} Year={year} Department={department} Export={export}");
            if (startRunning)
            {
                Console.WriteLine("Start the worker with no inspect flags so it claims this run:");
                Console.WriteLine("  WilkenAutomation.Worker.exe");
                Console.WriteLine("Wilken must already be logged in on this desktop (attach-only).");
            }
            else
            {
                Console.WriteLine("Run is Created (not Running). Start it from the dashboard, or seed again without --paused.");
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Cannot reach MySQL from this machine. Run --seed-jobs on the Test Environment desktop (VPN/same network as the DB).");
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    private static string? Arg(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }
}

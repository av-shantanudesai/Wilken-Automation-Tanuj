using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WilkenExport.Api.Configuration;
using WilkenExport.Api.Data;
using WilkenExport.Api.Domain;

namespace WilkenExport.Api.Engine;

public class JobGenerator
{
    private readonly ExportDbContext _db;

    public JobGenerator(ExportDbContext db) => _db = db;

    public static string DepartmentCode(string department) => department switch
    {
        "Handelsrecht" => "HR",
        "Steuerrecht" => "ST",
        _ => new string(department.Where(char.IsLetter).Take(2).ToArray()).ToUpperInvariant()
    };

    public async Task<string> NextRunIdAsync(CancellationToken ct)
    {
        var prefix = $"RUN-{DateTime.UtcNow:yyyyMMdd}-";
        var todayCount = await _db.Runs.CountAsync(r => r.Id.StartsWith(prefix), ct);
        return $"{prefix}{todayCount + 1:D3}";
    }

    /// <summary>
    /// Generates the run and all jobs from the effective configuration. The expected
    /// count is calculated dynamically (Clients x Years x Departments); any deviation
    /// between expected and generated jobs is recorded on the run before it can start.
    /// </summary>
    public async Task<ExportRun> GenerateRunAsync(RunConfig config, string? notes, CancellationToken ct)
    {
        if (config.Clients.Count == 0 || config.Years.Count == 0 || config.Departments.Count == 0)
            throw new ArgumentException("Clients, years and departments must each contain at least one entry.");

        var runId = await NextRunIdAsync(ct);
        var now = DateTime.UtcNow;

        var run = new ExportRun
        {
            Id = runId,
            Status = RunStatus.Created,
            CreatedAt = now,
            ExpectedJobCount = config.ExpectedJobCount,
            ConfigJson = JsonSerializer.Serialize(config),
            Notes = notes
        };

        var combinations =
            from client in config.Clients
            from year in config.Years
            from department in config.Departments
            select (client, year, department);

        var ordered = ApplyOrder(combinations, config);

        var index = 0;
        foreach (var (client, year, department) in ordered)
        {
            run.Jobs.Add(new ExportJob
            {
                Id = $"{runId}-M{client}-{year}-{DepartmentCode(department)}",
                RunId = runId,
                Client = client,
                FiscalYear = year,
                Department = department,
                DepartmentCode = DepartmentCode(department),
                OrderIndex = index++,
                Status = JobStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        run.GeneratedJobCount = run.Jobs.Count;

        _db.Runs.Add(run);
        await _db.SaveChangesAsync(ct);
        return run;
    }

    /// <summary>Processing order is configurable; default keeps client/year pairs together.</summary>
    private static IEnumerable<(string client, int year, string department)> ApplyOrder(
        IEnumerable<(string client, int year, string department)> jobs, RunConfig config)
    {
        var keys = config.JobOrder.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        IOrderedEnumerable<(string client, int year, string department)>? ordered = null;

        foreach (var key in keys)
        {
            Func<(string client, int year, string department), object> selector = key.ToLowerInvariant() switch
            {
                "client" => j => j.client,
                "fiscalyear" or "year" => j => j.year,
                "department" => j => Array.IndexOf(config.Departments.ToArray(), j.department),
                _ => throw new ArgumentException($"Unknown job order key '{key}'.")
            };
            ordered = ordered == null ? jobs.OrderBy(selector) : ordered.ThenBy(selector);
        }

        return ordered ?? jobs.OrderBy(j => j.client);
    }
}

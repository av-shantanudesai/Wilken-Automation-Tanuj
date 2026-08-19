using System.Text.Json;
using WilkenAutomation.Application.Configuration;
using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;

namespace WilkenAutomation.Application.Services;

/// <summary>
/// Generates the complete job queue for a run: Client x FiscalYear x Department,
/// with configurable ordering, deterministic ids, and duplicate prevention.
/// Expected count is always calculated dynamically - never hard-coded.
/// </summary>
public class JobGeneratorService
{
    private readonly IRunRepository _runs;
    private readonly RunDefaults _defaults;

    public JobGeneratorService(IRunRepository runs, RunDefaults defaults)
    {
        _runs = runs;
        _defaults = defaults;
    }

    public static string DepartmentCode(string department) => department switch
    {
        "Handelsrecht" or "CommercialLaw" or "Commercial Law" => "HR",
        "Steuerrecht" or "TaxLaw" or "Tax Law" => "ST",
        _ => new string(department.Where(char.IsLetter).Take(2).ToArray()).ToUpperInvariant()
    };

    public RunConfig BuildConfig(CreateRunRequestDto request)
    {
        var clients = request.Clients is { Count: > 0 }
            ? request.Clients
            : Enumerable.Range(1, request.ClientCount ?? _defaults.ClientCount).Select(i => i.ToString("D3")).ToList();

        List<int> years;
        if (request.Years is { Count: > 0 }) years = request.Years;
        else
        {
            var from = request.YearFrom ?? _defaults.YearFrom;
            var to = request.YearTo ?? _defaults.YearTo;
            if (to < from) (from, to) = (to, from);
            years = Enumerable.Range(from, to - from + 1).ToList();
        }

        var departments = request.Departments is { Count: > 0 } ? request.Departments : _defaults.EffectiveDepartments;

        return new RunConfig
        {
            Clients = clients.Distinct().ToList(),
            Years = years.Distinct().ToList(),
            Departments = departments.Distinct().ToList(),
            JobOrder = request.JobOrder ?? "Client,FiscalYear,Department",
            MaxAttempts = request.MaxAttempts ?? 3,
            EnableContentValidation = request.EnableContentValidation ?? true,
            EnableChecksum = request.EnableChecksum ?? true,
            Simulation = request.Simulation ?? new SimulationConfig()
        };
    }

    public async Task<AutomationRun> GenerateRunAsync(RunConfig config, string? notes, bool autoStart, CancellationToken ct, long userId = 0)
    {
        if (userId <= 0)
            throw new ArgumentOutOfRangeException(nameof(userId), "A valid owning user is required to create a run.");
        if (config.ExpectedJobs == 0)
            throw new ArgumentException("Configuration yields zero jobs - check clients, years and departments.");

        var prefix = $"RUN-{DateTime.UtcNow:yyyyMMdd}-";
        var sequence = await _runs.CountRunsWithPrefixAsync(prefix, ct) + 1;
        var runId = $"{prefix}{sequence:D3}";
        var now = DateTime.UtcNow;

        var combinations = Order(
            from client in config.Clients
            from year in config.Years
            from department in config.Departments
            select (client, year, department),
            config);

        var seen = new HashSet<string>();
        var jobs = new List<ExportJob>();
        var index = 0;
        foreach (var (client, year, department) in combinations)
        {
            var jobId = $"{runId}-M{client}-{year}-{DepartmentCode(department)}";
            if (!seen.Add(jobId)) continue; // duplicate prevention for Run+Client+Year+Department

            jobs.Add(new ExportJob
            {
                JobId = jobId,
                RunId = runId,
                Client = client,
                FiscalYear = year,
                Department = department,
                DepartmentCode = DepartmentCode(department),
                OrderIndex = index++,
                Status = JobStatus.Pending,
                ApplicationState = "IDLE",
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        var run = new AutomationRun
        {
            RunId = runId,
            UserId = userId,
            Status = autoStart ? RunStatus.Running : RunStatus.Created,
            ExpectedJobs = config.ExpectedJobs,
            TotalJobs = jobs.Count,
            PendingJobs = jobs.Count,
            StartedAt = autoStart ? now : null,
            CreatedAt = now,
            UpdatedAt = now,
            ConfigJson = JsonSerializer.Serialize(config),
            Notes = notes
        };

        return await _runs.CreateWithJobsAsync(run, jobs, ct);
    }

    private static IEnumerable<(string client, int year, string department)> Order(
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
                "department" => j => config.Departments.IndexOf(j.department),
                _ => throw new ArgumentException($"Unknown job order key '{key}'.")
            };
            ordered = ordered == null ? jobs.OrderBy(selector) : ordered.ThenBy(selector);
        }

        return ordered ?? jobs.OrderBy(j => j.client);
    }
}

using System.Text.Json;
using WilkenAutomation.Application.Configuration;
using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;

namespace WilkenAutomation.Application.Services;

/// <summary>
/// Builds the job queue from selected export definitions and the dimensions
/// each definition requires. Legacy Client × Year × Department runs still work
/// when no definitions are sent (Zugangsliste + Anlagenspiegel).
/// </summary>
public class JobGeneratorService
{
    private readonly IRunRepository _runs;
    private readonly RunDefaults _defaults;
    private readonly ExportDefinitionCatalog _catalog;

    public JobGeneratorService(IRunRepository runs, RunDefaults defaults, ExportDefinitionCatalog catalog)
    {
        _runs = runs;
        _defaults = defaults;
        _catalog = catalog;
    }

    public static string DepartmentCode(string department) => department switch
    {
        "Handelsrecht" or "CommercialLaw" or "Commercial Law" => "HR",
        "Steuerrecht" or "TaxLaw" or "Tax Law" => "ST",
        "" => "NA",
        _ => new string(department.Where(char.IsLetter).Take(2).ToArray()).ToUpperInvariant()
    };

    public RunConfig BuildConfig(CreateRunRequestDto request)
    {
        var clients = request.Clients is { Count: > 0 }
            ? request.Clients.Select(c => c.Trim()).Where(c => c.Length > 0).ToList()
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
        var periods = request.Periods is { Count: > 0 } ? request.Periods : new List<string> { "01-12" };
        var definitions = request.ExportDefinitions is { Count: > 0 }
            ? request.ExportDefinitions
            : ExportDefinitionCatalog.DefaultDefinitionNames.ToList();

        var config = new RunConfig
        {
            Clients = clients.Distinct().ToList(),
            Years = years.Distinct().ToList(),
            Departments = departments.Distinct().ToList(),
            Periods = periods.Distinct().ToList(),
            ExportDefinitions = definitions.Distinct().ToList(),
            JobOrder = request.JobOrder ?? "Client,FiscalYear,Department",
            MaxAttempts = request.MaxAttempts ?? 3,
            EnableContentValidation = request.EnableContentValidation ?? true,
            EnableChecksum = request.EnableChecksum ?? true,
            Simulation = request.Simulation ?? new SimulationConfig(),
            WilkenExecutablePath = string.IsNullOrWhiteSpace(request.WilkenExecutablePath)
                ? null : request.WilkenExecutablePath.Trim(),
            ExportRootDirectory = string.IsNullOrWhiteSpace(request.ExportRootDirectory)
                ? null : request.ExportRootDirectory.Trim()
        };
        config.ExpectedJobs = ExpandJobs(config).Count;
        return config;
    }

    public async Task<AutomationRun> GenerateRunAsync(RunConfig config, string? notes, bool autoStart, CancellationToken ct, long userId = 0)
    {
        if (userId <= 0)
            throw new ArgumentOutOfRangeException(nameof(userId), "A valid owning user is required to create a run.");

        var expanded = ExpandJobs(config);
        if (expanded.Count == 0)
            throw new ArgumentException("Configuration yields zero jobs - check clients, years, departments and export definitions.");

        var prefix = $"RUN-{DateTime.UtcNow:yyyyMMdd}-";
        var sequence = await _runs.CountRunsWithPrefixAsync(prefix, ct) + 1;
        var runId = $"{prefix}{sequence:D3}";
        var now = DateTime.UtcNow;

        var seen = new HashSet<string>();
        var jobs = new List<ExportJob>();
        var index = 0;
        foreach (var item in expanded)
        {
            var yearPart = item.Year > 0 ? item.Year.ToString() : "X";
            var periodPart = string.IsNullOrWhiteSpace(item.Period) ? "" : $"-{item.Period}";
            var jobId = $"{runId}-M{item.Client}-{yearPart}-{DepartmentCode(item.Law)}-{item.Definition.Name}{periodPart}";
            if (!seen.Add(jobId)) continue;

            jobs.Add(new ExportJob
            {
                JobId = jobId,
                RunId = runId,
                Client = item.Client,
                FiscalYear = item.Year,
                Department = item.Law,
                DepartmentCode = DepartmentCode(item.Law),
                ExportDefinition = item.Definition.Name,
                ExecutorType = item.Definition.Type,
                Period = item.Period,
                AccountingLaw = item.Law,
                OrderIndex = index++,
                Status = JobStatus.Pending,
                ApplicationState = "IDLE",
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        config.ExpectedJobs = jobs.Count;
        var run = new AutomationRun
        {
            RunId = runId,
            UserId = userId,
            Status = autoStart ? RunStatus.Running : RunStatus.Created,
            ExpectedJobs = jobs.Count,
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

    private List<ExpandedJob> ExpandJobs(RunConfig config)
    {
        var items = new List<ExpandedJob>();
        foreach (var name in config.ExportDefinitions)
        {
            var definition = _catalog.Get(name);
            var clients = definition.RequiresDimension(ExportDimensions.Client) ? config.Clients : new List<string> { "" };
            var years = definition.RequiresDimension(ExportDimensions.Year) ? config.Years : new List<int> { 0 };
            var laws = LawsFor(definition, config);
            var periods = definition.RequiresDimension(ExportDimensions.Period)
                ? (config.Periods.Count > 0 ? config.Periods : new List<string> { definition.DefaultValue(ExportDimensions.Period, "01-12") })
                : new List<string> { "" };

            foreach (var client in clients)
            foreach (var year in years)
            foreach (var law in laws)
            foreach (var period in periods)
                items.Add(new ExpandedJob(definition, client, year, law, period));
        }

        return Order(items, config).ToList();
    }

    private static List<string> LawsFor(ExportDefinition definition, RunConfig config)
    {
        if (!definition.RequiresDimension(ExportDimensions.AccountingLaw))
            return new List<string> { "" };

        var preferred = definition.DefaultValue(ExportDimensions.AccountingLaw);
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            if (config.Departments.Count == 0 || config.Departments.Contains(preferred, StringComparer.OrdinalIgnoreCase))
                return new List<string> { preferred };
            return new List<string>();
        }

        return config.Departments.Count > 0 ? config.Departments : new List<string> { "Handelsrecht" };
    }

    private static IEnumerable<ExpandedJob> Order(IEnumerable<ExpandedJob> jobs, RunConfig config)
    {
        var keys = config.JobOrder.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        IOrderedEnumerable<ExpandedJob>? ordered = null;
        foreach (var key in keys)
        {
            Func<ExpandedJob, object> selector = key.ToLowerInvariant() switch
            {
                "client" => j => j.Client,
                "fiscalyear" or "year" => j => j.Year,
                "department" or "accountinglaw" => j => config.Departments.IndexOf(j.Law),
                "export" or "exportdefinition" => j => j.Definition.Name,
                "period" => j => j.Period,
                _ => throw new ArgumentException($"Unknown job order key '{key}'.")
            };
            ordered = ordered == null ? jobs.OrderBy(selector) : ordered.ThenBy(selector);
        }

        return ordered ?? jobs.OrderBy(j => j.Client).ThenBy(j => j.Definition.Name);
    }

    private sealed record ExpandedJob(ExportDefinition Definition, string Client, int Year, string Law, string Period);
}

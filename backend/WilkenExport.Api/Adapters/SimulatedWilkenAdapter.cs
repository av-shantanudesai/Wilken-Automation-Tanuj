using System.Text;
using WilkenExport.Api.Configuration;
using WilkenExport.Api.Domain;

namespace WilkenExport.Api.Adapters;

/// <summary>
/// Dummy Wilken CS/2 adapter for testing the engine end-to-end without the real application.
/// It simulates session startup, navigation, report execution with realistic variable
/// durations, spool readiness polling, and writes a structured dummy export file whose
/// content embeds client / fiscal year / department so content validation can be exercised.
/// It also injects random failures, session crashes and legitimately empty periods
/// according to the run's SimulationOptions.
/// </summary>
public class SimulatedWilkenAdapter : IWilkenAdapter
{
    private readonly ILogger<SimulatedWilkenAdapter> _logger;
    private readonly Random _random = new();

    private RunConfig? _config;
    private string _sessionStatus = "NotStarted";
    private string? _selectedClient;
    private int? _fiscalYear;
    private string? _department;

    // Outcome decided when the evaluation starts, consumed by the following steps.
    private enum Outcome { Success, Empty, ReportFailure, Crash }
    private Outcome _plannedOutcome;
    private double _jobSeconds;

    public string SessionStatus => _sessionStatus;

    public SimulatedWilkenAdapter(ILogger<SimulatedWilkenAdapter> logger) => _logger = logger;

    public async Task EnsureSessionAsync(RunConfig config, CancellationToken ct)
    {
        _config = config;
        if (_sessionStatus == "Ready") return;

        _sessionStatus = "Starting";
        await Task.Delay(TimeSpan.FromMilliseconds(400 + _random.Next(400)), ct);
        _sessionStatus = "LoggingIn";
        await Task.Delay(TimeSpan.FromMilliseconds(200 + _random.Next(300)), ct);
        _sessionStatus = "Ready";
        _logger.LogInformation("Simulated Wilken session ready");
    }

    public async Task SelectClientAsync(string client, CancellationToken ct)
    {
        RequireSession();
        await Task.Delay(StepDelay(), ct);
        _selectedClient = client;
    }

    public async Task OpenAssetAccountingAsync(CancellationToken ct)
    {
        RequireSession();
        await Task.Delay(StepDelay(), ct);
    }

    public async Task SetFiscalYearAsync(int year, CancellationToken ct)
    {
        RequireSession();
        await Task.Delay(StepDelay(), ct);
        _fiscalYear = year;
    }

    public async Task SelectDepartmentAsync(string department, CancellationToken ct)
    {
        RequireSession();
        await Task.Delay(StepDelay(), ct);
        _department = department;
    }

    public async Task<string> StartEvaluationAsync(ExportJob job, CancellationToken ct)
    {
        RequireSession();
        VerifyContext(job);

        var sim = _config!.Simulation;
        _jobSeconds = sim.MinJobSeconds + _random.NextDouble() * Math.Max(0, sim.MaxJobSeconds - sim.MinJobSeconds);

        var roll = _random.NextDouble();
        if (roll < sim.CrashRate) _plannedOutcome = Outcome.Crash;
        else if (roll < sim.CrashRate + sim.FailureRate) _plannedOutcome = Outcome.ReportFailure;
        else if (roll < sim.CrashRate + sim.FailureRate + sim.EmptyRate) _plannedOutcome = Outcome.Empty;
        else _plannedOutcome = Outcome.Success;

        await Task.Delay(StepDelay(), ct);
        return $"SPOOL-{job.Client}-{job.FiscalYear}-{job.DepartmentCode}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
    }

    public async Task<SpoolInfo> WaitForSpoolAsync(string reportId, ExportJob job, TimeSpan timeout, CancellationToken ct)
    {
        RequireSession();

        // The bulk of the simulated report runtime happens here, consumed via polling
        // (state-based readiness detection, never a fixed wait).
        var deadline = DateTime.UtcNow + timeout;
        var readyAt = DateTime.UtcNow + TimeSpan.FromSeconds(_jobSeconds * 0.7);
        var crashAt = DateTime.UtcNow + TimeSpan.FromSeconds(_jobSeconds * 0.35);

        while (DateTime.UtcNow < readyAt)
        {
            ct.ThrowIfCancellationRequested();

            if (_plannedOutcome == Outcome.Crash && DateTime.UtcNow >= crashAt)
            {
                _sessionStatus = "Crashed";
                throw new WilkenException("WILKEN_CRASH", "Simulated Wilken CS/2 crash during report processing.", sessionLost: true);
            }
            if (DateTime.UtcNow > deadline)
                throw new WilkenException("SPOOL_TIMEOUT", $"Spool for report {reportId} not ready within {timeout.TotalSeconds:F0}s.");

            await Task.Delay(250, ct);
        }

        if (_plannedOutcome == Outcome.ReportFailure)
            throw new WilkenException("REPORT_FAILED", "Simulated Wilken report generation error (spool marked faulty).");

        var recordCount = _plannedOutcome == Outcome.Empty ? 0 : 10 + _random.Next(490);
        return new SpoolInfo(reportId, recordCount);
    }

    public async Task ExportSpoolAsync(SpoolInfo spool, ExportJob job, string tempFilePath, CancellationToken ct)
    {
        RequireSession();
        VerifyContext(job);
        await Task.Delay(StepDelay(), ct);

        var sb = new StringBuilder();
        sb.AppendLine("WILKEN CS/2 ASSET ACCOUNTING EXPORT (SIMULATED)");
        sb.AppendLine($"Client;{job.Client}");
        sb.AppendLine($"FiscalYear;{job.FiscalYear}");
        sb.AppendLine($"Department;{job.Department}");
        sb.AppendLine($"ReportId;{spool.ReportId}");
        sb.AppendLine($"GeneratedAt;{DateTime.UtcNow:O}");
        sb.AppendLine("Sections;Zugaenge,Abgaenge,Anlagengitter,Kontensummen");
        sb.AppendLine($"RecordCount;{spool.RecordCount}");
        sb.AppendLine("---DATA---");
        sb.AppendLine("AssetNo;Description;Section;AcquisitionDate;Cost;AccumulatedDepreciation;BookValue");

        var sections = new[] { "Zugaenge", "Abgaenge", "Anlagengitter", "Kontensummen" };
        for (var i = 1; i <= spool.RecordCount; i++)
        {
            var cost = Math.Round(_random.NextDouble() * 100000, 2);
            var depr = Math.Round(cost * _random.NextDouble(), 2);
            sb.AppendLine(string.Join(';',
                $"A{job.FiscalYear}{i:D5}",
                $"Dummy asset {i} ({job.Department})",
                sections[_random.Next(sections.Length)],
                $"{job.FiscalYear}-{_random.Next(1, 13):D2}-{_random.Next(1, 29):D2}",
                cost.ToString(System.Globalization.CultureInfo.InvariantCulture),
                depr.ToString(System.Globalization.CultureInfo.InvariantCulture),
                (cost - depr).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        sb.AppendLine("---END_OF_REPORT---");

        Directory.CreateDirectory(Path.GetDirectoryName(tempFilePath)!);
        await File.WriteAllTextAsync(tempFilePath, sb.ToString(), Encoding.UTF8, ct);
    }

    public async Task RecoverSessionAsync(CancellationToken ct)
    {
        _logger.LogWarning("Recovering simulated Wilken session (previous status: {Status})", _sessionStatus);
        _sessionStatus = "Restarting";
        _selectedClient = null;
        _fiscalYear = null;
        _department = null;
        await Task.Delay(TimeSpan.FromMilliseconds(600 + _random.Next(600)), ct);
        _sessionStatus = "Ready";
    }

    private void RequireSession()
    {
        if (_sessionStatus != "Ready")
            throw new WilkenException("SESSION_LOST", $"Wilken session not usable (status: {_sessionStatus}).", sessionLost: true);
    }

    /// <summary>Guards against acting on the wrong client/year/department (Step verification).</summary>
    private void VerifyContext(ExportJob job)
    {
        if (_selectedClient != null && _selectedClient != job.Client)
            throw new WilkenException("WRONG_CLIENT", $"Selected client {_selectedClient} does not match job client {job.Client}.");
        if (_fiscalYear != null && _fiscalYear != job.FiscalYear)
            throw new WilkenException("WRONG_YEAR", $"Selected year {_fiscalYear} does not match job year {job.FiscalYear}.");
        if (_department != null && _department != job.Department)
            throw new WilkenException("WRONG_DEPARTMENT", $"Selected department {_department} does not match job department {job.Department}.");
    }

    private TimeSpan StepDelay() =>
        TimeSpan.FromMilliseconds(_jobSeconds > 0 ? _jobSeconds * 30 + _random.Next(120) : 80 + _random.Next(150));
}

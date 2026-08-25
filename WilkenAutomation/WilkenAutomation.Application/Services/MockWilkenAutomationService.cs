using System.Text;
using Microsoft.Extensions.Logging;
using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Application.Validators;

namespace WilkenAutomation.Application.Services;

/// <summary>
/// Full simulation of the Wilken CS/2 desktop workflow (AutomationMode=Mock).
/// Walks through the same states as the real automation, produces a real export
/// file on disk, and injects configurable random failures, session crashes and
/// legitimately empty periods so retry/recovery/validation paths are exercised
/// without Wilken being available.
/// </summary>
public class MockWilkenAutomationService : IWilkenAutomationService
{
    private readonly ILogger<MockWilkenAutomationService> _logger;
    private readonly Random _random = new();
    private readonly string _tempDirectory;

    private ExportJob? _job;
    private RunConfig _config = new();
    private double _stepSeconds;
    private bool _crashed;

    public WilkenSessionStatus SessionStatus { get; private set; } = WilkenSessionStatus.NotRunning;

    public MockWilkenAutomationService(ILogger<MockWilkenAutomationService> logger, string? tempDirectory = null)
    {
        _logger = logger;
        _tempDirectory = tempDirectory ?? Path.Combine(Path.GetTempPath(), "WilkenAutomationMock");
        Directory.CreateDirectory(_tempDirectory);
    }

    public Task BeginJobAsync(ExportJob job, RunConfig config, CancellationToken ct)
    {
        _job = job;
        _config = config;
        var sim = config.Simulation;
        var total = sim.MinJobSeconds + _random.NextDouble() * Math.Max(0, sim.MaxJobSeconds - sim.MinJobSeconds);
        _stepSeconds = Math.Max(0.05, total / 8.0); // ~8 workflow steps share the simulated duration
        return Task.CompletedTask;
    }

    public async Task EnsureSessionAsync(CancellationToken ct)
    {
        if (SessionStatus == WilkenSessionStatus.Ready) return;
        SessionStatus = WilkenSessionStatus.Starting;
        await Step(ct);
        SessionStatus = WilkenSessionStatus.Ready;
        _crashed = false;
    }

    public async Task SelectClientAsync(string client, CancellationToken ct)
    {
        await SimulatedAction("SelectClient", ct);
    }

    public async Task OpenAssetAccountingAsync(CancellationToken ct)
    {
        await SimulatedAction("OpenAssetAccounting", ct);
    }

    public Task CaptureSpoolSnapshotAsync(CancellationToken ct) => Task.CompletedTask;

    public async Task OpenExportDefinitionAsync(string definitionName, CancellationToken ct)
    {
        await SimulatedAction($"OpenExport:{definitionName}", ct);
    }

    public async Task SetFiscalYearAsync(int fiscalYear, CancellationToken ct)
    {
        await SimulatedAction("SetFiscalYear", ct);
    }

    public async Task SelectDepartmentAsync(string department, CancellationToken ct)
    {
        await SimulatedAction("SelectDepartment", ct);
    }

    public async Task StartEvaluationAsync(CancellationToken ct)
    {
        await SimulatedAction("StartEvaluation", ct);
        SessionStatus = WilkenSessionStatus.Busy;
    }

    public async Task WaitForReportReadyAsync(CancellationToken ct)
    {
        // Report generation is the longest phase; give it a double share.
        await SimulatedAction("WaitForReportReady", ct);
        await Step(ct);
        SessionStatus = WilkenSessionStatus.Ready;
    }

    public async Task OpenSpoolAsync(CancellationToken ct)
    {
        await SimulatedAction("OpenSpool", ct);
        if (_job is not null)
            _job.SpoolId = $"SPL-{_job.ExportDefinition}-{_job.Client}-{Guid.NewGuid():N}"[..32];
    }

    public async Task ReturnToProcessManagerAsync(CancellationToken ct)
    {
        await SimulatedAction("ReturnToProcessManager", ct);
    }

    public async Task<string> ExportAsync(ExportJob job, CancellationToken ct)
    {
        await SimulatedAction("Export", ct);

        var empty = _random.NextDouble() < _config.Simulation.EmptyRate;
        var recordCount = empty ? 0 : _random.Next(5, 250);

        var sb = new StringBuilder();
        sb.AppendLine($"{ExportFileValidator.ReportMarker} V2.4");
        sb.AppendLine($"Client;{job.Client}");
        if (job.FiscalYear > 0) sb.AppendLine($"FiscalYear;{job.FiscalYear}");
        if (!string.IsNullOrWhiteSpace(job.Department)) sb.AppendLine($"Department;{job.Department}");
        if (!string.IsNullOrWhiteSpace(job.ExportDefinition)) sb.AppendLine($"ExportDefinition;{job.ExportDefinition}");
        if (!string.IsNullOrWhiteSpace(job.Period)) sb.AppendLine($"Period;{job.Period}");
        if (!string.IsNullOrWhiteSpace(job.AccountingLaw)) sb.AppendLine($"AccountingLaw;{job.AccountingLaw}");
        sb.AppendLine($"RecordCount;{recordCount}");
        sb.AppendLine($"GeneratedAt;{DateTime.UtcNow:O}");
        sb.AppendLine(ExportFileValidator.DataMarker);
        sb.AppendLine("AssetNo;Description;AcquisitionDate;AcquisitionValue;Depreciation;BookValue");
        for (var i = 1; i <= recordCount; i++)
        {
            var value = Math.Round(_random.NextDouble() * 100000, 2);
            var depreciation = Math.Round(value * _random.NextDouble() * 0.8, 2);
            sb.AppendLine(FormattableString.Invariant(
                $"A{i:D5};Asset {i} ({job.Department});{job.FiscalYear}-{_random.Next(1, 13):D2}-{_random.Next(1, 29):D2};{value};{depreciation};{Math.Round(value - depreciation, 2)}"));
        }
        sb.AppendLine(ExportFileValidator.EndMarker);

        var tempPath = Path.Combine(_tempDirectory, $"{job.JobId}-{Guid.NewGuid():N}.csv");
        await File.WriteAllTextAsync(tempPath, sb.ToString(), ct);
        return tempPath;
    }

    public Task<bool> IsSessionHealthyAsync(CancellationToken ct) =>
        Task.FromResult(!_crashed && SessionStatus is WilkenSessionStatus.Ready or WilkenSessionStatus.Busy);

    public async Task RecoverSessionAsync(CancellationToken ct)
    {
        SessionStatus = WilkenSessionStatus.Recovering;
        _logger.LogWarning("Mock Wilken session recovery: closing broken session and restarting.");
        await Step(ct);
        SessionStatus = WilkenSessionStatus.Ready;
        _crashed = false;
    }

    private async Task SimulatedAction(string action, CancellationToken ct)
    {
        if (_crashed)
            throw new WilkenAutomationException("SESSION_DOWN", "Wilken session is down and must be recovered first.", sessionLost: true);

        await Step(ct);
        var sim = _config.Simulation;

        if (_random.NextDouble() < sim.CrashRate)
        {
            _crashed = true;
            SessionStatus = WilkenSessionStatus.NotResponding;
            throw new WilkenAutomationException("WILKEN_CRASH",
                $"Simulated Wilken crash during {action} (process stopped responding).", sessionLost: true);
        }

        if (_random.NextDouble() < sim.FailureRate)
        {
            throw new WilkenAutomationException($"MOCK_{EnumWire.ToUpperSnake(action)}_FAILED",
                $"Simulated transient failure during {action} (e.g. unexpected dialog).");
        }
    }

    private Task Step(CancellationToken ct) =>
        Task.Delay(TimeSpan.FromSeconds(_stepSeconds), ct);
}

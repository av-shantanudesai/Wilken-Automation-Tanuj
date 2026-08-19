using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WilkenAutomation.Application.Configuration;
using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Application.Services;
using WilkenAutomation.Application.Validators;
using WilkenAutomation.Infrastructure.Database;
using WilkenAutomation.Infrastructure.FileSystem;
using WilkenAutomation.Infrastructure.Repositories;

namespace WilkenAutomation.Tests;

/// <summary>Shared wiring: in-memory database, real repositories, mock automation.</summary>
public sealed class TestContext : IDisposable
{
    public AutomationDbContext Db { get; }
    public RunRepository Runs { get; }
    public JobRepository Jobs { get; }
    public LogRepository Logs { get; }
    public ChecksumService Checksum { get; } = new();
    public ExportFileValidator Validator { get; } = new();
    public CapturingNotifier Notifier { get; } = new();
    public FakeScreenshotService Screenshots { get; } = new();
    public string WorkDirectory { get; }
    public ExportSettings ExportSettings { get; }
    public WilkenOptions WilkenOptions { get; } = new() { FileCreationTimeoutSeconds = 5, PollingIntervalMs = 20 };

    public TestContext()
    {
        var options = new DbContextOptionsBuilder<AutomationDbContext>()
            .UseInMemoryDatabase($"tests-{Guid.NewGuid():N}")
            .Options;
        Db = new AutomationDbContext(options);
        Runs = new RunRepository(Db);
        Jobs = new JobRepository(Db);
        Logs = new LogRepository(Db);

        WorkDirectory = Path.Combine(Path.GetTempPath(), $"wilken-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(WorkDirectory);
        ExportSettings = new ExportSettings
        {
            RootDirectory = Path.Combine(WorkDirectory, "Exports"),
            ScreenshotDirectory = Path.Combine(WorkDirectory, "Screenshots")
        };
    }

    public JobGeneratorService Generator(RunDefaults? defaults = null) =>
        new(Runs, defaults ?? new RunDefaults());

    public JobExecutor Executor(IWilkenAutomationService automation) =>
        new(Jobs, Runs, Logs, automation, Validator, Checksum, Screenshots, Notifier,
            ExportSettings, WilkenOptions, NullLogger<JobExecutor>.Instance);

    public MockWilkenAutomationService MockAutomation() =>
        new(NullLogger<MockWilkenAutomationService>.Instance, Path.Combine(WorkDirectory, "MockTemp"));

    public StartupRecoveryService Recovery() =>
        new(Jobs, Runs, Logs, Checksum, NullLogger<StartupRecoveryService>.Instance);

    public void Dispose()
    {
        Db.Dispose();
        try { Directory.Delete(WorkDirectory, recursive: true); } catch { /* best effort */ }
    }
}

public class CapturingNotifier : IRealtimeNotifier
{
    public List<(string Event, object Payload)> Events { get; } = new();

    public Task PublishAsync(string eventName, object payload, CancellationToken ct = default, long? audienceUserId = null, string? runId = null)
    {
        Events.Add((eventName, payload));
        return Task.CompletedTask;
    }

    public Task PublishWorkerStatusAsync(WorkerStatusDto status, CancellationToken ct = default, long? audienceUserId = null) =>
        Task.CompletedTask;
}

public class FakeScreenshotService : IScreenshotService
{
    public int Captures { get; private set; }

    public Task<string?> CaptureAsync(string runId, string jobId, int attempt, string label, CancellationToken ct)
    {
        Captures++;
        return Task.FromResult<string?>($"screenshots/{runId}/{jobId}/attempt-{attempt}.png");
    }
}

/// <summary>Automation stub that always fails at a chosen step.</summary>
public class AlwaysFailingAutomation : IWilkenAutomationService
{
    private readonly bool _sessionLost;
    public WilkenSessionStatus SessionStatus => WilkenSessionStatus.Ready;

    public AlwaysFailingAutomation(bool sessionLost = false)
    {
        _sessionLost = sessionLost;
    }

    public Task BeginJobAsync(ExportJob job, RunConfig config, CancellationToken ct) => Task.CompletedTask;
    public Task EnsureSessionAsync(CancellationToken ct) => Task.CompletedTask;

    public Task SelectClientAsync(string client, CancellationToken ct) =>
        throw new WilkenAutomationException("TEST_FAILURE", "Simulated deterministic failure.", _sessionLost);

    public Task OpenAssetAccountingAsync(CancellationToken ct) => Task.CompletedTask;
    public Task SetFiscalYearAsync(int fiscalYear, CancellationToken ct) => Task.CompletedTask;
    public Task SelectDepartmentAsync(string department, CancellationToken ct) => Task.CompletedTask;
    public Task StartEvaluationAsync(CancellationToken ct) => Task.CompletedTask;
    public Task WaitForReportReadyAsync(CancellationToken ct) => Task.CompletedTask;
    public Task OpenSpoolAsync(CancellationToken ct) => Task.CompletedTask;
    public Task<string> ExportAsync(ExportJob job, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> IsSessionHealthyAsync(CancellationToken ct) => Task.FromResult(true);
    public Task RecoverSessionAsync(CancellationToken ct) => Task.CompletedTask;
}

/// <summary>Simulates the user closing Wilken mid-step: wait times out and the session is gone.</summary>
public class TimeoutUnhealthyAutomation : IWilkenAutomationService
{
    public WilkenSessionStatus SessionStatus => WilkenSessionStatus.NotRunning;

    public Task BeginJobAsync(ExportJob job, RunConfig config, CancellationToken ct) => Task.CompletedTask;
    public Task EnsureSessionAsync(CancellationToken ct) => Task.CompletedTask;
    public Task SelectClientAsync(string client, CancellationToken ct) =>
        throw new WaitTimeoutException("Timed out after 30s waiting for: client selection");
    public Task OpenAssetAccountingAsync(CancellationToken ct) => Task.CompletedTask;
    public Task SetFiscalYearAsync(int fiscalYear, CancellationToken ct) => Task.CompletedTask;
    public Task SelectDepartmentAsync(string department, CancellationToken ct) => Task.CompletedTask;
    public Task StartEvaluationAsync(CancellationToken ct) => Task.CompletedTask;
    public Task WaitForReportReadyAsync(CancellationToken ct) => Task.CompletedTask;
    public Task OpenSpoolAsync(CancellationToken ct) => Task.CompletedTask;
    public Task<string> ExportAsync(ExportJob job, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> IsSessionHealthyAsync(CancellationToken ct) => Task.FromResult(false);
    public Task RecoverSessionAsync(CancellationToken ct) => Task.CompletedTask;
}

public static class TestData
{
    public static CreateRunRequestDto SmallRunRequest(int clients = 2, int years = 2) => new()
    {
        ClientCount = clients,
        YearFrom = 2003,
        YearTo = 2003 + years - 1,
        MaxAttempts = 3,
        Simulation = new SimulationConfig
        {
            MinJobSeconds = 0.01,
            MaxJobSeconds = 0.05,
            FailureRate = 0,
            CrashRate = 0,
            EmptyRate = 0
        }
    };
}

using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Tests;

public class RetryTests : IDisposable
{
    private readonly TestContext _ctx = new();

    [Fact]
    public async Task ThreeFailedAttempts_LeadTo_FailedFinal_WithoutStoppingOthers()
    {
        var generator = _ctx.Generator();
        var config = generator.BuildConfig(TestData.SmallRunRequest(1, 1));
        var run = await generator.GenerateRunAsync(config, null, true, CancellationToken.None, 1);
        var executor = _ctx.Executor(new AlwaysFailingAutomation());

        var job = await _ctx.Jobs.GetNextEligibleAsync(run.RunId, CancellationToken.None);
        Assert.NotNull(job);

        for (var attempt = 1; attempt <= config.MaxAttempts; attempt++)
        {
            var result = await executor.ExecuteAsync(job!, config, CancellationToken.None);
            Assert.Equal(attempt < config.MaxAttempts ? JobStatus.Retry : JobStatus.FailedFinal, result.FinalStatus);
        }

        Assert.Equal(JobStatus.FailedFinal, job!.Status);
        Assert.Equal(3, job.AttemptCount);
        Assert.Equal("TEST_FAILURE", job.ErrorCode);
        Assert.Equal(3, (await _ctx.Jobs.GetAttemptsAsync(job.JobId, CancellationToken.None)).Count);
        Assert.Equal(3, _ctx.Screenshots.Captures);

        // Other jobs of the run remain eligible - the queue is not blocked.
        var next = await _ctx.Jobs.GetNextEligibleAsync(run.RunId, CancellationToken.None);
        Assert.NotNull(next);
        Assert.NotEqual(job.JobId, next!.JobId);
    }

    public void Dispose() => _ctx.Dispose();
}

public class RestartRecoveryTests : IDisposable
{
    private readonly TestContext _ctx = new();

    [Fact]
    public async Task StaleRunningJob_IsMovedToRetry_AndAttemptMarkedInterrupted()
    {
        var generator = _ctx.Generator();
        var config = generator.BuildConfig(TestData.SmallRunRequest(1, 1));
        var run = await generator.GenerateRunAsync(config, null, true, CancellationToken.None, 1);

        // Simulate a crash mid-attempt: job RUNNING with an open attempt.
        var job = (await _ctx.Jobs.GetAllForRunAsync(run.RunId, CancellationToken.None)).First();
        job.Status = JobStatus.Running;
        job.AttemptCount = 1;
        job.StartTime = DateTime.UtcNow.AddMinutes(-5);
        await _ctx.Jobs.UpdateAsync(job, CancellationToken.None);
        await _ctx.Jobs.AddAttemptAsync(new JobAttempt
        {
            JobId = job.JobId, AttemptNumber = 1, StartTime = job.StartTime.Value,
            Status = "Running", CreatedAt = DateTime.UtcNow
        }, CancellationToken.None);

        var recovered = await _ctx.Recovery().RecoverStaleRunningJobsAsync(CancellationToken.None);

        Assert.Equal(1, recovered);
        Assert.Equal(JobStatus.Retry, job.Status);
        Assert.Equal("INTERRUPTED", job.ErrorCode);
        var attempt = (await _ctx.Jobs.GetAttemptsAsync(job.JobId, CancellationToken.None)).Single();
        Assert.Equal("Interrupted", attempt.Status);
    }

    [Fact]
    public async Task SuccessfulJob_WithMissingFile_IsRequeued_OnVerification()
    {
        var generator = _ctx.Generator();
        var config = generator.BuildConfig(TestData.SmallRunRequest(1, 1));
        var run = await generator.GenerateRunAsync(config, null, true, CancellationToken.None, 1);

        var jobs = await _ctx.Jobs.GetAllForRunAsync(run.RunId, CancellationToken.None);
        var missing = jobs[0];
        missing.Status = JobStatus.SuccessWithData;
        missing.FilePath = Path.Combine(_ctx.WorkDirectory, "deleted.csv");
        await _ctx.Jobs.UpdateAsync(missing, CancellationToken.None);

        // A verifiable good job stays untouched.
        var good = jobs[1];
        var goodPath = Path.Combine(_ctx.WorkDirectory, "good.csv");
        await File.WriteAllTextAsync(goodPath, "content");
        good.Status = JobStatus.SuccessWithData;
        good.FilePath = goodPath;
        good.Sha256 = await _ctx.Checksum.ComputeSha256Async(goodPath, CancellationToken.None);
        await _ctx.Jobs.UpdateAsync(good, CancellationToken.None);

        var requeued = await _ctx.Recovery().VerifyCompletedJobsAsync(run.RunId, CancellationToken.None);

        Assert.Equal(1, requeued);
        Assert.Equal(JobStatus.Pending, missing.Status);
        Assert.Equal(JobStatus.SuccessWithData, good.Status);
    }

    [Fact]
    public async Task SuccessfulJob_WithTamperedFile_IsRequeued()
    {
        var generator = _ctx.Generator();
        var config = generator.BuildConfig(TestData.SmallRunRequest(1, 1));
        var run = await generator.GenerateRunAsync(config, null, true, CancellationToken.None, 1);

        var job = (await _ctx.Jobs.GetAllForRunAsync(run.RunId, CancellationToken.None)).First();
        var path = Path.Combine(_ctx.WorkDirectory, "tampered.csv");
        await File.WriteAllTextAsync(path, "original");
        job.Status = JobStatus.SuccessWithData;
        job.FilePath = path;
        job.Sha256 = await _ctx.Checksum.ComputeSha256Async(path, CancellationToken.None);
        await _ctx.Jobs.UpdateAsync(job, CancellationToken.None);

        await File.WriteAllTextAsync(path, "modified after validation");

        var requeued = await _ctx.Recovery().VerifyCompletedJobsAsync(run.RunId, CancellationToken.None);
        Assert.Equal(1, requeued);
        Assert.Equal(JobStatus.Pending, job.Status);
    }

    public void Dispose() => _ctx.Dispose();
}

public class MockLifecycleTests : IDisposable
{
    private readonly TestContext _ctx = new();

    [Fact]
    public async Task CompleteJobLifecycle_SucceedsWithData_PersistsFileChecksumAndEvents()
    {
        var generator = _ctx.Generator();
        var config = generator.BuildConfig(TestData.SmallRunRequest(1, 1));
        var run = await generator.GenerateRunAsync(config, null, true, CancellationToken.None, 1);
        var executor = _ctx.Executor(_ctx.MockAutomation());

        var job = await _ctx.Jobs.GetNextEligibleAsync(run.RunId, CancellationToken.None);
        var result = await executor.ExecuteAsync(job!, config, CancellationToken.None);

        Assert.Equal(JobStatus.SuccessWithData, result.FinalStatus);
        Assert.Equal(ValidationStatus.Valid, job!.ValidationStatus);
        Assert.True(job.RecordCount > 0);
        Assert.True(File.Exists(job.FilePath));
        Assert.Equal(64, job.Sha256!.Length);
        Assert.Equal(await _ctx.Checksum.ComputeSha256Async(job.FilePath!, CancellationToken.None), job.Sha256);
        Assert.True(job.DurationMs >= 0);
        Assert.Equal("COMPLETED", job.ApplicationState);

        // Persistence-first eventing: started, state changes, completed.
        var events = _ctx.Notifier.Events.Select(e => e.Event).ToList();
        Assert.Contains(SignalREvents.JobStarted, events);
        Assert.Contains(SignalREvents.JobApplicationStateChanged, events);
        Assert.Contains(SignalREvents.JobCompleted, events);
        Assert.Contains(SignalREvents.RunProgressChanged, events);

        // Run counters were refreshed transactionally.
        var counts = await _ctx.Runs.GetCountsAsync(run.RunId, CancellationToken.None);
        Assert.Equal(1, counts.SuccessWithData);
    }

    [Fact]
    public async Task EmptyPeriod_ResultsIn_SuccessEmpty()
    {
        var generator = _ctx.Generator();
        var request = TestData.SmallRunRequest(1, 1);
        request.Simulation!.EmptyRate = 1.0;
        var config = generator.BuildConfig(request);
        var run = await generator.GenerateRunAsync(config, null, true, CancellationToken.None, 1);

        var job = await _ctx.Jobs.GetNextEligibleAsync(run.RunId, CancellationToken.None);
        var result = await _ctx.Executor(_ctx.MockAutomation()).ExecuteAsync(job!, config, CancellationToken.None);

        Assert.Equal(JobStatus.SuccessEmpty, result.FinalStatus);
        Assert.Equal(ValidationStatus.ValidEmpty, job!.ValidationStatus);
        Assert.Equal(0, job.RecordCount);
    }

    [Fact]
    public async Task SessionCrash_ReportsSessionLost_ForWorkerRecovery()
    {
        var generator = _ctx.Generator();
        var config = generator.BuildConfig(TestData.SmallRunRequest(1, 1));
        var run = await generator.GenerateRunAsync(config, null, true, CancellationToken.None, 1);

        var job = await _ctx.Jobs.GetNextEligibleAsync(run.RunId, CancellationToken.None);
        var result = await _ctx.Executor(new AlwaysFailingAutomation(sessionLost: true))
            .ExecuteAsync(job!, config, CancellationToken.None);

        Assert.True(result.SessionLost);
        Assert.Equal(JobStatus.Retry, result.FinalStatus);
    }

    [Fact]
    public async Task TimeoutWhenDesktopGone_IsTreatedAsSessionLost()
    {
        var generator = _ctx.Generator();
        var config = generator.BuildConfig(TestData.SmallRunRequest(1, 1));
        var run = await generator.GenerateRunAsync(config, null, true, CancellationToken.None, 1);

        var job = await _ctx.Jobs.GetNextEligibleAsync(run.RunId, CancellationToken.None);
        var result = await _ctx.Executor(new TimeoutUnhealthyAutomation())
            .ExecuteAsync(job!, config, CancellationToken.None);

        Assert.True(result.SessionLost);
        Assert.Equal(JobStatus.Retry, result.FinalStatus);
        Assert.Equal("TIMEOUT", job!.ErrorCode);
    }

    public void Dispose() => _ctx.Dispose();
}

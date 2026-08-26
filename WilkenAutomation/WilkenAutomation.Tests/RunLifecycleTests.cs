using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Tests;

public class RunLifecycleTests : IDisposable
{
    private readonly TestContext _ctx = new();

    [Fact]
    public async Task Requeue_ReturnsOwnedFailedJob_ToPending()
    {
        var generator = _ctx.Generator();
        var config = generator.BuildConfig(TestData.SmallRunRequest(1, 1));
        var run = await generator.GenerateRunAsync(config, null, true, CancellationToken.None, 1);
        var job = (await _ctx.Jobs.GetAllForRunAsync(run.RunId, CancellationToken.None)).First();
        job.Status = JobStatus.FailedFinal;
        job.ErrorCode = "TEST";
        await _ctx.Jobs.UpdateAsync(job, CancellationToken.None);

        var lifecycle = new RunLifecycleService(_ctx.Runs, _ctx.Jobs, _ctx.Notifier);
        var requeued = await lifecycle.RequeueJobAsync(job.JobId, run.UserId, CancellationToken.None);

        Assert.NotNull(requeued);
        Assert.Equal(JobStatus.Pending, requeued!.Status);
        Assert.Null(requeued.ErrorCode);
        Assert.Contains(_ctx.Notifier.Events, e => e.Event == SignalREvents.JobStatusChanged);
    }

    [Fact]
    public async Task Requeue_ForOtherUser_ReturnsNull()
    {
        var generator = _ctx.Generator();
        var config = generator.BuildConfig(TestData.SmallRunRequest(1, 1));
        var run = await generator.GenerateRunAsync(config, null, true, CancellationToken.None, 1);
        var job = (await _ctx.Jobs.GetAllForRunAsync(run.RunId, CancellationToken.None)).First();
        job.Status = JobStatus.FailedFinal;
        await _ctx.Jobs.UpdateAsync(job, CancellationToken.None);

        var lifecycle = new RunLifecycleService(_ctx.Runs, _ctx.Jobs, _ctx.Notifier);
        Assert.Null(await lifecycle.RequeueJobAsync(job.JobId, userId: 99, CancellationToken.None));
    }

    [Fact]
    public async Task RetryFailed_RequeuesAllFailedFinal_InOnePass()
    {
        var generator = _ctx.Generator();
        var config = generator.BuildConfig(TestData.SmallRunRequest(2, 1));
        var run = await generator.GenerateRunAsync(config, null, false, CancellationToken.None, 1);
        var jobs = await _ctx.Jobs.GetAllForRunAsync(run.RunId, CancellationToken.None);
        foreach (var job in jobs)
        {
            job.Status = JobStatus.FailedFinal;
            await _ctx.Jobs.UpdateAsync(job, CancellationToken.None);
        }

        var lifecycle = new RunLifecycleService(_ctx.Runs, _ctx.Jobs, _ctx.Notifier);
        var updated = await lifecycle.RetryFailedAsync(run.RunId, run.UserId, CancellationToken.None);
        Assert.NotNull(updated);

        var pending = await _ctx.Jobs.GetAllForRunAsync(run.RunId, CancellationToken.None);
        Assert.All(pending, j => Assert.Equal(JobStatus.Pending, j.Status));
    }

    [Fact]
    public async Task SecondClaim_DoesNotReturnTheSameJob()
    {
        var generator = _ctx.Generator();
        var config = generator.BuildConfig(TestData.SmallRunRequest(2, 1));
        var run = await generator.GenerateRunAsync(config, null, true, CancellationToken.None, 1);

        var first = await _ctx.Jobs.ClaimNextEligibleAsync(run.RunId, CancellationToken.None);
        var second = await _ctx.Jobs.ClaimNextEligibleAsync(run.RunId, CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first!.JobId, second!.JobId);
        Assert.Equal(JobStatus.Running, first.Status);
        Assert.Equal(JobStatus.Running, second.Status);
    }

    public void Dispose() => _ctx.Dispose();
}

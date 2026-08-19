using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Models;

namespace WilkenAutomation.Application.Services;

/// <summary>
/// Single place for run counter math so RunRepository and JobRepository cannot drift.
/// </summary>
public static class RunCounters
{
    public static StatusCountsDto FromGroups(IEnumerable<(JobStatus Status, int Count)> groups)
    {
        var list = groups.ToList();
        int Of(JobStatus status) => list.Where(g => g.Status == status).Select(g => g.Count).FirstOrDefault();
        return new StatusCountsDto(
            list.Sum(g => g.Count),
            Of(JobStatus.Pending),
            Of(JobStatus.Running),
            Of(JobStatus.Retry),
            Of(JobStatus.SuccessWithData),
            Of(JobStatus.SuccessEmpty),
            Of(JobStatus.FailedFinal));
    }

    public static void ApplyToRun(AutomationRun run, StatusCountsDto counts)
    {
        run.TotalJobs = counts.Total;
        run.SuccessfulWithData = counts.SuccessWithData;
        run.SuccessfulEmpty = counts.SuccessEmpty;
        run.FailedJobs = counts.FailedFinal;
        run.PendingJobs = counts.Pending + counts.Retry;
        run.CompletedJobs = counts.Terminal;
        run.UpdatedAt = DateTime.UtcNow;

        if (run.Status == RunStatus.Running && counts.Total > 0 && counts.Terminal == counts.Total)
        {
            run.Status = RunStatus.Completed;
            run.CompletedAt = DateTime.UtcNow;
        }
    }
}

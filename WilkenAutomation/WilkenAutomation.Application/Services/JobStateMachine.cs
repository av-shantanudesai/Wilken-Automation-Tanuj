using WilkenAutomation.Application.Enums;

namespace WilkenAutomation.Application.Services;

/// <summary>
/// Guards job status transitions so invalid transitions are rejected instead of
/// silently corrupting state.
/// </summary>
public static class JobStateMachine
{
    private static readonly Dictionary<JobStatus, JobStatus[]> Allowed = new()
    {
        [JobStatus.Pending] = new[] { JobStatus.Running },
        [JobStatus.Retry] = new[] { JobStatus.Running, JobStatus.Pending },
        [JobStatus.Running] = new[]
        {
            JobStatus.SuccessWithData, JobStatus.SuccessEmpty,
            JobStatus.Failed, JobStatus.Retry, JobStatus.FailedFinal
        },
        // Transient attempt outcome; persisted as RETRY or FAILED_FINAL.
        [JobStatus.Failed] = new[] { JobStatus.Retry, JobStatus.FailedFinal },
        // Terminal states can only be re-opened deliberately (operator requeue /
        // failed re-verification of the export file).
        [JobStatus.FailedFinal] = new[] { JobStatus.Pending },
        [JobStatus.SuccessWithData] = new[] { JobStatus.Pending },
        [JobStatus.SuccessEmpty] = new[] { JobStatus.Pending }
    };

    public static bool CanTransition(JobStatus from, JobStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    public static void EnsureTransition(JobStatus from, JobStatus to)
    {
        if (!CanTransition(from, to))
            throw new InvalidOperationException($"Invalid job status transition {from} -> {to}.");
    }
}

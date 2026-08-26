using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;

namespace WilkenAutomation.Application.Services;

/// <summary>
/// Progress / ETA math based on actual completed-job runtimes,
/// never on a fixed per-job assumption.
/// </summary>
public class RunStatisticsService
{
    private readonly IJobRepository _jobs;

    public RunStatisticsService(IJobRepository jobs)
    {
        _jobs = jobs;
    }

    public static double ProgressPercent(StatusCountsDto counts) =>
        counts.Total == 0 ? 0 : Math.Round(counts.Terminal * 100.0 / counts.Total, 2);

    public static double? EstimateRemainingMs(double? averageMs, int openJobs) =>
        averageMs is null or <= 0 ? null : averageMs * openJobs;

    public async Task<(double ProgressPercent, double? AverageMs, double? RemainingMs)> ComputeAsync(
        string runId, StatusCountsDto counts, CancellationToken ct)
    {
        var (averageMs, _) = await _jobs.GetRuntimeStatsAsync(runId, ct);
        return (ProgressPercent(counts), averageMs, EstimateRemainingMs(averageMs, counts.Open));
    }
}

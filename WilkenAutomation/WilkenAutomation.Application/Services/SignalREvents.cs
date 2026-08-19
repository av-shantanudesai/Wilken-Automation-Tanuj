namespace WilkenAutomation.Application.Services;

/// <summary>Event names broadcast on the /hubs/job-monitoring SignalR hub.</summary>
public static class SignalREvents
{
    public const string JobStarted = "JobStarted";
    public const string JobStatusChanged = "JobStatusChanged";
    public const string JobApplicationStateChanged = "JobApplicationStateChanged";
    public const string JobCompleted = "JobCompleted";
    public const string JobFailed = "JobFailed";
    public const string JobRetrying = "JobRetrying";
    public const string RunProgressChanged = "RunProgressChanged";
    public const string DashboardSummaryChanged = "DashboardSummaryChanged";
    public const string WorkerStatusChanged = "WorkerStatusChanged";
    public const string WilkenSessionChanged = "WilkenSessionChanged";
    public const string LastErrorChanged = "LastErrorChanged";
    public const string LastSuccessChanged = "LastSuccessChanged";
    public const string RunsChanged = "RunsChanged";

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        JobStarted, JobStatusChanged, JobApplicationStateChanged, JobCompleted, JobFailed,
        JobRetrying, RunProgressChanged, DashboardSummaryChanged, WorkerStatusChanged,
        WilkenSessionChanged, LastErrorChanged, LastSuccessChanged, RunsChanged
    };

    public static bool IsKnown(string? eventName) =>
        !string.IsNullOrWhiteSpace(eventName) && Known.Contains(eventName);
}

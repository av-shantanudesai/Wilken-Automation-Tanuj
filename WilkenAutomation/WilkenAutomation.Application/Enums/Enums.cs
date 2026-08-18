namespace WilkenAutomation.Application.Enums;

public enum RunStatus
{
    Created,
    Running,
    Paused,
    Completed
}

/// <summary>
/// Serialized as enum names ("SuccessWithData", ...) to stay compatible with the
/// existing Angular frontend models (see frontend/src/app/core/models.ts).
/// Conceptually these map to PENDING/RUNNING/RETRY/SUCCESS_WITH_DATA/SUCCESS_EMPTY/
/// FAILED/FAILED_FINAL from the specification.
/// </summary>
public enum JobStatus
{
    Pending,
    Running,
    Retry,
    SuccessWithData,
    SuccessEmpty,
    Failed,
    FailedFinal
}

public enum ValidationStatus
{
    NotValidated,
    Valid,
    ValidEmpty,
    Invalid
}

public enum AutomationMode
{
    Mock,
    Wilken
}

public enum WorkerStatus
{
    Stopped,
    Starting,
    Idle,
    Running,
    Recovering,
    Error
}

public enum WilkenSessionStatus
{
    NotRunning,
    Starting,
    LoginRequired,
    Ready,
    Busy,
    NotResponding,
    Recovering,
    Error
}

/// <summary>
/// Fine-grained application state, tracked independently of the job status
/// and streamed to the dashboard. Wire format is UPPER_SNAKE (see ToWireName).
/// </summary>
public enum ApplicationState
{
    Idle,
    StartingWilken,
    LoggingIn,
    SelectingClient,
    OpeningAssetAccounting,
    SettingYear,
    SelectingDepartment,
    StartingReport,
    WaitingForReport,
    OpeningSpool,
    Exporting,
    WaitingForFile,
    ValidatingFile,
    CalculatingHash,
    Completed,
    RecoveringSession
}

public static class EnumWire
{
    /// <summary>PascalCase -> UPPER_SNAKE (e.g. WaitingForReport -> WAITING_FOR_REPORT).</summary>
    public static string ToWireName(this ApplicationState state) => ToUpperSnake(state.ToString());
    public static string ToWireName(this WorkerStatus status) => ToUpperSnake(status.ToString());

    public static string ToUpperSnake(string pascal) =>
        string.Concat(pascal.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + c : c.ToString())).ToUpperInvariant();

    public static bool IsTerminal(this JobStatus status) =>
        status is JobStatus.SuccessWithData or JobStatus.SuccessEmpty or JobStatus.FailedFinal;

    public static bool IsSuccess(this JobStatus status) =>
        status is JobStatus.SuccessWithData or JobStatus.SuccessEmpty;
}

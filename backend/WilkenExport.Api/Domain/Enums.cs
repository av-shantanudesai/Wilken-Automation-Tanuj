namespace WilkenExport.Api.Domain;

public enum RunStatus
{
    Created,
    Running,
    Paused,
    Completed
}

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

public enum ValidationOutcome
{
    NotValidated,
    Valid,
    ValidEmpty,
    Invalid
}

public static class JobStatusExtensions
{
    public static bool IsTerminal(this JobStatus status) =>
        status is JobStatus.SuccessWithData or JobStatus.SuccessEmpty or JobStatus.FailedFinal;

    public static bool IsSuccess(this JobStatus status) =>
        status is JobStatus.SuccessWithData or JobStatus.SuccessEmpty;
}

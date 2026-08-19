using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Models;

namespace WilkenAutomation.Application.Interfaces;

/// <summary>
/// Real-time notification sink. The database is always updated first; this only
/// broadcasts. Implementations: SignalR hub context (API) and SignalR client (worker).
/// Failures to notify must never fail a job.
/// </summary>
public interface IRealtimeNotifier
{
    /// <param name="audienceUserId">When set, only that dashboard user receives the event.</param>
    Task PublishAsync(string eventName, object payload, CancellationToken ct = default, long? audienceUserId = null);
    Task PublishWorkerStatusAsync(WorkerStatusDto status, CancellationToken ct = default);
}

public record FileValidationResult(ValidationStatus Status, int? RecordCount, string Detail);

public interface IExportFileValidator
{
    Task<FileValidationResult> ValidateAsync(string filePath, ExportJob job, bool contentValidation, CancellationToken ct);
}

public interface IChecksumService
{
    Task<string> ComputeSha256Async(string filePath, CancellationToken ct);
}

public interface IScreenshotService
{
    /// <summary>Captures failure evidence; returns the stored path or null when capture failed.</summary>
    Task<string?> CaptureAsync(string runId, string jobId, int attempt, string label, CancellationToken ct);
}

public record WilkenCredentials(string Username, string Password);

/// <summary>
/// Credential abstraction: configuration/environment/secret-store backed.
/// Credentials must never appear in source code, API responses or logs.
/// </summary>
public interface IWilkenCredentialProvider
{
    Task<WilkenCredentials?> GetCredentialsAsync(CancellationToken ct);
}

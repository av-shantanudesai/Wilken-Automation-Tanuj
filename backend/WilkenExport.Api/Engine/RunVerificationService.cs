using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WilkenExport.Api.Configuration;
using WilkenExport.Api.Data;
using WilkenExport.Api.Domain;

namespace WilkenExport.Api.Engine;

/// <summary>
/// Idempotency guard used when a run is (re)started: successfully completed jobs are
/// only skipped if their output file still exists and its checksum still matches.
/// Anything that fails re-verification is transparently re-queued.
/// </summary>
public class RunVerificationService
{
    private readonly ExportDbContext _db;
    private readonly ChecksumService _checksum;

    public RunVerificationService(ExportDbContext db, ChecksumService checksum)
    {
        _db = db;
        _checksum = checksum;
    }

    public async Task<int> VerifyCompletedJobsAsync(ExportRun run, CancellationToken ct)
    {
        var config = JsonSerializer.Deserialize<RunConfig>(run.ConfigJson) ?? new RunConfig();

        var completed = await _db.Jobs
            .Where(j => j.RunId == run.Id &&
                        (j.Status == JobStatus.SuccessWithData || j.Status == JobStatus.SuccessEmpty))
            .ToListAsync(ct);

        var requeued = 0;
        foreach (var job in completed)
        {
            string? reason = null;

            if (string.IsNullOrEmpty(job.FilePath) || !File.Exists(job.FilePath))
                reason = "Previously validated export file is missing.";
            else if (config.EnableChecksum && !string.IsNullOrEmpty(job.Sha256))
            {
                var actual = await _checksum.ComputeSha256Async(job.FilePath, ct);
                if (!string.Equals(actual, job.Sha256, StringComparison.OrdinalIgnoreCase))
                    reason = "Checksum of archived export no longer matches - file was modified or corrupted.";
            }

            if (reason == null) continue;

            job.Status = JobStatus.Pending;
            job.AttemptCount = 0;
            job.ErrorCode = "REVERIFICATION_FAILED";
            job.ErrorMessage = reason;
            job.UpdatedAt = DateTime.UtcNow;
            requeued++;

            _db.Logs.Add(new JobLogEntry
            {
                Timestamp = DateTime.UtcNow,
                RunId = run.Id,
                JobId = job.Id,
                Client = job.Client,
                FiscalYear = job.FiscalYear,
                Department = job.Department,
                Level = "WARN",
                Action = "REVERIFY_REQUEUED",
                Message = reason
            });
        }

        if (requeued > 0) await _db.SaveChangesAsync(ct);
        return requeued;
    }
}

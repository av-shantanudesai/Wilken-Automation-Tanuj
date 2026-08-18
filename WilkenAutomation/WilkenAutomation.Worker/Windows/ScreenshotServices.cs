using FlaUI.Core.Capturing;
using WilkenAutomation.Application.Configuration;
using WilkenAutomation.Application.Interfaces;

namespace WilkenAutomation.Worker.Windows;

/// <summary>
/// Captures the physical screen as failure evidence.
/// Layout: /Logs/Screenshots/RUN-xxx/JOB-xxx/attempt-N-<label>.png
/// </summary>
public class ScreenCaptureService : IScreenshotService
{
    private readonly ExportSettings _settings;
    private readonly ILogger<ScreenCaptureService> _logger;

    public ScreenCaptureService(ExportSettings settings, ILogger<ScreenCaptureService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public Task<string?> CaptureAsync(string runId, string jobId, int attempt, string label, CancellationToken ct)
    {
        try
        {
            var directory = Path.Combine(_settings.ScreenshotDirectory, runId, jobId);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"attempt-{attempt}-{Sanitize(label)}.png");
            using var capture = Capture.Screen();
            capture.ToFile(path);
            return Task.FromResult<string?>(Path.GetFullPath(path));
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Screenshot capture failed: {Message}", ex.Message);
            return Task.FromResult<string?>(null);
        }
    }

    private static string Sanitize(string value) =>
        string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}

/// <summary>
/// Mock-mode evidence writer: records a text file instead of a real screenshot,
/// keeping the pipeline (paths persisted on attempts) fully testable headlessly.
/// </summary>
public class TextEvidenceScreenshotService : IScreenshotService
{
    private readonly ExportSettings _settings;

    public TextEvidenceScreenshotService(ExportSettings settings)
    {
        _settings = settings;
    }

    public async Task<string?> CaptureAsync(string runId, string jobId, int attempt, string label, CancellationToken ct)
    {
        var directory = Path.Combine(_settings.ScreenshotDirectory, runId, jobId);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"attempt-{attempt}-error.txt");
        await File.WriteAllTextAsync(path,
            $"Mock screenshot evidence\nTime: {DateTime.UtcNow:O}\nJob: {jobId}\nAttempt: {attempt}\nLabel: {label}\n", ct);
        return Path.GetFullPath(path);
    }
}

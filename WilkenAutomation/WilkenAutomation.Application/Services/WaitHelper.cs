namespace WilkenAutomation.Application.Services;

public class WaitTimeoutException : TimeoutException
{
    public WaitTimeoutException(string message) : base(message) { }
}

/// <summary>
/// Reusable state-based waiting: poll a condition until it holds or a timeout
/// elapses. This is the only sanctioned synchronization mechanism - never
/// Thread.Sleep(fixed large value).
/// </summary>
public static class WaitHelper
{
    public static async Task WaitUntilAsync(
        Func<Task<bool>> condition,
        TimeSpan timeout,
        TimeSpan pollingInterval,
        string description,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await condition()) return;
            if (DateTime.UtcNow >= deadline)
                throw new WaitTimeoutException($"Timed out after {timeout.TotalSeconds:F0}s waiting for: {description}");
            await Task.Delay(pollingInterval, cancellationToken);
        }
    }

    public static Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout,
        TimeSpan pollingInterval,
        string description,
        CancellationToken cancellationToken)
        => WaitUntilAsync(() => Task.FromResult(condition()), timeout, pollingInterval, description, cancellationToken);

    /// <summary>Waits until a file exists, is non-empty, and its size is stable (no longer being written).</summary>
    public static async Task WaitForFileReadyAsync(
        string path, TimeSpan timeout, TimeSpan pollingInterval, CancellationToken ct)
    {
        long lastSize = -1;
        await WaitUntilAsync(() =>
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0) { lastSize = -1; return false; }
            if (info.Length != lastSize) { lastSize = info.Length; return false; }
            try
            {
                using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
        }, timeout, pollingInterval, $"file creation of {Path.GetFileName(path)}", ct);
    }
}

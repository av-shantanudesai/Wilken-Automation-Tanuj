using WilkenAutomation.Application.Interfaces;

namespace WilkenAutomation.Application.Services;

/// <summary>
/// One signal used to recognize a screen: either a stable control
/// (automation id) or a text anchor that must appear in the screen text.
/// </summary>
public sealed class ScreenAnchor
{
    public string? AutomationId { get; init; }
    public string? Text { get; init; }

    public static ScreenAnchor ById(string automationId) => new() { AutomationId = automationId };
    public static ScreenAnchor ByText(string text) => new() { Text = text };
}

/// <summary>
/// A screen is detected from multiple independent signals, never a single
/// element/image. It counts as detected when at least <see cref="MinMatches"/>
/// anchors are present.
/// </summary>
public sealed class ScreenDefinition
{
    public required string Name { get; init; }
    public List<ScreenAnchor> Anchors { get; init; } = new();
    public int MinMatches { get; init; } = 2;
}

/// <summary>
/// UI access supplied by the automation adapter (FlaUI, keyboard, image layer...).
/// The engine itself stays UI-framework agnostic so mock and real Wilken share it.
/// </summary>
public sealed class ScreenProbe
{
    public required Func<string, bool> ElementExists { get; init; }
    public required Func<string> ReadScreenText { get; init; }
}

public sealed record ScreenDetectionResult(ScreenDefinition? Screen, double Confidence, int MatchedAnchors);

/// <summary>
/// One automation step in the Detect Screen -> Act -> Verify Expected State loop.
/// Every step declares its expected state, timeout, and failure handler so
/// recovery is deterministic instead of "keep clicking".
/// </summary>
public sealed class AutomationStep
{
    public required string Name { get; init; }
    public required Action Action { get; init; }
    public required Func<bool> ExpectedState { get; init; }
    public required TimeSpan Timeout { get; init; }
    /// <summary>Invoked before the step error is raised (screenshot, ESC, return to known screen).</summary>
    public Func<CancellationToken, Task>? OnFailure { get; init; }
    public string? FailureErrorCode { get; init; }
}

/// <summary>
/// Reusable screen-state automation layer:
/// detect screen (multi-anchor, confidence-based) -> perform action ->
/// verify expected new state -> continue. No fixed sleeps, no coordinates.
/// </summary>
public class ScreenStateEngine
{
    private readonly ScreenProbe _probe;
    private readonly TimeSpan _pollingInterval;

    public ScreenStateEngine(ScreenProbe probe, TimeSpan? pollingInterval = null)
    {
        _probe = probe;
        _pollingInterval = pollingInterval ?? TimeSpan.FromMilliseconds(250);
    }

    public int CountMatches(ScreenDefinition screen)
    {
        if (screen.Anchors.Count == 0) return 0;
        string? screenText = null;
        var matches = 0;
        foreach (var anchor in screen.Anchors)
        {
            try
            {
                if (!string.IsNullOrEmpty(anchor.AutomationId))
                {
                    if (_probe.ElementExists(anchor.AutomationId)) matches++;
                }
                else if (!string.IsNullOrEmpty(anchor.Text))
                {
                    screenText ??= _probe.ReadScreenText() ?? "";
                    if (screenText.Contains(anchor.Text, StringComparison.OrdinalIgnoreCase)) matches++;
                }
            }
            catch
            {
                // A failing probe just means the anchor is absent right now.
            }
        }
        return matches;
    }

    public double Confidence(ScreenDefinition screen) =>
        screen.Anchors.Count == 0 ? 0 : (double)CountMatches(screen) / screen.Anchors.Count;

    public bool Matches(ScreenDefinition screen) =>
        CountMatches(screen) >= Math.Min(screen.MinMatches, Math.Max(1, screen.Anchors.Count));

    /// <summary>Return the best-matching screen among candidates, or none if no screen reaches its minimum.</summary>
    public ScreenDetectionResult Detect(IEnumerable<ScreenDefinition> screens)
    {
        ScreenDefinition? best = null;
        var bestConfidence = 0.0;
        var bestMatches = 0;
        foreach (var screen in screens)
        {
            var matched = CountMatches(screen);
            if (matched < screen.MinMatches) continue;
            var confidence = screen.Anchors.Count == 0 ? 0 : (double)matched / screen.Anchors.Count;
            if (confidence > bestConfidence)
            {
                best = screen;
                bestConfidence = confidence;
                bestMatches = matched;
            }
        }
        return new ScreenDetectionResult(best, bestConfidence, bestMatches);
    }

    public Task WaitForScreenAsync(ScreenDefinition screen, TimeSpan timeout, CancellationToken ct) =>
        WaitHelper.WaitUntilAsync(() => Matches(screen), timeout, _pollingInterval,
            $"screen '{screen.Name}' ({screen.MinMatches}+ of {screen.Anchors.Count} anchors)", ct);

    public Task WaitUntilGoneAsync(ScreenDefinition screen, TimeSpan timeout, CancellationToken ct) =>
        WaitHelper.WaitUntilAsync(() => !Matches(screen), timeout, _pollingInterval,
            $"screen '{screen.Name}' to disappear", ct);

    /// <summary>
    /// Run one step: action -> wait for expected state -> continue.
    /// On timeout the step's OnFailure hook runs first (diagnostics/safe recovery),
    /// then a structured error is raised so the job-level retry takes over.
    /// </summary>
    public async Task RunStepAsync(AutomationStep step, CancellationToken ct)
    {
        step.Action();
        try
        {
            await WaitHelper.WaitUntilAsync(step.ExpectedState, step.Timeout, _pollingInterval,
                $"expected state after step '{step.Name}'", ct);
        }
        catch (WaitTimeoutException ex)
        {
            if (step.OnFailure is not null)
            {
                try { await step.OnFailure(ct); }
                catch { /* recovery hooks must never mask the original failure */ }
            }
            throw new WilkenAutomationException(
                step.FailureErrorCode ?? "STEP_EXPECTED_STATE_TIMEOUT",
                $"Step '{step.Name}' did not reach its expected state within {step.Timeout.TotalSeconds:0}s.", inner: ex);
        }
    }
}

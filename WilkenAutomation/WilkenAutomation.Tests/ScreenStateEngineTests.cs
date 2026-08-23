using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Services;
using Xunit;

namespace WilkenAutomation.Tests;

public class ScreenStateEngineTests
{
    private static ScreenStateEngine Engine(HashSet<string> presentIds, string screenText) =>
        new(new ScreenProbe
        {
            ElementExists = presentIds.Contains,
            ReadScreenText = () => screenText
        }, TimeSpan.FromMilliseconds(10));

    private static readonly ScreenDefinition SpoolList = new()
    {
        Name = "SPOOL_LIST",
        Anchors =
        {
            ScreenAnchor.ById("Spool_Grid"),
            ScreenAnchor.ById("Spool_Refresh"),
            ScreenAnchor.ByText("Druckauswahl")
        },
        MinMatches = 2
    };

    private static readonly ScreenDefinition ExportScreen = new()
    {
        Name = "GITTERBOX_EXPORT",
        Anchors =
        {
            ScreenAnchor.ByText("Gitterbox-Export"),
            ScreenAnchor.ById("Export_Target_Excel"),
            ScreenAnchor.ById("Export_Records_All")
        },
        MinMatches = 2
    };

    [Fact]
    public void Screen_Is_Detected_Only_When_Enough_Anchors_Match()
    {
        var engine = Engine(new HashSet<string> { "Spool_Grid" }, "Bereit");
        Assert.False(engine.Matches(SpoolList)); // 1 of 3 anchors < MinMatches 2

        engine = Engine(new HashSet<string> { "Spool_Grid", "Spool_Refresh" }, "Bereit");
        Assert.True(engine.Matches(SpoolList));

        engine = Engine(new HashSet<string> { "Spool_Grid" }, "Druckauswahl - Liste");
        Assert.True(engine.Matches(SpoolList)); // id + text anchor
    }

    [Fact]
    public void Detect_Returns_Best_Matching_Screen_With_Confidence()
    {
        var engine = Engine(
            new HashSet<string> { "Export_Target_Excel", "Export_Records_All" },
            "System - Gitterbox-Export");

        var result = engine.Detect(new[] { SpoolList, ExportScreen });

        Assert.Equal("GITTERBOX_EXPORT", result.Screen?.Name);
        Assert.Equal(1.0, result.Confidence, 3);
        Assert.Equal(3, result.MatchedAnchors);
    }

    [Fact]
    public void Detect_Returns_No_Screen_When_Nothing_Reaches_Minimum()
    {
        var engine = Engine(new HashSet<string>(), "Bereit");
        var result = engine.Detect(new[] { SpoolList, ExportScreen });
        Assert.Null(result.Screen);
        Assert.Equal(0, result.Confidence);
    }

    [Fact]
    public async Task RunStep_Invokes_OnFailure_And_Raises_Structured_Error_On_Timeout()
    {
        var engine = Engine(new HashSet<string>(), "");
        var actionRan = false;
        var failureHookRan = false;

        var ex = await Assert.ThrowsAsync<WilkenAutomationException>(() => engine.RunStepAsync(new AutomationStep
        {
            Name = "Click Run",
            Action = () => actionRan = true,
            ExpectedState = () => false,
            Timeout = TimeSpan.FromMilliseconds(50),
            FailureErrorCode = "SPOOL_CONFIRMATION_MISSING",
            OnFailure = _ => { failureHookRan = true; return Task.CompletedTask; }
        }, CancellationToken.None));

        Assert.True(actionRan);
        Assert.True(failureHookRan);
        Assert.Equal("SPOOL_CONFIRMATION_MISSING", ex.ErrorCode);
    }

    [Fact]
    public async Task RunStep_Completes_When_Expected_State_Is_Reached()
    {
        var engine = Engine(new HashSet<string>(), "");
        var reached = false;
        await engine.RunStepAsync(new AutomationStep
        {
            Name = "Set parameter",
            Action = () => reached = true,
            ExpectedState = () => reached,
            Timeout = TimeSpan.FromSeconds(1)
        }, CancellationToken.None);
        Assert.True(reached);
    }
}

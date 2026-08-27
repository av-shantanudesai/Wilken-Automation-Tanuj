using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Tests;

public class ExportFilenameDirectoryTests
{
    private static ExportJob Job(string client = "0001", int year = 2024) =>
        new() { Client = client, FiscalYear = year, Department = "HGB" };

    [Fact]
    public void Default_Layout_Is_Mandant_Then_Year()
    {
        var dir = ExportFilename.DirectoryFor(Job(), @"C:\exports", template: null);
        Assert.Equal(Path.Combine(@"C:\exports", "Mandant_0001", "2024"), dir);
    }

    [Fact]
    public void Missing_FiscalYear_Falls_Back_To_Master_Folder()
    {
        var dir = ExportFilename.DirectoryFor(Job(year: 0), @"C:\exports", template: null);
        Assert.Equal(Path.Combine(@"C:\exports", "Mandant_0001", "master"), dir);
    }

    [Fact]
    public void Relative_Template_Is_Rendered_Under_Root()
    {
        var dir = ExportFilename.DirectoryFor(Job(), @"C:\exports", "{CLIENT}\\{YEAR}");
        Assert.Equal(Path.Combine(@"C:\exports", "0001", "2024"), dir);
    }

    [Fact]
    public void Rooted_Template_Ignores_Root()
    {
        var dir = ExportFilename.DirectoryFor(Job(), @"C:\exports", @"D:\archive\{CLIENT}");
        Assert.Equal(@"D:\archive\0001", dir);
    }
}

public class RunCountersApplyTests
{
    private static AutomationRun Run(RunStatus status) => new()
    {
        RunId = "run-1",
        Status = status,
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public void ApplyToRun_Copies_All_Counters()
    {
        var run = Run(RunStatus.Running);
        RunCounters.ApplyToRun(run, new StatusCountsDto(10, 3, 1, 2, 2, 1, 1));

        Assert.Equal(10, run.TotalJobs);
        Assert.Equal(2, run.SuccessfulWithData);
        Assert.Equal(1, run.SuccessfulEmpty);
        Assert.Equal(1, run.FailedJobs);
        Assert.Equal(5, run.PendingJobs); // pending + retry
        Assert.Equal(4, run.CompletedJobs);
    }

    [Fact]
    public void Running_Run_Completes_When_All_Jobs_Are_Terminal()
    {
        var run = Run(RunStatus.Running);
        RunCounters.ApplyToRun(run, new StatusCountsDto(4, 0, 0, 0, 2, 1, 1));

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.NotNull(run.CompletedAt);
    }

    [Fact]
    public void Running_Run_Stays_Running_While_Jobs_Are_Open()
    {
        var run = Run(RunStatus.Running);
        RunCounters.ApplyToRun(run, new StatusCountsDto(4, 1, 0, 0, 2, 1, 0));

        Assert.Equal(RunStatus.Running, run.Status);
        Assert.Null(run.CompletedAt);
    }

    [Fact]
    public void Paused_Run_Is_Never_AutoCompleted()
    {
        var run = Run(RunStatus.Paused);
        RunCounters.ApplyToRun(run, new StatusCountsDto(4, 0, 0, 0, 2, 1, 1));

        Assert.Equal(RunStatus.Paused, run.Status);
    }

    [Fact]
    public void Empty_Run_Is_Not_Completed()
    {
        var run = Run(RunStatus.Running);
        RunCounters.ApplyToRun(run, StatusCountsDto.Empty);

        Assert.Equal(RunStatus.Running, run.Status);
    }
}

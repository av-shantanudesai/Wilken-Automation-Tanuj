using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Application.Services;
using WilkenAutomation.Application.Validators;
using WilkenAutomation.Infrastructure.FileSystem;

namespace WilkenAutomation.Tests;

public class JobGeneratorTests : IDisposable
{
    private readonly TestContext _ctx = new();

    [Fact]
    public async Task Generates_ClientsTimesYearsTimesDepartments_Jobs()
    {
        var generator = _ctx.Generator();
        var config = generator.BuildConfig(new CreateRunRequestDto { ClientCount = 3, YearFrom = 2003, YearTo = 2005 });

        Assert.Equal(3 * 3 * 2, config.ExpectedJobs);

        var run = await generator.GenerateRunAsync(config, null, autoStart: false, CancellationToken.None);
        var jobs = await _ctx.Jobs.GetAllForRunAsync(run.RunId, CancellationToken.None);

        Assert.Equal(18, run.TotalJobs);
        Assert.Equal(run.ExpectedJobs, run.TotalJobs);
        Assert.Equal(18, jobs.Count);
        Assert.Equal(18, jobs.Select(j => j.JobId).Distinct().Count());
    }

    [Fact]
    public async Task DefaultOrdering_Is_Client_Year_CommercialThenTax()
    {
        var generator = _ctx.Generator();
        var config = generator.BuildConfig(new CreateRunRequestDto { ClientCount = 2, YearFrom = 2003, YearTo = 2004 });
        var run = await generator.GenerateRunAsync(config, null, false, CancellationToken.None);
        var jobs = await _ctx.Jobs.GetAllForRunAsync(run.RunId, CancellationToken.None);

        Assert.Equal("001", jobs[0].Client);
        Assert.Equal(2003, jobs[0].FiscalYear);
        Assert.Equal("Handelsrecht", jobs[0].Department);
        Assert.Equal("Steuerrecht", jobs[1].Department);
        Assert.Equal(2004, jobs[2].FiscalYear);
        Assert.Equal("002", jobs[4].Client);
    }

    [Fact]
    public async Task DuplicateInputs_DoNotCreateDuplicateJobs()
    {
        var generator = _ctx.Generator();
        var config = generator.BuildConfig(new CreateRunRequestDto
        {
            Clients = new List<string> { "001", "001" },
            Years = new List<int> { 2003, 2003 },
            Departments = new List<string> { "Handelsrecht", "Handelsrecht" }
        });
        var run = await generator.GenerateRunAsync(config, null, false, CancellationToken.None);

        Assert.Equal(1, run.TotalJobs);
    }

    public void Dispose() => _ctx.Dispose();
}

public class JobStateMachineTests
{
    [Theory]
    [InlineData(JobStatus.Pending, JobStatus.Running, true)]
    [InlineData(JobStatus.Retry, JobStatus.Running, true)]
    [InlineData(JobStatus.Running, JobStatus.SuccessWithData, true)]
    [InlineData(JobStatus.Running, JobStatus.SuccessEmpty, true)]
    [InlineData(JobStatus.Running, JobStatus.Retry, true)]
    [InlineData(JobStatus.Running, JobStatus.FailedFinal, true)]
    [InlineData(JobStatus.FailedFinal, JobStatus.Pending, true)]
    [InlineData(JobStatus.Pending, JobStatus.SuccessWithData, false)]
    [InlineData(JobStatus.Pending, JobStatus.FailedFinal, false)]
    [InlineData(JobStatus.SuccessWithData, JobStatus.Running, false)]
    [InlineData(JobStatus.FailedFinal, JobStatus.Running, false)]
    public void ValidatesTransitions(JobStatus from, JobStatus to, bool allowed)
    {
        Assert.Equal(allowed, JobStateMachine.CanTransition(from, to));
    }

    [Fact]
    public void InvalidTransition_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => JobStateMachine.EnsureTransition(JobStatus.Pending, JobStatus.SuccessWithData));
    }
}

public class FileValidatorTests : IDisposable
{
    private readonly TestContext _ctx = new();
    private readonly ExportJob _job = new()
    {
        JobId = "J1", RunId = "R1", Client = "001", FiscalYear = 2003,
        Department = "Handelsrecht", DepartmentCode = "HR"
    };

    private string WriteFile(string content)
    {
        var path = Path.Combine(_ctx.WorkDirectory, $"{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, content);
        return path;
    }

    private static string ValidContent(int records)
    {
        var rows = string.Join('\n', Enumerable.Range(1, records).Select(i => $"A{i};Asset;2003-01-01;100;10;90"));
        return $"{ExportFileValidator.ReportMarker} V2.4\nClient;001\nFiscalYear;2003\nDepartment;Handelsrecht\nRecordCount;{records}\n" +
               $"{ExportFileValidator.DataMarker}\nAssetNo;Description;Date;Value;Dep;Book\n" +
               (records > 0 ? rows + "\n" : "") + $"{ExportFileValidator.EndMarker}\n";
    }

    [Fact]
    public async Task MissingFile_IsInvalid()
    {
        var result = await _ctx.Validator.ValidateAsync(
            Path.Combine(_ctx.WorkDirectory, "missing.csv"), _job, true, CancellationToken.None);
        Assert.Equal(ValidationStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task EmptyPhysicalFile_IsInvalid()
    {
        var result = await _ctx.Validator.ValidateAsync(WriteFile(""), _job, true, CancellationToken.None);
        Assert.Equal(ValidationStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task ValidEmptyBusinessExport_IsValidEmpty()
    {
        var result = await _ctx.Validator.ValidateAsync(WriteFile(ValidContent(0)), _job, true, CancellationToken.None);
        Assert.Equal(ValidationStatus.ValidEmpty, result.Status);
        Assert.Equal(0, result.RecordCount);
    }

    [Fact]
    public async Task CorruptFile_MissingEndMarker_IsInvalid()
    {
        var corrupt = ValidContent(5).Replace(ExportFileValidator.EndMarker, "");
        var result = await _ctx.Validator.ValidateAsync(WriteFile(corrupt), _job, true, CancellationToken.None);
        Assert.Equal(ValidationStatus.Invalid, result.Status);
        Assert.Contains("truncated", result.Detail);
    }

    [Fact]
    public async Task ValidExport_IsValid_WithRecordCount()
    {
        var result = await _ctx.Validator.ValidateAsync(WriteFile(ValidContent(7)), _job, true, CancellationToken.None);
        Assert.Equal(ValidationStatus.Valid, result.Status);
        Assert.Equal(7, result.RecordCount);
    }

    [Fact]
    public async Task WrongClientInContent_IsInvalid()
    {
        var wrong = ValidContent(3).Replace("Client;001", "Client;099");
        var result = await _ctx.Validator.ValidateAsync(WriteFile(wrong), _job, true, CancellationToken.None);
        Assert.Equal(ValidationStatus.Invalid, result.Status);
        Assert.Contains("Client mismatch", result.Detail);
    }

    public void Dispose() => _ctx.Dispose();
}

public class ChecksumTests : IDisposable
{
    private readonly TestContext _ctx = new();

    [Fact]
    public async Task Sha256_MatchesKnownVector()
    {
        var path = Path.Combine(_ctx.WorkDirectory, "abc.txt");
        await File.WriteAllTextAsync(path, "abc");
        var hash = await new ChecksumService().ComputeSha256Async(path, CancellationToken.None);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
    }

    public void Dispose() => _ctx.Dispose();
}

public class RuntimeStatisticsTests
{
    [Fact]
    public void ProgressPercent_ComputedFromTerminalJobs()
    {
        var counts = new StatusCountsDto(100, 40, 1, 4, 50, 3, 2);
        Assert.Equal(55.0, RunStatisticsService.ProgressPercent(counts));
    }

    [Fact]
    public void EstimatedRemaining_UsesActualAverage()
    {
        Assert.Equal(120000, RunStatisticsService.EstimateRemainingMs(60000, 2));
        Assert.Null(RunStatisticsService.EstimateRemainingMs(null, 10));
    }
}

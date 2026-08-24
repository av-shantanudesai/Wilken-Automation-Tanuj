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

        var run = await generator.GenerateRunAsync(config, null, autoStart: false, CancellationToken.None, userId: 1);
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
        var run = await generator.GenerateRunAsync(config, null, false, CancellationToken.None, 1);
        var jobs = await _ctx.Jobs.GetAllForRunAsync(run.RunId, CancellationToken.None);

        Assert.Equal("001", jobs[0].Client);
        Assert.Equal(2003, jobs[0].FiscalYear);
        Assert.Equal("Handelsrecht", jobs[0].Department);
        Assert.Equal("Steuerrecht", jobs[1].Department);
        Assert.Equal(2004, jobs[2].FiscalYear);
        Assert.Equal("002", jobs[4].Client);
    }

    [Fact]
    public void BuildConfig_Persists_Dashboard_Mandants_And_Paths()
    {
        var generator = _ctx.Generator();
        var config = generator.BuildConfig(new CreateRunRequestDto
        {
            Clients = new List<string> { "02", " 041 " },
            YearFrom = 2003,
            YearTo = 2003,
            WilkenExecutablePath = @"C:\Wilken\CS2\Wilken.exe",
            ExportRootDirectory = @"C:\WilkenExports"
        });
        Assert.Equal(new[] { "02", "041" }, config.Clients);
        Assert.Equal(@"C:\Wilken\CS2\Wilken.exe", config.WilkenExecutablePath);
        Assert.Equal(@"C:\WilkenExports", config.ExportRootDirectory);
    }

    [Fact]
    public async Task AlleAnlagen_Generates_ClientYear_Steuerrecht_Jobs()
    {
        var generator = _ctx.Generator();
        var config = generator.BuildConfig(new CreateRunRequestDto
        {
            ClientCount = 2,
            YearFrom = 2003,
            YearTo = 2004,
            ExportDefinitions = new List<string> { "AlleAnlagenNachKontenVerdichtet" }
        });
        Assert.Equal(4, config.ExpectedJobs);
        var run = await generator.GenerateRunAsync(config, null, false, CancellationToken.None, 1);
        var jobs = await _ctx.Jobs.GetAllForRunAsync(run.RunId, CancellationToken.None);
        Assert.Equal(4, jobs.Count);
        Assert.All(jobs, j =>
        {
            Assert.Equal("AlleAnlagenNachKontenVerdichtet", j.ExportDefinition);
            Assert.Equal("SPOOL", j.ExecutorType);
            Assert.Equal("Steuerrecht", j.AccountingLaw);
        });
    }

    [Fact]
    public async Task MasterData_Generates_ClientOnly_Jobs()
    {
        var generator = _ctx.Generator();
        var config = generator.BuildConfig(new CreateRunRequestDto
        {
            ClientCount = 2,
            YearFrom = 2003,
            YearTo = 2004,
            ExportDefinitions = new List<string> { "MasterData" }
        });
        Assert.Equal(2, config.ExpectedJobs);
        var run = await generator.GenerateRunAsync(config, null, false, CancellationToken.None, 1);
        var jobs = await _ctx.Jobs.GetAllForRunAsync(run.RunId, CancellationToken.None);
        Assert.Equal(2, jobs.Count);
        Assert.All(jobs, j =>
        {
            Assert.Equal("MasterData", j.ExportDefinition);
            Assert.Equal("VIEW", j.ExecutorType);
            Assert.Equal(0, j.FiscalYear);
        });
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
        var run = await generator.GenerateRunAsync(config, null, false, CancellationToken.None, 1);

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

    [Fact]
    public async Task ValidXlsx_IsValid_WithRecordCount()
    {
        var path = WriteReplicaXlsx("Zugangsliste", "Handelsrecht", 7);
        var result = await _ctx.Validator.ValidateAsync(path, _job, true, CancellationToken.None);
        Assert.Equal(ValidationStatus.Valid, result.Status);
        Assert.Equal(7, result.RecordCount);
    }

    [Fact]
    public async Task XlsxWrongDepartment_IsInvalid()
    {
        var path = WriteReplicaXlsx("Anlagenspiegel nach Anlagen", "Steuerrecht", 3);
        var result = await _ctx.Validator.ValidateAsync(path, _job, true, CancellationToken.None);
        Assert.Equal(ValidationStatus.Invalid, result.Status);
        Assert.Contains("Department mismatch", result.Detail);
    }

    [Fact]
    public async Task CorruptZip_IsInvalid()
    {
        var path = Path.Combine(_ctx.WorkDirectory, $"{Guid.NewGuid():N}.xlsx");
        await File.WriteAllTextAsync(path, "not-a-zip");
        var result = await _ctx.Validator.ValidateAsync(path, _job, true, CancellationToken.None);
        Assert.Equal(ValidationStatus.Invalid, result.Status);
    }

    private string WriteReplicaXlsx(string report, string fachbereich, int records)
    {
        var path = Path.Combine(_ctx.WorkDirectory, $"{Guid.NewGuid():N}.xlsx");
        using var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create);
        var entry = zip.CreateEntry("xl/worksheets/sheet1.xml");
        using var writer = new StreamWriter(entry.Open());
        writer.Write("<?xml version=\"1.0\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        writer.Write("<row r=\"1\"><c t=\"inlineStr\"><is><t>Report</t></is></c></row>");
        for (var i = 1; i <= records; i++)
            writer.Write($"<row r=\"{i + 1}\"><c t=\"inlineStr\"><is><t>{report}</t></is></c><c t=\"inlineStr\"><is><t>{fachbereich}</t></is></c></row>");
        writer.Write("</sheetData></worksheet>");
        return path;
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

public class ExportFilenameTests
{
    [Fact]
    public void Renders_Template_Skipping_Empty_Tokens()
    {
        var name = ExportFilename.Render(
            "WILKEN_{CLIENT}_{EXPORT}_{YEAR}_{PERIOD}_{ACCOUNTINGLAW}_{TIMESTAMP}.csv",
            new ExportJob
            {
                Client = "001",
                ExportDefinition = "MasterData",
                FiscalYear = 0,
                Period = "",
                AccountingLaw = ""
            },
            new DateTime(2026, 8, 23, 10, 0, 0, DateTimeKind.Utc));
        Assert.Equal("WILKEN_001_MasterData_20260823100000.csv", name);
    }
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

public class PasswordHasherTests
{
    [Fact]
    public void Hash_ThenVerify_Succeeds()
    {
        var hash = PasswordHasher.Hash("correct-horse");
        Assert.True(PasswordHasher.Verify("correct-horse", hash));
        Assert.False(PasswordHasher.Verify("wrong-password", hash));
    }
}

public class SignalREventsTests
{
    [Fact]
    public void IsKnown_AcceptsCatalogAndRejectsArbitraryNames()
    {
        Assert.True(SignalREvents.IsKnown(SignalREvents.JobStarted));
        Assert.True(SignalREvents.IsKnown(SignalREvents.RunsChanged));
        Assert.False(SignalREvents.IsKnown("DropAllTables"));
        Assert.False(SignalREvents.IsKnown(""));
        Assert.False(SignalREvents.IsKnown(null));
    }
}

public class WilkenSelectorCatalogTests
{
    [Fact]
    public void MissingRequired_Reports_Unmapped_Core_Controls()
    {
        var missing = WilkenSelectorCatalog.MissingRequired(new Dictionary<string, string>
        {
            ["ClientField"] = "Name:Mandant",
            ["ExecuteButton"] = ""
        });
        Assert.Contains("FiscalYearField", missing);
        Assert.Contains("DepartmentField", missing);
        Assert.Contains("ExecuteButton", missing);
        Assert.Contains("ExportButton", missing);
        Assert.DoesNotContain("ClientField", missing);
    }

    [Fact]
    public void MissingRequired_Empty_When_Core_Selectors_Mapped()
    {
        var mapped = WilkenSelectorCatalog.RequiredForAutomation
            .ToDictionary(k => k, k => "Name:" + k);
        Assert.Empty(WilkenSelectorCatalog.MissingRequired(mapped));
    }

    [Fact]
    public void RemoteDisplay_Detects_Citrix_And_Browser_Processes()
    {
        Assert.True(WilkenSessionPolicy.IsRemoteDisplayProcess("wfica32"));
        Assert.True(WilkenSessionPolicy.IsRemoteDisplayProcess("msedge"));
        Assert.False(WilkenSessionPolicy.IsRemoteDisplayProcess("WilkenCS2"));
    }

    [Fact]
    public void JavaWindow_Detected_From_Awt_Class()
    {
        Assert.True(WilkenSessionPolicy.IsLikelyJavaWindow("SunAwtFrame", "Win32"));
        Assert.False(WilkenSessionPolicy.IsLikelyJavaWindow("HwndWrapper", "WPF"));
    }
}

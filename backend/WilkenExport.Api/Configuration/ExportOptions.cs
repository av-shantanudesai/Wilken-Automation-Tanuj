namespace WilkenExport.Api.Configuration;

/// <summary>
/// Application-level defaults (appsettings.json, section "Export").
/// Every run snapshots its own effective RunConfig, so changing these defaults
/// never affects a run that is already generated.
/// </summary>
public class ExportOptions
{
    public string OutputRootDirectory { get; set; } = "Wilken_Export";
    public string DiagnosticsDirectory { get; set; } = "Wilken_Diagnostics";

    /// <summary>Explicit client list. When empty, ClientCount is used to generate 001..NNN.</summary>
    public List<string> Clients { get; set; } = new();
    public int ClientCount { get; set; } = 78;

    /// <summary>Explicit year list. When empty, YearFrom..YearTo is used.</summary>
    public List<int> Years { get; set; } = new();
    public int YearFrom { get; set; } = 2003;
    public int YearTo { get; set; } = 2025;

    // No initializer default: configuration binding appends to pre-populated lists,
    // which would duplicate entries. Code falls back to HR/ST when the list is empty.
    public List<string> Departments { get; set; } = new();

    /// <summary>Sort precedence, comma separated: Client,FiscalYear,Department (default).</summary>
    public string JobOrder { get; set; } = "Client,FiscalYear,Department";

    public int MaxAttempts { get; set; } = 3;

    /// <summary>Reserved for later parallelization. Version 1 enforces 1.</summary>
    public int WorkerCount { get; set; } = 1;

    public bool EnableContentValidation { get; set; } = true;
    public bool EnableChecksum { get; set; } = true;

    public TimeoutOptions Timeouts { get; set; } = new();
    public SimulationOptions Simulation { get; set; } = new();
}

public class TimeoutOptions
{
    public int StartupSeconds { get; set; } = 60;
    public int LoginSeconds { get; set; } = 30;
    public int NavigationSeconds { get; set; } = 30;
    public int ReportExecutionSeconds { get; set; } = 900;
    public int SpoolSeconds { get; set; } = 300;
    public int ExportSeconds { get; set; } = 300;
    public int FileCreationSeconds { get; set; } = 120;
}

/// <summary>Controls the simulated (dummy) Wilken adapter used for testing.</summary>
public class SimulationOptions
{
    public double MinJobSeconds { get; set; } = 2;
    public double MaxJobSeconds { get; set; } = 6;

    /// <summary>Probability that a report execution fails (0..1).</summary>
    public double FailureRate { get; set; } = 0.08;

    /// <summary>Probability that the simulated Wilken session crashes mid-job (0..1).</summary>
    public double CrashRate { get; set; } = 0.03;

    /// <summary>Probability that a period legitimately contains no data (0..1).</summary>
    public double EmptyRate { get; set; } = 0.10;
}

/// <summary>
/// The effective, immutable configuration snapshot stored on each run (ConfigJson).
/// </summary>
public class RunConfig
{
    public List<string> Clients { get; set; } = new();
    public List<int> Years { get; set; } = new();
    public List<string> Departments { get; set; } = new();
    public string JobOrder { get; set; } = "Client,FiscalYear,Department";
    public int MaxAttempts { get; set; } = 3;
    public string OutputRootDirectory { get; set; } = "Wilken_Export";
    public string DiagnosticsDirectory { get; set; } = "Wilken_Diagnostics";
    public bool EnableContentValidation { get; set; } = true;
    public bool EnableChecksum { get; set; } = true;
    public TimeoutOptions Timeouts { get; set; } = new();
    public SimulationOptions Simulation { get; set; } = new();

    public int ExpectedJobCount => Clients.Count * Years.Count * Departments.Count;
}

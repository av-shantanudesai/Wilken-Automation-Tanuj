namespace WilkenAutomation.Application.Models;

/// <summary>
/// Effective configuration a run was generated with. Stored as JSON on the run,
/// never modified afterwards, and used by the worker while processing that run.
/// </summary>
public class RunConfig
{
    public List<string> Clients { get; set; } = new();
    public List<int> Years { get; set; } = new();
    public List<string> Departments { get; set; } = new();
    public List<string> Periods { get; set; } = new();
    public List<string> ExportDefinitions { get; set; } = new();

    /// <summary>Sort precedence, e.g. "Client,FiscalYear,Department".</summary>
    public string JobOrder { get; set; } = "Client,FiscalYear,Department";

    public int MaxAttempts { get; set; } = 3;
    public bool EnableContentValidation { get; set; } = true;
    public bool EnableChecksum { get; set; } = true;

    public SimulationConfig Simulation { get; set; } = new();

    /// <summary>Filled by the generator from the selected export definitions.</summary>
    public int ExpectedJobs { get; set; }
}

/// <summary>Behavior of the mock automation mode (accepted from the existing frontend).</summary>
public class SimulationConfig
{
    public double MinJobSeconds { get; set; } = 2;
    public double MaxJobSeconds { get; set; } = 6;
    public double FailureRate { get; set; } = 0.08;
    public double CrashRate { get; set; } = 0.03;
    public double EmptyRate { get; set; } = 0.10;
}

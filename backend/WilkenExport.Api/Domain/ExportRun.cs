namespace WilkenExport.Api.Domain;

public class ExportRun
{
    /// <summary>Human-readable unique run id, e.g. RUN-20260817-001.</summary>
    public string Id { get; set; } = default!;

    public RunStatus Status { get; set; } = RunStatus.Created;

    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>Clients x Years x Departments, calculated from configuration.</summary>
    public int ExpectedJobCount { get; set; }

    /// <summary>Number of jobs actually generated; must equal ExpectedJobCount.</summary>
    public int GeneratedJobCount { get; set; }

    /// <summary>Immutable snapshot of the configuration this run was created with (JSON).</summary>
    public string ConfigJson { get; set; } = "{}";

    public string? Notes { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public List<ExportJob> Jobs { get; set; } = new();
}

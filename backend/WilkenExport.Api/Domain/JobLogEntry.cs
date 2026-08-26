namespace WilkenExport.Api.Domain;

public class JobLogEntry
{
    public long Id { get; set; }

    public DateTime Timestamp { get; set; }
    public string RunId { get; set; } = default!;
    public string? JobId { get; set; }

    public string? Client { get; set; }
    public int? FiscalYear { get; set; }
    public string? Department { get; set; }

    /// <summary>INFO | WARN | ERROR</summary>
    public string Level { get; set; } = "INFO";

    /// <summary>Structured action name, e.g. SELECT_CLIENT, WAIT_SPOOL, VALIDATE.</summary>
    public string Action { get; set; } = default!;

    public int? Attempt { get; set; }
    public long? DurationMs { get; set; }
    public string? ErrorCode { get; set; }
    public string Message { get; set; } = "";
}

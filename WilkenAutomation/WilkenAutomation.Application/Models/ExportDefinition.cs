namespace WilkenAutomation.Application.Models;

public static class ExportDimensions
{
    public const string Client = "CLIENT";
    public const string Year = "YEAR";
    public const string Period = "PERIOD";
    public const string AccountingLaw = "ACCOUNTING_LAW";
}

public static class ExecutorTypes
{
    public const string Spool = "SPOOL";
    public const string View = "VIEW";
}

public sealed class ExportDefinitionCatalogFile
{
    public List<ExportDefinition> Definitions { get; set; } = new();
}

public sealed class ExportDefinition
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = ExecutorTypes.Spool;
    public string Module { get; set; } = "Asset Accounting";
    public string DisplayName { get; set; } = "";
    public List<string> Requires { get; set; } = new();
    public Dictionary<string, string> Defaults { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public ExecutionSpec Execution { get; set; } = new();
    public ExportOutputSpec Export { get; set; } = new();
    public SpoolMatchSpec? SpoolMatch { get; set; }
    public string? ReplicaNavId { get; set; }
    public string? ReplicaTitleContains { get; set; }

    public bool RequiresDimension(string dimension) =>
        Requires.Any(r => string.Equals(r, dimension, StringComparison.OrdinalIgnoreCase));

    public string DefaultValue(string dimension, string fallback = "") =>
        Defaults.TryGetValue(dimension, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
}

public sealed class ExecutionSpec
{
    public string StartAction { get; set; } = "Execute";
    public string CompletionDetection { get; set; } = "SPOOL_CREATED";
}

public sealed class ExportOutputSpec
{
    public string Format { get; set; } = "CSV";
    public string Filename { get; set; } = "WILKEN_{CLIENT}_{EXPORT}_{TIMESTAMP}.csv";
    public string? Directory { get; set; }
}

public sealed class SpoolMatchSpec
{
    public string? ListName { get; set; }
    public string? Extension { get; set; }
    public string? Protocol { get; set; }
    public string? User { get; set; }
}

using System.Text.Json;
using WilkenAutomation.Application.Models;

namespace WilkenAutomation.Application.Services;

/// <summary>
/// Loads export recipes from export-definitions.json, falling back to the
/// built-in catalog so Mock and tests work without a file on disk.
/// </summary>
public sealed class ExportDefinitionCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly IReadOnlyDictionary<string, ExportDefinition> _byName;

    public ExportDefinitionCatalog(IEnumerable<ExportDefinition> definitions)
    {
        _byName = definitions.ToDictionary(d => d.Name, d => d, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ExportDefinition> All => _byName.Values.OrderBy(d => d.Name).ToList();

    public ExportDefinition Get(string name) =>
        TryGet(name) ?? throw new ArgumentException($"Unknown export definition '{name}'.");

    public ExportDefinition? TryGet(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : _byName.GetValueOrDefault(name);

    /// <summary>Legacy jobs without a recipe: Steuerrecht → Anlagenspiegel, else Zugangsliste.</summary>
    public ExportDefinition ResolveForJob(ExportJob job)
    {
        if (TryGet(job.ExportDefinition) is { } named)
            return named;
        return string.Equals(job.Department, "Steuerrecht", StringComparison.OrdinalIgnoreCase)
               || string.Equals(job.AccountingLaw, "Steuerrecht", StringComparison.OrdinalIgnoreCase)
            ? Get("Anlagenspiegel")
            : Get("Zugangsliste");
    }

    public static ExportDefinitionCatalog Load(string? contentRoot = null)
    {
        foreach (var path in CandidatePaths(contentRoot))
        {
            if (!File.Exists(path)) continue;
            var file = JsonSerializer.Deserialize<ExportDefinitionCatalogFile>(File.ReadAllText(path), JsonOptions);
            if (file?.Definitions is { Count: > 0 })
                return new ExportDefinitionCatalog(file.Definitions);
        }

        return new ExportDefinitionCatalog(Builtin());
    }

    public static IReadOnlyList<string> DefaultDefinitionNames { get; } = new[] { "Zugangsliste", "Anlagenspiegel" };

    private static IEnumerable<string> CandidatePaths(string? contentRoot)
    {
        yield return Path.Combine(AppContext.BaseDirectory, "export-definitions.json");
        yield return Path.Combine(AppContext.BaseDirectory, "ExportDefinitions", "catalog.json");
        if (!string.IsNullOrWhiteSpace(contentRoot))
        {
            yield return Path.Combine(contentRoot, "export-definitions.json");
            yield return Path.Combine(contentRoot, "ExportDefinitions", "catalog.json");
        }
    }

    public static IEnumerable<ExportDefinition> Builtin()
    {
        yield return new ExportDefinition
        {
            Name = "Zugangsliste",
            Type = ExecutorTypes.Spool,
            DisplayName = "Zugangsliste",
            Requires = new() { ExportDimensions.Client, ExportDimensions.Year, ExportDimensions.AccountingLaw },
            Defaults = new(StringComparer.OrdinalIgnoreCase) { [ExportDimensions.AccountingLaw] = "Handelsrecht" },
            Execution = new() { CompletionDetection = "SPOOL_CREATED" },
            Export = new() { Format = "XLSX", Filename = "WILKEN_{CLIENT}_{EXPORT}_{YEAR}_{ACCOUNTINGLAW}_{TIMESTAMP}.xlsx" },
            SpoolMatch = new() { ListName = "B024", Extension = "PRT", Protocol = "Protokoll: Zugangsliste", User = "BHL" },
            ReplicaNavId = "Nav_Zugangsliste",
            ReplicaTitleContains = "Zugangsliste erstellen"
        };
        yield return new ExportDefinition
        {
            Name = "Anlagenspiegel",
            Type = ExecutorTypes.Spool,
            DisplayName = "Anlagenspiegel nach Anlagen",
            Requires = new() { ExportDimensions.Client, ExportDimensions.Year, ExportDimensions.AccountingLaw },
            Defaults = new(StringComparer.OrdinalIgnoreCase) { [ExportDimensions.AccountingLaw] = "Steuerrecht" },
            Execution = new() { CompletionDetection = "SPOOL_CREATED" },
            Export = new() { Format = "XLSX", Filename = "WILKEN_{CLIENT}_{EXPORT}_{YEAR}_{ACCOUNTINGLAW}_{TIMESTAMP}.xlsx" },
            SpoolMatch = new() { ListName = "B015", Extension = "PRT", Protocol = "Protokoll: Anlagenspiegel", User = "BHL" },
            ReplicaNavId = "Nav_Anlagenspiegel",
            ReplicaTitleContains = "Anlagenspiegel erstellen"
        };
        yield return new ExportDefinition
        {
            Name = "MasterData",
            Type = ExecutorTypes.View,
            DisplayName = "Asset master data",
            Requires = new() { ExportDimensions.Client },
            Execution = new() { CompletionDetection = "DATA_READY" },
            Export = new() { Format = "CSV", Filename = "WILKEN_{CLIENT}_{EXPORT}_{TIMESTAMP}.csv" }
        };
        yield return new ExportDefinition
        {
            Name = "Bookings",
            Type = ExecutorTypes.View,
            DisplayName = "Bookings by period",
            Requires = new() { ExportDimensions.Client, ExportDimensions.Year, ExportDimensions.Period },
            Defaults = new(StringComparer.OrdinalIgnoreCase) { [ExportDimensions.Period] = "01-12" },
            Execution = new() { CompletionDetection = "DATA_READY" },
            Export = new() { Format = "CSV", Filename = "WILKEN_{CLIENT}_{EXPORT}_{YEAR}_{PERIOD}_{TIMESTAMP}.csv" }
        };
    }
}

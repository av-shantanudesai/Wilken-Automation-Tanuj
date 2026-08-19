using WilkenAutomation.Application.Enums;

namespace WilkenAutomation.Application.Configuration;

public class WilkenOptions
{
    public const string Section = "Wilken";

    public string ExecutablePath { get; set; } = "";
    public string MainWindowTitle { get; set; } = "Wilken CS/2";
    public string? ProcessName { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public int StartupTimeoutSeconds { get; set; } = 60;
    public int LoginTimeoutSeconds { get; set; } = 30;
    public int NavigationTimeoutSeconds { get; set; } = 30;
    public int ReportTimeoutMinutes { get; set; } = 20;
    public int ExportTimeoutMinutes { get; set; } = 10;
    public int FileCreationTimeoutSeconds { get; set; } = 120;
    public int PollingIntervalMs { get; set; } = 500;

    /// <summary>
    /// UIA selectors discovered during the control-inspection POC.
    /// Format per entry: "AutomationId:xyz", "Name:xyz" or "ClassName:xyz".
    /// Empty selectors cause a descriptive CONTROL_NOT_MAPPED failure instead of blind clicking.
    /// </summary>
    public Dictionary<string, string> Selectors { get; set; } = new();
}

public class ExportSettings
{
    public const string Section = "Export";

    public string RootDirectory { get; set; } = "Exports";
    public string ScreenshotDirectory { get; set; } = Path.Combine("Logs", "Screenshots");
    public bool GenerateSha256 { get; set; } = true;
    public string FileExtension { get; set; } = ".csv";
}

public class WorkerSettings
{
    public const string Section = "Worker";

    public int WorkerCount { get; set; } = 1;
    public AutomationMode AutomationMode { get; set; } = AutomationMode.Mock;
    public int PollIntervalMs { get; set; } = 1000;
    public int HeartbeatIntervalMs { get; set; } = 2000;
    public string ApiBaseUrl { get; set; } = "http://localhost:5210";
}

public class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "WilkenAutomation";
    public string Audience { get; set; } = "WilkenAutomation.Clients";
    public string Key { get; set; } = "";
    /// <summary>Short-lived access JWT. Clamped to 5–60 minutes.</summary>
    public int AccessTokenMinutes { get; set; } = 15;
    /// <summary>Refresh token lifetime in days. Clamped to 1–30.</summary>
    public int RefreshTokenDays { get; set; } = 7;
    /// <summary>Worker-to-API JWT lifetime in hours. Clamped to 1–24.</summary>
    public int WorkerTokenHours { get; set; } = 12;
}

/// <summary>Defaults used when a run request does not specify scope explicitly.</summary>
public class RunDefaults
{
    public const string Section = "RunDefaults";

    public int ClientCount { get; set; } = 78;
    public int YearFrom { get; set; } = 2003;
    public int YearTo { get; set; } = 2025;
    public List<string> Departments { get; set; } = new();

    public List<string> EffectiveDepartments =>
        Departments.Count > 0 ? Departments.Distinct().ToList() : new List<string> { "Handelsrecht", "Steuerrecht" };
}

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

    /// <summary>
    /// How long to wait for the user to finish EHP (Country/Company) and Anmeldung
    /// (Benutzer, Passwort, Mandant) after Wilken is started. The dashboard Mandant
    /// is not typed into those screens.
    /// </summary>
    public int ManualLoginTimeoutMinutes { get; set; } = 15;

    /// <summary>Optional process arguments, e.g. replica <c>--manual-login</c>.</summary>
    public string StartupArguments { get; set; } = "";

    /// <summary>
    /// When true, the worker must see EHP/Anmeldung before treating the session
    /// as ready. Prevents attaching to an already-open main window.
    /// </summary>
    public bool RequireManualLoginScreens { get; set; }
    public int NavigationTimeoutSeconds { get; set; } = 30;
    public int ReportTimeoutMinutes { get; set; } = 20;
    public int ExportTimeoutMinutes { get; set; } = 10;
    public int FileCreationTimeoutSeconds { get; set; } = 120;
    public int PollingIntervalMs { get; set; } = 500;

    /// <summary>
    /// Real Citrix test: never launch or kill Wilken. The user opens the published
    /// desktop from Citrix Workspace, logs in, then the worker only attaches.
    /// Replica/DesktopTest sets this false so it can still start the dummy exe.
    /// </summary>
    public bool AttachOnly { get; set; } = true;

    /// <summary>
    /// Real Citrix test: the user logs in manually. The worker never types credentials.
    /// </summary>
    public bool SkipLogin { get; set; } = true;

    /// <summary>
    /// Real Wilken UI stack is unknown (WinForms, WPF, Win32, Java, ...).
    /// Refuse to automate until core selectors were mapped from an --inspect dump.
    /// </summary>
    public bool RequireInspectedSelectors { get; set; } = true;

    /// <summary>
    /// UIA selectors discovered during the control-inspection POC.
    /// Format per entry: "AutomationId:xyz", "Name:xyz", "ClassName:xyz" or "NameContains:xyz".
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
    /// <summary>Re-queue RUNNING jobs older than this. Must exceed report+export timeouts.</summary>
    public int HungJobTimeoutMinutes { get; set; } = 45;
    public int MaintenanceIntervalMinutes { get; set; } = 30;
    public int VerifiedRunCacheSize { get; set; } = 32;
}

public class MaintenanceSettings
{
    public const string Section = "Maintenance";

    /// <summary>How often the worker checks for hung RUNNING jobs. Audit rows and files are never deleted.</summary>
    public int IntervalMinutes { get; set; } = 30;
}

public class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "WilkenAutomation";
    public string Audience { get; set; } = "WilkenAutomation.Clients";
    public string Key { get; set; } = "";
    /// <summary>Separate HMAC key for worker tokens. Falls back to Key in Development only.</summary>
    public string WorkerKey { get; set; } = "";
    public string WorkerAudience { get; set; } = "WilkenAutomation.Worker";
    /// <summary>Short-lived access JWT. Clamped to 5–60 minutes.</summary>
    public int AccessTokenMinutes { get; set; } = 15;
    /// <summary>Refresh token lifetime in days. Clamped to 1–30.</summary>
    public int RefreshTokenDays { get; set; } = 7;
    /// <summary>Worker-to-API JWT lifetime in hours. Clamped to 1–4.</summary>
    public int WorkerTokenHours { get; set; } = 1;
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

namespace WilkenAutomation.Application.Services;

/// <summary>
/// Real Wilken can be WinForms, WPF, Win32, Java, or something else.
/// Selectors are never assumed: inspect the live German UI, then map these keys.
/// Replica AutomationIds are not used on real Test Wilken.
/// </summary>
public static class WilkenSelectorCatalog
{
    /// <summary>
    /// Minimum mapped controls before a real (non-replica) spool/export run can start.
    /// Login keys are omitted because the Citrix user logs in manually.
    /// </summary>
    public static readonly string[] RequiredForAutomation =
    [
        "ClientField",
        "FiscalYearField",
        "DepartmentField",
        "ExecuteButton",
        "ExportButton"
    ];

    /// <summary>
    /// Full CS/2 workflow keys. Fill from --inspect-watch --inspect-record on German Test Wilken.
    /// </summary>
    public static readonly string[] Cs2WorkflowSelectors =
    [
        "ProcessManagerNav",
        "ProcessManagerGrid",
        "ProcessManagerOpen",
        "HomeNav",
        "ListeAnzeigenOpen",
        "DateFromField",
        "DateToField",
        "PeriodFromMonthField",
        "PeriodFromYearField",
        "PeriodToMonthField",
        "PeriodToYearField",
        "FiscalYearField",
        "DepartmentField",
        "SaveButton",
        "ExecuteButton",
        "ConfirmYes",
        "ConfirmNo",
        "ProgressDialog",
        "FunctionLockedOk",
        "ScreenTitle",
        "StatusText",
        "SpoolMenu",
        "PrintSelectionStart",
        "SpoolList",
        "SpoolLatestRow",
        "ExportButton",
        "ExportTargetExcel",
        "ExportRecordsAll",
        "ExportFormatXlsx",
        "ExportRun",
        "InternalWindowClose"
    ];

    public static IReadOnlyList<string> MissingRequired(IDictionary<string, string>? selectors)
    {
        selectors ??= new Dictionary<string, string>();
        return RequiredForAutomation
            .Where(key => !selectors.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    public static IReadOnlyList<string> MissingCs2(IDictionary<string, string>? selectors)
    {
        selectors ??= new Dictionary<string, string>();
        return Cs2WorkflowSelectors
            .Where(key => !selectors.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            .ToArray();
    }
}

/// <summary>
/// Citrix Workspace in a browser is only a remote picture. Automating that
/// client window can never see real Wilken controls.
/// </summary>
public static class WilkenSessionPolicy
{
    public static readonly string[] RemoteDisplayProcessNames =
    [
        "wfica32", "cdviewer", "wfcrun32", "concentr", "receiver",
        "citrixworkspace", "msedge", "chrome", "firefox", "iexplore", "msedgewebview2"
    ];

    public const string AttachInstructions =
        "Open the Test Environment desktop from Citrix Workspace, log in to Wilken yourself, " +
        "and run the worker inside that same desktop (not on the PC that only has the browser). " +
        "Then inspect the UI (WilkenAutomation.Worker.exe --inspect-watch --inspect-record) " +
        "and map Wilken:Selectors before starting a run.";

    public static bool IsRemoteDisplayProcess(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;
        return RemoteDisplayProcessNames.Any(p =>
            processName.Equals(p, StringComparison.OrdinalIgnoreCase)
            || processName.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsLikelyJavaWindow(string? className, string? frameworkId)
    {
        var cls = className ?? "";
        var framework = frameworkId ?? "";
        return cls.Contains("SunAwt", StringComparison.OrdinalIgnoreCase)
            || cls.Contains("GlassWnd", StringComparison.OrdinalIgnoreCase)
            || cls.Contains("SWT_Window", StringComparison.OrdinalIgnoreCase)
            || framework.Equals("Java", StringComparison.OrdinalIgnoreCase);
    }
}

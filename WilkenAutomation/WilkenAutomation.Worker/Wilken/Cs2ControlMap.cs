using FlaUI.Core.Definitions;

namespace WilkenAutomation.Worker.Wilken;

internal sealed record Cs2NameHint(
    string[] Names,
    ControlType[]? Types = null,
    bool ExactName = false,
    bool SkipWindowTitleBar = false);

/// <summary>
/// Maps CS/2 workflow controls to Wilken:Selectors keys and German names.
/// Real Test Wilken does not use replica AutomationIds; fill selectors from inspect.
/// </summary>
internal static class Cs2ControlMap
{
    public static readonly Dictionary<string, string> SelectorKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Nav_ProzesseVerwalten"] = "ProcessManagerNav",
        ["ProcessManager_Grid"] = "ProcessManagerGrid",
        ["ProcessManager_OpenSelected"] = "ProcessManagerOpen",
        ["Nav_Home"] = "HomeNav",
        ["Home_Workspace"] = "HomeNav",
        ["Nav_ListeAnzeigen"] = "SpoolMenu",
        ["Nav_ListeAnzeigen_Open"] = "ListeAnzeigenOpen",
        ["Zugang_DateFrom"] = "DateFromField",
        ["Anlage_DateFrom"] = "DateFromField",
        ["Zugang_DateTo"] = "DateToField",
        ["Anlage_DateTo"] = "DateToField",
        ["Zugang_Period_0"] = "PeriodFromMonthField",
        ["Anlage_Period_0"] = "PeriodFromMonthField",
        ["Zugang_Period_1"] = "PeriodFromYearField",
        ["Anlage_Period_1"] = "PeriodFromYearField",
        ["Zugang_Period_2"] = "PeriodToMonthField",
        ["Anlage_Period_2"] = "PeriodToMonthField",
        ["Zugang_Period_3"] = "PeriodToYearField",
        ["Anlage_Period_3"] = "PeriodToYearField",
        ["Zugang_Fachbereich"] = "DepartmentField",
        ["Anlage_Fachbereich"] = "DepartmentField",
        ["Toolbar_Save"] = "SaveButton",
        ["Toolbar_Execute"] = "ExecuteButton",
        ["Confirm_Yes"] = "ConfirmYes",
        ["Confirm_No"] = "ConfirmNo",
        ["ProgressDialog"] = "ProgressDialog",
        ["FunctionLocked_OK"] = "FunctionLockedOk",
        ["Screen_Title"] = "ScreenTitle",
        ["Status_Text"] = "StatusText",
        ["PrintSelection_Start"] = "PrintSelectionStart",
        ["Spool_Grid"] = "SpoolList",
        ["Spool_LatestDataRow"] = "SpoolLatestRow",
        ["Spool_OpenAdvancedExport"] = "ExportButton",
        ["Spool_Context_ExportAdvanced"] = "ExportButton",
        ["Export_Target_Excel"] = "ExportTargetExcel",
        ["Export_Records_All"] = "ExportRecordsAll",
        ["Export_Format_XLSX"] = "ExportFormatXlsx",
        ["Export_Run"] = "ExportRun",
        ["InternalWindow_Close"] = "InternalWindowClose"
    };

    public static readonly Dictionary<string, Cs2NameHint> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Nav_ProzesseVerwalten"] = new(["Prozesse verwalten"], [ControlType.TreeItem, ControlType.MenuItem, ControlType.ListItem]),
        ["Nav_ListeAnzeigen"] = new(["Liste anzeigen"], [ControlType.TreeItem, ControlType.MenuItem, ControlType.ListItem]),
        ["Nav_ListeAnzeigen_Open"] = new(["Liste anzeigen"], [ControlType.Button, ControlType.MenuItem, ControlType.TreeItem]),
        ["Nav_Home"] = new(["Startseite", "Home"], [ControlType.TreeItem, ControlType.MenuItem, ControlType.ListItem]),
        ["Toolbar_Save"] = new(["Speichern"], [ControlType.Button]),
        ["Toolbar_Execute"] = new(["Ausführen", "Ausfuehren"], [ControlType.Button]),
        ["Confirm_Yes"] = new(["Ja"], [ControlType.Button], ExactName: true),
        ["Confirm_No"] = new(["Nein"], [ControlType.Button], ExactName: true),
        ["PrintSelection_Start"] = new(["Starten"], [ControlType.Button], ExactName: true),
        ["ProcessManager_OpenSelected"] = new(["Öffnen", "Oeffnen"], [ControlType.Button]),
        ["InternalWindow_Close"] = new(["Schließen", "Schliessen"], [ControlType.Button], SkipWindowTitleBar: true),
        ["FunctionLocked_OK"] = new(["OK"], [ControlType.Button], ExactName: true),
        ["FunctionLockedDialog"] = new(["Funktion gesperrt"], [ControlType.Window, ControlType.Pane]),
        ["ProgressDialog"] = new(["Fortschritt"], [ControlType.Window, ControlType.Pane]),
        ["Export_Target_Excel"] = new(["Excel"], [ControlType.RadioButton], ExactName: true),
        ["Export_Records_All"] = new(["Alle"], [ControlType.RadioButton], ExactName: true),
        ["Export_Format_XLSX"] = new(["XLSX", "*.xlsx", "xlsx"], [ControlType.RadioButton]),
        ["Export_Run"] = new(["Ausführen", "Exportieren"], [ControlType.Button]),
        ["Spool_OpenAdvancedExport"] = new(["Erweitert", "Exportieren"], [ControlType.Button, ControlType.MenuItem]),
        ["Spool_Context_Export"] = new(["Export"], [ControlType.MenuItem], ExactName: true),
        ["Spool_Context_ExportAdvanced"] = new(["Erweitert"], [ControlType.MenuItem], ExactName: true),
        ["Zugang_DateFrom"] = new(["Zugangsdatum", "Datum von", "gültig von", "Gueltig von"], [ControlType.Edit, ControlType.ComboBox, ControlType.Document]),
        ["Zugang_DateTo"] = new(["Datum bis", "gültig bis", "Gueltig bis"], [ControlType.Edit, ControlType.ComboBox, ControlType.Document]),
        ["Zugang_Fachbereich"] = new(["Fachbereich"], [ControlType.ComboBox, ControlType.Edit]),
        ["Anlage_Fachbereich"] = new(["Fachbereich"], [ControlType.ComboBox, ControlType.Edit])
    };
}

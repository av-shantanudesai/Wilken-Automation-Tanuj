using System.Text.Json;
using System.Text.RegularExpressions;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Worker.Windows;

/// <summary>
/// Maps inspect dumps / recorded focus events to Wilken:Selectors for German Test Wilken.
/// Always review DateFrom/DateTo/Zeitraum/Fachbereich before a real run — names can collide.
/// </summary>
internal static class SelectorProposer
{
    private static readonly Dictionary<string, string> ExactGermanNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Prozesse verwalten"] = "ProcessManagerNav",
        ["Liste anzeigen"] = "SpoolMenu",
        ["Speichern"] = "SaveButton",
        ["Ausführen"] = "ExecuteButton",
        ["Ausfuehren"] = "ExecuteButton",
        ["Ja"] = "ConfirmYes",
        ["Nein"] = "ConfirmNo",
        ["Starten"] = "PrintSelectionStart",
        ["Öffnen"] = "ProcessManagerOpen",
        ["Oeffnen"] = "ProcessManagerOpen",
        ["Schließen"] = "InternalWindowClose",
        ["Schliessen"] = "InternalWindowClose",
        ["Erweitert"] = "ExportButton",
        ["Excel"] = "ExportTargetExcel",
        ["Fortschritt"] = "ProgressDialog",
        ["Funktion gesperrt"] = "FunctionLockedOk",
        ["Startseite"] = "HomeNav",
        ["Home"] = "HomeNav"
    };

    private static readonly (string Key, string[] Needles)[] NameHints =
    [
        ("DateFromField", ["zugangsdatum", "datum von", "gültig von", "gueltig von", "zeitraum von", "von datum"]),
        ("DateToField", ["datum bis", "gültig bis", "gueltig bis", "zeitraum bis", "bis datum"]),
        ("PeriodFromMonthField", ["periode von", "monat von", "zeitraum monat"]),
        ("PeriodFromYearField", ["jahr von", "geschäftsjahr", "geschaeftsjahr"]),
        ("PeriodToMonthField", ["periode bis", "monat bis"]),
        ("PeriodToYearField", ["jahr bis"]),
        ("FiscalYearField", ["geschäftsjahr", "geschaeftsjahr", "jahr von"]),
        ("DepartmentField", ["fachbereich", "handelsrecht", "steuerrecht"]),
        ("ExecuteButton", ["ausführen", "ausfuehren"]),
        ("SaveButton", ["speichern"]),
        ("ExportButton", ["erweitert", "gitterbox", "exportieren"]),
        ("ExportTargetExcel", ["excel"]),
        ("ExportRecordsAll", ["alle datensätze", "datensätze alle"]),
        ("ExportFormatXlsx", ["xlsx", "*.xlsx"]),
        ("ExportRun", ["exportieren"]),
        ("ProcessManagerNav", ["prozesse verwalten"]),
        ("ProcessManagerOpen", ["öffnen", "oeffnen"]),
        ("ConfirmYes", ["name='ja'"]),
        ("PrintSelectionStart", ["starten"]),
        ("InternalWindowClose", ["schließen", "schliessen"]),
        ("SpoolMenu", ["liste anzeigen", "druckauswahl"]),
        ("ListeAnzeigenOpen", ["liste anzeigen"]),
        ("SpoolList", ["ausgabeliste", "spool"]),
        ("ProgressDialog", ["fortschritt"]),
        ("FunctionLockedOk", ["funktion gesperrt"]),
        ("ClientField", ["mandant"]),
        ("AssetAccountingMenu", ["anlagenbuchhaltung"]),
        ("AssetAccountingWindowMarker", ["anlagenbuchhaltung"]),
        ("SaveDialogFileName", ["dateiname"]),
        ("SaveDialogConfirm", ["speichern"]),
        ("ReportStatusIndicator", ["fortschritt"]),
        ("ReportReadyText", ["prozess aktualisiert", "fertig"]),
        ("ScreenTitle", ["zugangsliste erstellen", "anlagenspiegel erstellen"]),
        ("StatusText", ["prozess aktualisiert", "status"]),
        ("LoginUsername", ["benutzer", "anmeldename"]),
        ("LoginPassword", ["passwort"]),
        ("LoginButton", ["anmelden"])
    ];

    public static Dictionary<string, string> FromDumpsAndEvents(
        IEnumerable<string> dumps,
        IEnumerable<InspectCaptureRunner.RecordedEvent> events)
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dump in dumps)
            CollectFromDump(dump, found);
        foreach (var evt in events)
            CollectFromEvent(evt, found);

        var titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dump in dumps)
        {
            foreach (Match m in Regex.Matches(dump, @"WINDOW Title='([^']+)'"))
            {
                var title = m.Groups[1].Value;
                if (title.Contains("Zugang", StringComparison.OrdinalIgnoreCase)
                    || title.Contains("Anlage", StringComparison.OrdinalIgnoreCase)
                    || title.Contains("Fortschritt", StringComparison.OrdinalIgnoreCase)
                    || title.Contains("Druck", StringComparison.OrdinalIgnoreCase)
                    || title.Contains("Gitterbox", StringComparison.OrdinalIgnoreCase)
                    || title.Contains("Funktion gesperrt", StringComparison.OrdinalIgnoreCase)
                    || title.Contains("Liste anzeigen", StringComparison.OrdinalIgnoreCase)
                    || title.Contains("Prozesse verwalten", StringComparison.OrdinalIgnoreCase))
                    titles.Add(title);
            }
        }
        if (titles.Count > 0)
            found["KnownDialogTitles"] = string.Join('|', titles.Take(12));

        foreach (var key in WilkenSelectorCatalog.Cs2WorkflowSelectors)
            found.TryAdd(key, "");

        return found;
    }

    public static List<object> ExtractControls(
        IEnumerable<string> dumps,
        IEnumerable<InspectCaptureRunner.RecordedEvent> events)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<object>();
        foreach (var dump in dumps)
        {
            foreach (var line in dump.Split('\n'))
            {
                var id = MatchProp(line, "AutomationId");
                var name = MatchProp(line, "Name");
                var help = MatchProp(line, "Help");
                var legacy = MatchProp(line, "Legacy");
                var cls = MatchProp(line, "Class");
                var type = MatchControlType(line);
                if (string.IsNullOrWhiteSpace(id)
                    && string.IsNullOrWhiteSpace(name)
                    && string.IsNullOrWhiteSpace(help)
                    && string.IsNullOrWhiteSpace(legacy))
                    continue;
                var key = $"{type}|{id}|{name}|{help}|{legacy}";
                if (!seen.Add(key)) continue;
                list.Add(new
                {
                    controlType = type,
                    automationId = id,
                    name,
                    helpText = help,
                    legacyName = legacy,
                    className = cls,
                    selector = UiaTreeDumper.PublicSuggestSelector(id, name, cls, help, legacy)
                });
            }
        }
        foreach (var evt in events)
        {
            var key = $"{evt.ControlType}|{evt.AutomationId}|{evt.Name}|{evt.HelpText}|{evt.LegacyName}";
            if (!seen.Add(key)) continue;
            list.Add(new
            {
                controlType = evt.ControlType,
                automationId = evt.AutomationId,
                name = evt.Name,
                helpText = evt.HelpText,
                legacyName = evt.LegacyName,
                className = evt.ClassName,
                selector = evt.Selector,
                recorded = true
            });
        }
        return list;
    }

    public static Dictionary<string, string>? LoadLatestProposal(string? inspectRoot = null)
    {
        inspectRoot ??= Path.Combine(AppContext.BaseDirectory, "Logs", "Inspect");
        if (!Directory.Exists(inspectRoot)) return null;
        var latest = new DirectoryInfo(inspectRoot)
            .GetDirectories()
            .OrderByDescending(d => d.Name)
            .FirstOrDefault();
        if (latest is null) return null;
        var path = Path.Combine(latest.FullName, "proposed-selectors.json");
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
    }

    private static void CollectFromDump(string dump, Dictionary<string, string> found)
    {
        string? pendingLabel = null;
        var zeitraumEdits = 0;
        foreach (var line in dump.Split('\n'))
        {
            var id = MatchProp(line, "AutomationId");
            var name = MatchProp(line, "Name");
            var help = MatchProp(line, "Help");
            var legacy = MatchProp(line, "Legacy");
            var cls = MatchProp(line, "Class");
            var type = MatchControlType(line);
            var haystack = $"{type} {name} {help} {legacy} {line}";
            var selector = line.Contains("-> ")
                ? line[(line.LastIndexOf("-> ", StringComparison.Ordinal) + 3)..].Trim()
                : UiaTreeDumper.PublicSuggestSelector(id, name, cls, help, legacy);

            var labelText = $"{name} {help} {legacy}".Trim();
            if (type is "Text" or "Header" or "HeaderItem" or "Text")
            {
                if (!string.IsNullOrWhiteSpace(labelText))
                    pendingLabel = labelText;
                if (labelText.Contains("Zeitraum", StringComparison.OrdinalIgnoreCase))
                    zeitraumEdits = 0;
            }

            if (string.IsNullOrWhiteSpace(selector)) continue;
            TryAssign(found, FirstNonEmpty(name, legacy, help), selector, haystack);

            var isEdit = type is "Edit" or "ComboBox" or "Spinner" or "Document"
                || haystack.Contains("Patterns=Value", StringComparison.OrdinalIgnoreCase)
                || haystack.Contains("Value,", StringComparison.OrdinalIgnoreCase);
            if (isEdit)
            {
                var context = $"{pendingLabel} {labelText}";
                TryAssign(found, pendingLabel, selector, context);
                if (ContainsAny(context, "zugangsdatum", "datum von", "gültig von", "von"))
                    found.TryAdd("DateFromField", selector);
                if (ContainsAny(context, "datum bis", "gültig bis", "bis"))
                    found.TryAdd("DateToField", selector);
                if (ContainsAny(context, "fachbereich"))
                    found.TryAdd("DepartmentField", selector);
                if (pendingLabel is not null
                    && pendingLabel.Contains("Zeitraum", StringComparison.OrdinalIgnoreCase))
                {
                    var periodKey = zeitraumEdits switch
                    {
                        0 => "PeriodFromMonthField",
                        1 => "PeriodFromYearField",
                        2 => "PeriodToMonthField",
                        3 => "PeriodToYearField",
                        _ => null
                    };
                    if (periodKey is not null)
                        found.TryAdd(periodKey, selector);
                    zeitraumEdits++;
                }
            }
        }
    }

    private static void CollectFromEvent(InspectCaptureRunner.RecordedEvent evt, Dictionary<string, string> found)
    {
        if (string.IsNullOrWhiteSpace(evt.Selector)) return;
        var haystack = $"{evt.ControlType} {evt.Name} {evt.HelpText} {evt.LegacyName} {evt.Value}";
        TryAssign(found, FirstNonEmpty(evt.Name, evt.LegacyName, evt.HelpText), evt.Selector, haystack);
        if (evt.CanInvoke)
            TryAssign(found, evt.Name, evt.Selector, "button " + haystack);
        if (evt.CanSetValue)
            TryAssign(found, evt.Name, evt.Selector, "edit " + haystack);
    }

    private static string? FirstNonEmpty(params string?[] parts) =>
        parts.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));

    private static string MatchControlType(string line)
    {
        var m = Regex.Match(line, @"\[([A-Za-z]+)\]");
        return m.Success ? m.Groups[1].Value : "";
    }

    private static readonly HashSet<string> InputFieldKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "DateFromField", "DateToField",
        "PeriodFromMonthField", "PeriodFromYearField", "PeriodToMonthField", "PeriodToYearField",
        "FiscalYearField", "DepartmentField",
        "ProcessManagerGrid", "SpoolList"
    };

    private static void TryAssign(Dictionary<string, string> found, string? name, string selector, string haystack)
    {
        var value = selector.Trim();
        var isLabel = haystack.Contains("[Text]", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("[Header]", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("[HeaderItem]", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(name) && ExactGermanNames.TryGetValue(name.Trim(), out var exactKey))
        {
            if (!(isLabel && InputFieldKeys.Contains(exactKey)))
                found.TryAdd(exactKey, value);
        }

        var text = $"{name} {haystack}".ToLowerInvariant();
        foreach (var (key, needles) in NameHints)
        {
            if (isLabel && InputFieldKeys.Contains(key))
                continue;
            if (found.TryGetValue(key, out var existing) && !string.IsNullOrWhiteSpace(existing))
                continue;
            if (needles.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase)))
                found[key] = value;
        }

        if ((haystack.Contains("[DataGrid", StringComparison.OrdinalIgnoreCase)
             || haystack.Contains("[Table", StringComparison.OrdinalIgnoreCase))
            && (text.Contains("spool", StringComparison.OrdinalIgnoreCase)
                || text.Contains("ausgabeliste", StringComparison.OrdinalIgnoreCase)
                || text.Contains("liste anzeigen", StringComparison.OrdinalIgnoreCase)))
        {
            found.TryAdd("SpoolList", value);
        }

        if (haystack.Contains("[DataGrid", StringComparison.OrdinalIgnoreCase)
            && text.Contains("prozesse", StringComparison.OrdinalIgnoreCase))
        {
            found.TryAdd("ProcessManagerGrid", value);
        }

        if (haystack.Contains("[RadioButton", StringComparison.OrdinalIgnoreCase)
            || text.Contains("radiobutton", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(name?.Trim(), "Excel", StringComparison.OrdinalIgnoreCase))
                found.TryAdd("ExportTargetExcel", value);
            if (string.Equals(name?.Trim(), "Alle", StringComparison.OrdinalIgnoreCase))
                found.TryAdd("ExportRecordsAll", value);
            if (name is not null && name.Contains("XLSX", StringComparison.OrdinalIgnoreCase))
                found.TryAdd("ExportFormatXlsx", value);
        }
    }

    private static string MatchProp(string line, string prop)
    {
        var m = Regex.Match(line, $@"{prop}='([^']*)'");
        return m.Success ? m.Groups[1].Value : "";
    }
}

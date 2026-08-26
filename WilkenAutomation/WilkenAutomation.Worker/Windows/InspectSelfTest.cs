using System.Text.Json;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Worker.Windows;

internal static class InspectSelfTest
{
    public static int Run()
    {
        var failures = 0;
        void Check(string name, bool ok, string detail = "")
        {
            if (ok)
            {
                Console.WriteLine($"  PASS  {name}");
                return;
            }
            failures++;
            Console.WriteLine($"  FAIL  {name}  {detail}");
        }

        Console.WriteLine("SELF-TEST 1: German inspect dump → CS/2 Wilken:Selectors");
        var dump = """
            WINDOW Title='1/02 - Wilken_CS/2_Finanzmanagement'
            [TreeItem] AutomationId='' Name='Prozesse verwalten' Help='' Legacy='' Value='' Class='' -> Name:Prozesse verwalten
            [DataGrid] AutomationId='pmGrid' Name='Prozesse verwalten' Help='' Legacy='' Value='' Class='DataGrid' -> AutomationId:pmGrid
            [Button] AutomationId='' Name='Öffnen' Help='' Legacy='' Value='' Class='' -> Name:Öffnen
            [TreeItem] AutomationId='' Name='Startseite' Help='' Legacy='' Value='' Class='' -> Name:Startseite
            [Text] AutomationId='' Name='Fachbereich' Help='' Legacy='' Value='' Class=''
            [ComboBox] AutomationId='fb1' Name='' Help='Fachbereich' Legacy='' Value='Handelsrecht' Class='' -> AutomationId:fb1
            [Text] AutomationId='' Name='Zugangsdatum' Help='' Legacy='' Value='' Class=''
            [Edit] AutomationId='dfrom' Name='' Help='' Legacy='' Value='01.01.2020' Class='' -> AutomationId:dfrom
            [Text] AutomationId='' Name='bis' Help='' Legacy='' Value='' Class=''
            [Edit] AutomationId='dto' Name='' Help='' Legacy='' Value='31.12.2020' Class='' -> AutomationId:dto
            [Text] AutomationId='' Name='Zeitraum' Help='' Legacy='' Value='' Class=''
            [Edit] AutomationId='p0' Name='' Help='' Legacy='' Value='01' Class='' -> AutomationId:p0
            [Edit] AutomationId='p1' Name='' Help='' Legacy='' Value='2020' Class='' -> AutomationId:p1
            [Edit] AutomationId='p2' Name='' Help='' Legacy='' Value='12' Class='' -> AutomationId:p2
            [Edit] AutomationId='p3' Name='' Help='' Legacy='' Value='2020' Class='' -> AutomationId:p3
            [Button] AutomationId='' Name='Speichern' Help='' Legacy='' Value='' Class='' -> Name:Speichern
            [Button] AutomationId='' Name='Ausführen' Help='' Legacy='' Value='' Class='' -> Name:Ausführen
            [Button] AutomationId='' Name='Ja' Help='' Legacy='' Value='' Class='' -> Name:Ja
            [Button] AutomationId='' Name='Nein' Help='' Legacy='' Value='' Class='' -> Name:Nein
            [Window] AutomationId='' Name='Fortschritt' Help='' Legacy='' Value='' Class='' -> Name:Fortschritt
            [Window] AutomationId='' Name='Funktion gesperrt' Help='' Legacy='' Value='' Class='' -> Name:Funktion gesperrt
            [TreeItem] AutomationId='' Name='Liste anzeigen' Help='' Legacy='' Value='' Class='' -> Name:Liste anzeigen
            [Button] AutomationId='' Name='Starten' Help='' Legacy='' Value='' Class='' -> Name:Starten
            [DataGrid] AutomationId='spool1' Name='Ausgabeliste' Help='' Legacy='' Value='' Class='DataGrid' -> AutomationId:spool1
            [Button] AutomationId='' Name='Erweitert' Help='' Legacy='' Value='' Class='' -> Name:Erweitert
            [RadioButton] AutomationId='' Name='Excel' Help='' Legacy='' Value='' Class='' -> Name:Excel
            [RadioButton] AutomationId='' Name='Alle' Help='' Legacy='' Value='' Class='' -> Name:Alle
            [RadioButton] AutomationId='' Name='XLSX' Help='' Legacy='' Value='' Class='' -> Name:XLSX
            [Button] AutomationId='' Name='Schließen' Help='' Legacy='' Value='' Class='' -> Name:Schließen
            """;

        var mapped = SelectorProposer.FromDumpsAndEvents([dump], Array.Empty<InspectCaptureRunner.RecordedEvent>());
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ProcessManagerNav"] = "Name:Prozesse verwalten",
            ["ProcessManagerGrid"] = "AutomationId:pmGrid",
            ["ProcessManagerOpen"] = "Name:Öffnen",
            ["HomeNav"] = "Name:Startseite",
            ["DepartmentField"] = "AutomationId:fb1",
            ["DateFromField"] = "AutomationId:dfrom",
            ["DateToField"] = "AutomationId:dto",
            ["PeriodFromMonthField"] = "AutomationId:p0",
            ["PeriodFromYearField"] = "AutomationId:p1",
            ["PeriodToMonthField"] = "AutomationId:p2",
            ["PeriodToYearField"] = "AutomationId:p3",
            ["SaveButton"] = "Name:Speichern",
            ["ExecuteButton"] = "Name:Ausführen",
            ["ConfirmYes"] = "Name:Ja",
            ["ConfirmNo"] = "Name:Nein",
            ["ProgressDialog"] = "Name:Fortschritt",
            ["SpoolMenu"] = "Name:Liste anzeigen",
            ["PrintSelectionStart"] = "Name:Starten",
            ["SpoolList"] = "AutomationId:spool1",
            ["ExportButton"] = "Name:Erweitert",
            ["ExportTargetExcel"] = "Name:Excel",
            ["ExportRecordsAll"] = "Name:Alle",
            ["ExportFormatXlsx"] = "Name:XLSX",
            ["InternalWindowClose"] = "Name:Schließen"
        };

        foreach (var (key, want) in expected)
        {
            mapped.TryGetValue(key, out var got);
            Check(key, string.Equals(got, want, StringComparison.OrdinalIgnoreCase), $"got '{got}' want '{want}'");
        }

        Check("SpoolList is not the process grid",
            mapped.GetValueOrDefault("SpoolList") != "AutomationId:pmGrid");

        Console.WriteLine();
        Console.WriteLine("SELF-TEST 2: appsettings.json contains every CS/2 key");
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(settingsPath))
        {
            Check("appsettings.json exists", false, settingsPath);
        }
        else
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
            var selectors = doc.RootElement.GetProperty("Wilken").GetProperty("Selectors");
            foreach (var key in WilkenSelectorCatalog.Cs2WorkflowSelectors)
                Check($"appsettings {key}", selectors.TryGetProperty(key, out _));
            Check("AttachOnly",
                doc.RootElement.GetProperty("Wilken").GetProperty("AttachOnly").GetBoolean());
            Check("SkipLogin",
                doc.RootElement.GetProperty("Wilken").GetProperty("SkipLogin").GetBoolean());
            Check("xlsx export",
                doc.RootElement.GetProperty("Export").GetProperty("FileExtension").GetString()
                    ?.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) == true);
        }

        Console.WriteLine();
        Console.WriteLine("SELF-TEST 3: isolated --apply-selectors merge");
        var tempRoot = Path.Combine(Path.GetTempPath(), "WilkenSelfTest-" + Guid.NewGuid().ToString("N"));
        var inspectDir = Path.Combine(tempRoot, "Logs", "Inspect", "20990101-000000");
        Directory.CreateDirectory(inspectDir);
        File.WriteAllText(Path.Combine(inspectDir, "proposed-selectors.json"), JsonSerializer.Serialize(mapped));
        var tempSettings = Path.Combine(tempRoot, "appsettings.json");
        File.WriteAllText(tempSettings, """{ "Wilken": { "Selectors": { "ExecuteButton": "" } } }""");
        var apply = SelectorApply.Run(force: true, inspectRoot: Path.Combine(tempRoot, "Logs", "Inspect"), settingsPath: tempSettings);
        Check("apply exit 0", apply == 0);
        using (var applied = JsonDocument.Parse(File.ReadAllText(tempSettings)))
        {
            var sel = applied.RootElement.GetProperty("Wilken").GetProperty("Selectors");
            Check("applied DateFromField",
                sel.TryGetProperty("DateFromField", out var df) && df.GetString() == "AutomationId:dfrom");
            Check("applied ExecuteButton",
                sel.TryGetProperty("ExecuteButton", out var ex) && ex.GetString() == "Name:Ausführen");
        }

        try { Directory.Delete(tempRoot, recursive: true); } catch { }

        Console.WriteLine();
        if (failures == 0)
        {
            Console.WriteLine("SELF-TEST passed. Real Citrix Wilken cannot be driven from this PC — run inspect on the Test desktop.");
            return 0;
        }

        Console.WriteLine($"SELF-TEST failed: {failures} check(s).");
        return 1;
    }
}

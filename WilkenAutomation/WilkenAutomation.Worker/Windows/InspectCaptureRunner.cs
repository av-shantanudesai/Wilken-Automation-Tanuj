using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Worker.Windows;

/// <summary>
/// Watch: snapshot every interesting desktop window when its UIA tree changes.
/// Record: also log focus / invoke-capable controls while the user exports one job by hand.
/// Output: Logs/Inspect/&lt;session&gt;/
/// </summary>
internal static class InspectCaptureRunner
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static int Run(bool record, int intervalMs, int durationSeconds = 0, bool autoApply = true)
    {
        intervalMs = Math.Clamp(intervalMs, 400, 10_000);
        var sessionDir = Path.Combine(AppContext.BaseDirectory, "Logs", "Inspect", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(sessionDir);

        File.WriteAllText(Path.Combine(sessionDir, "00_README.txt"),
            """
            Inspect capture session
            -----------------------
            1. Log in to Wilken yourself (EHP + Anmeldung). Leave the main screen visible.
            2. Start this capture, then slowly perform ONE full German export by hand.
               Click every control: Prozesse verwalten, Öffnen, Fachbereich,
               von/bis dates, Zeitraum (4 boxes), Speichern, Ausführen, Ja,
               Liste anzeigen, Starten, Erweitert, Excel, Alle, inner Schließen.
            3. Press Ctrl+C when the export file is saved.
            4. On Ctrl+C, proposed-selectors.json is written and merged into appsettings.json.
            5. Review DateFromField / DateToField / Period* / DepartmentField in appsettings.json.
            6. Seed a job if needed: WilkenAutomation.Worker.exe --seed-jobs --client 02 --year 2020
            7. Start the worker with no inspect flags so it claims the job.
            """);

        Console.WriteLine();
        Console.WriteLine("============================================================");
        Console.WriteLine(record
            ? "MODE: inspect-watch + record — you click Wilken; this process only dumps UIA."
            : "MODE: inspect-watch — snapshots windows when the UI tree changes.");
        Console.WriteLine($"Folder: {sessionDir}");
        Console.WriteLine("Click every German control slowly. Dumps are deep (Help/Legacy/Value + IDs).");
        Console.WriteLine("On Ctrl+C, selectors are proposed and merged into appsettings.json.");
        if (durationSeconds > 0)
            Console.WriteLine($"Auto-stop after {durationSeconds} seconds (--duration).");
        Console.WriteLine("============================================================");
        Console.WriteLine();

        using var automation = new UIA3Automation();
        File.WriteAllText(Path.Combine(sessionDir, "00_windows_list.txt"), UiaTreeDumper.ListTopLevelWindows());

        var lastHashByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dumpCount = 0;
        var events = new ConcurrentBag<RecordedEvent>();
        var wilkenPids = new HashSet<int>();
        using var cts = new CancellationTokenSource();
        if (durationSeconds > 0)
            cts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(durationSeconds, 2, 3600)));
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        if (record)
        {
            try
            {
                automation.RegisterFocusChangedEvent(el =>
                {
                    var evt = Describe(el, "Focus");
                    if (evt is not null) events.Add(evt);
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Focus events unavailable ({ex.Message}). Watch dumps still run.");
            }
        }

        try
        {
            while (!cts.IsCancellationRequested)
            {
                CaptureTick(automation, sessionDir, lastHashByKey, wilkenPids, ref dumpCount, events, record);
                WriteSessionSummary(sessionDir, events.OrderBy(e => e.At).ToList(), dumpCount, record);
                cts.Token.WaitHandle.WaitOne(intervalMs);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"Capture stopped: {ex.Message}");
        }
        finally
        {
            try { automation.UnregisterAllEvents(); } catch { }
        }

        WriteSessionSummary(sessionDir, events.OrderBy(e => e.At).ToList(), dumpCount, record);
        Console.WriteLine();
        Console.WriteLine($"Stopped. Dumps: {dumpCount}. Folder: {sessionDir}");
        if (autoApply)
        {
            Console.WriteLine("Merging proposed-selectors.json into appsettings.json …");
            SelectorApply.Run(force: false);
        }
        else
        {
            Console.WriteLine("Next: WilkenAutomation.Worker.exe --apply-selectors");
        }
        Console.WriteLine("Then: WilkenAutomation.Worker.exe --seed-jobs --client 02 --year 2020");
        return 0;
    }

    private static void CaptureTick(
        UIA3Automation automation,
        string sessionDir,
        Dictionary<string, string> lastHashByKey,
        HashSet<int> wilkenPids,
        ref int dumpCount,
        ConcurrentBag<RecordedEvent> events,
        bool record)
    {
        List<UiaTreeDumper.TopLevelWindow> windows;
        try { windows = UiaTreeDumper.GetTopLevelWindows(automation, includeUntitled: true); }
        catch { return; }

        if (record)
        {
            try
            {
                var focused = automation.FocusedElement();
                var evt = Describe(focused, "FocusedPoll");
                if (evt is not null) events.Add(evt);
            }
            catch { }
        }

        foreach (var window in windows)
        {
            if (window.IsRemoteDisplay) continue;
            if (LooksLikeWilkenOrDialog(window) || wilkenPids.Contains(window.Pid))
                wilkenPids.Add(window.Pid);
            else
                continue;

            string fingerprint;
            try { fingerprint = UiaTreeDumper.WindowFingerprint(window); }
            catch { continue; }

            var key = $"{window.Pid}|{window.ClassName}|{window.Title}";
            var hash = Sha256(fingerprint);
            if (lastHashByKey.TryGetValue(key, out var previous) && previous == hash)
                continue;
            lastHashByKey[key] = hash;

            dumpCount++;
            var name = $"{dumpCount:000}_{Sanitize(window.Title)}.txt";
            var path = Path.Combine(sessionDir, name);
            try
            {
                File.WriteAllText(path, UiaTreeDumper.DumpWindow(window, maxDepth: 16));
                Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} DUMP {name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} DUMP failed {window.Title}: {ex.Message}");
            }

            if (dumpCount >= 250)
            {
                Console.WriteLine("Reached 250 dumps — stop with Ctrl+C.");
            }
        }
    }

    private static bool LooksLikeWilkenOrDialog(UiaTreeDumper.TopLevelWindow window)
    {
        if (window.IsRemoteDisplay) return false;

        var process = window.ProcessName;
        if (process.Equals("Cursor", StringComparison.OrdinalIgnoreCase)
            || process.Equals("devenv", StringComparison.OrdinalIgnoreCase)
            || process.Equals("Code", StringComparison.OrdinalIgnoreCase)
            || process.Equals("explorer", StringComparison.OrdinalIgnoreCase)
            || process.Equals("olk", StringComparison.OrdinalIgnoreCase)
            || process.Equals("ms-teams", StringComparison.OrdinalIgnoreCase))
            return false;

        var t = window.Title;
        if (t.Contains("Wilken_CS/2", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Wilken CS/2", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Finanzmanagement", StringComparison.OrdinalIgnoreCase)
            || t.Contains("EHP 2", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Anmeldung", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Gitterbox", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Druckauswahl", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Fortschritt", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Zugangsliste", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Anlagenspiegel", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Prozesse verwalten", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Liste anzeigen", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Zeitraum", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Fachbereich", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Wilken", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Funktion gesperrt", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Speichern unter", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Save As", StringComparison.OrdinalIgnoreCase))
            return true;

        return process.Contains("Wilken", StringComparison.OrdinalIgnoreCase)
            || process.Contains("cs2", StringComparison.OrdinalIgnoreCase)
            || process.Contains("ehp", StringComparison.OrdinalIgnoreCase);
    }

    private static RecordedEvent? Describe(AutomationElement? element, string kind)
    {
        if (element is null) return null;
        try
        {
            var pid = element.Properties.ProcessId.ValueOrDefault;
            var process = "";
            try { if (pid > 0) process = Process.GetProcessById(pid).ProcessName; } catch { }
            if (WilkenSessionPolicy.IsRemoteDisplayProcess(process)
                || process.Equals("Cursor", StringComparison.OrdinalIgnoreCase)
                || process.Equals("devenv", StringComparison.OrdinalIgnoreCase)
                || process.Equals("Code", StringComparison.OrdinalIgnoreCase)
                || process.Equals("explorer", StringComparison.OrdinalIgnoreCase)
                || process.Equals("olk", StringComparison.OrdinalIgnoreCase)
                || process.Equals("ms-teams", StringComparison.OrdinalIgnoreCase))
                return null;

            var id = element.Properties.AutomationId.ValueOrDefault ?? "";
            var name = element.Properties.Name.ValueOrDefault ?? "";
            var type = element.Properties.ControlType.ValueOrDefault.ToString();
            var cls = element.Properties.ClassName.ValueOrDefault ?? "";
            var help = "";
            var legacy = "";
            var value = "";
            try { help = element.Properties.HelpText.ValueOrDefault ?? ""; } catch { }
            try
            {
                if (element.Patterns.LegacyIAccessible.IsSupported)
                    legacy = element.Patterns.LegacyIAccessible.Pattern.Name ?? "";
            }
            catch { }
            try
            {
                if (element.Patterns.Value.IsSupported)
                    value = element.Patterns.Value.Pattern.Value.ValueOrDefault ?? "";
            }
            catch { }
            if (string.IsNullOrWhiteSpace(id)
                && string.IsNullOrWhiteSpace(name)
                && string.IsNullOrWhiteSpace(help)
                && string.IsNullOrWhiteSpace(legacy))
                return null;
            var invoke = false;
            var setValue = false;
            try { invoke = element.Patterns.Invoke.IsSupported; } catch { }
            try { setValue = element.Patterns.Value.IsSupported; } catch { }
            return new RecordedEvent
            {
                At = DateTime.Now,
                Kind = kind,
                ControlType = type,
                AutomationId = id,
                Name = name,
                HelpText = help,
                LegacyName = legacy,
                Value = value,
                ClassName = cls,
                Selector = UiaTreeDumper.PublicSuggestSelector(id, name, cls, help, legacy),
                CanInvoke = invoke,
                CanSetValue = setValue
            };
        }
        catch
        {
            return null;
        }
    }

    private static void WriteSessionSummary(string sessionDir, List<RecordedEvent> events, int dumpCount, bool record)
    {
        if (record)
        {
            var distinct = DedupEvents(events);
            File.WriteAllText(Path.Combine(sessionDir, "recording.json"), JsonSerializer.Serialize(new
            {
                capturedAt = DateTime.Now,
                dumpCount,
                events = distinct
            }, Json));
            File.WriteAllText(Path.Combine(sessionDir, "events.jsonl"),
                string.Join(Environment.NewLine, distinct.Select(e => JsonSerializer.Serialize(e))));
        }

        var dumps = Directory.GetFiles(sessionDir, "*.txt")
            .Where(p =>
            {
                var file = Path.GetFileName(p);
                return file.Length > 4 && char.IsDigit(file[0]) && char.IsDigit(file[1]) && char.IsDigit(file[2]) && file[3] == '_';
            })
            .Select(File.ReadAllText)
            .ToList();
        var proposal = SelectorProposer.FromDumpsAndEvents(dumps, events);
        File.WriteAllText(Path.Combine(sessionDir, "proposed-selectors.json"), JsonSerializer.Serialize(proposal, Json));
        File.WriteAllText(Path.Combine(sessionDir, "proposed-appsettings-fragment.json"),
            JsonSerializer.Serialize(new { Wilken = new { Selectors = proposal } }, Json));
        File.WriteAllText(Path.Combine(sessionDir, "captured-controls.json"),
            JsonSerializer.Serialize(SelectorProposer.ExtractControls(dumps, events), Json));
    }

    private static List<RecordedEvent> DedupEvents(List<RecordedEvent> events)
    {
        var result = new List<RecordedEvent>();
        RecordedEvent? last = null;
        foreach (var evt in events)
        {
            if (last is not null
                && last.Kind == evt.Kind
                && last.AutomationId == evt.AutomationId
                && last.Name == evt.Name
                && (evt.At - last.At).TotalMilliseconds < 400)
                continue;
            result.Add(evt);
            last = evt;
        }
        return result;
    }

    private static string Sanitize(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = title.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var name = new string(chars).Trim();
        if (name.Length > 60) name = name[..60];
        return string.IsNullOrWhiteSpace(name) ? "window" : name;
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    internal sealed class RecordedEvent
    {
        public DateTime At { get; set; }
        public string Kind { get; set; } = "";
        public string ControlType { get; set; } = "";
        public string AutomationId { get; set; } = "";
        public string Name { get; set; } = "";
        public string HelpText { get; set; } = "";
        public string LegacyName { get; set; } = "";
        public string Value { get; set; } = "";
        public string ClassName { get; set; } = "";
        public string Selector { get; set; } = "";
        public bool CanInvoke { get; set; }
        public bool CanSetValue { get; set; }
    }
}

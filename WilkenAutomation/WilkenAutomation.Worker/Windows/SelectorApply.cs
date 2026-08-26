using System.Text.Json;
using System.Text.Json.Nodes;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Worker.Windows;

/// <summary>
/// Merges Logs/Inspect/&lt;latest&gt;/proposed-selectors.json into appsettings.json
/// next to the published worker exe. Existing non-empty selectors are kept unless --force.
/// </summary>
internal static class SelectorApply
{
    public static int Run(bool force, string? inspectRoot = null, string? settingsPath = null)
    {
        var proposal = SelectorProposer.LoadLatestProposal(inspectRoot);
        if (proposal is null || proposal.Count == 0)
        {
            Console.Error.WriteLine("No proposed-selectors.json under Logs/Inspect. Run --inspect-watch first.");
            return 2;
        }

        settingsPath ??= Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(settingsPath))
        {
            Console.Error.WriteLine($"appsettings.json not found at {settingsPath}");
            return 2;
        }

        var root = JsonNode.Parse(File.ReadAllText(settingsPath))?.AsObject()
            ?? throw new InvalidOperationException("appsettings.json is not a JSON object.");
        root["Wilken"] ??= new JsonObject();
        var wilken = root["Wilken"]!.AsObject();
        wilken["Selectors"] ??= new JsonObject();
        var selectors = wilken["Selectors"]!.AsObject();

        var applied = 0;
        var skipped = 0;
        foreach (var (key, value) in proposal)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            var current = selectors[key]?.GetValue<string>() ?? "";
            if (!force && !string.IsNullOrWhiteSpace(current))
            {
                skipped++;
                continue;
            }
            selectors[key] = value;
            applied++;
            Console.WriteLine($"  {key} = {value}");
        }

        foreach (var key in WilkenSelectorCatalog.Cs2WorkflowSelectors)
            selectors[key] ??= "";

        var backup = settingsPath + ".bak";
        File.Copy(settingsPath, backup, overwrite: true);
        File.WriteAllText(settingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Applied {applied} selector(s), skipped {skipped} existing. Backup: {backup}");

        var stillEmpty = WilkenSelectorCatalog.MissingCs2(
            selectors.ToDictionary(p => p.Key, p => p.Value?.GetValue<string>() ?? "", StringComparer.OrdinalIgnoreCase));
        if (stillEmpty.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Still empty (click these German controls during --inspect-record, then --apply-selectors --force if needed):");
            foreach (var key in stillEmpty)
                Console.WriteLine($"  {key}");
        }
        else
        {
            Console.WriteLine("All CS/2 Wilken:Selectors have a value. Review DateFromField / DateToField / DepartmentField.");
        }

        Console.WriteLine("Restart the worker (no inspect flags) so it loads the new selectors.");
        return 0;
    }
}

using System.Globalization;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Worker.Wilken;

/// <summary>
/// Gitterbox export: force XLSX / Excel / Alle, execute, then wait for a stable, unlocked workbook in Downloads or the export folder.
/// </summary>
public partial class WindowsWilkenAutomationService
{
    private async Task<string> ReplicaExportAsync(ExportJob job, CancellationToken ct)
    {
        GuardHealthy();
        var before = SnapshotExportFiles();

        await ReplicaOpenGitterboxViaContextMenuAsync(ct);

        var formatId = ReplicaFormatRadioId();
        SelectRadio(formatId);
        SelectRadio("Export_Target_Excel");
        SelectRadio("Export_Records_All");
        if (IsReplica
            || TryFindByAutomationId(formatId) is not null
            || TryFindByAutomationId("Export_Target_Excel") is not null)
        {
            await WaitUntilUiAsync(
                () => (TryFindByAutomationId(formatId) is null || RadioIsSelected(formatId))
                      && (TryFindByAutomationId("Export_Target_Excel") is null || RadioIsSelected("Export_Target_Excel"))
                      && (TryFindByAutomationId("Export_Records_All") is null || RadioIsSelected("Export_Records_All")),
                TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
                $"{formatId} + Excel + Alle selected", ct);
        }

        InvokeControl(FindByAutomationId("Toolbar_Execute"));
        if (!await TryWaitForExportStartAsync(TimeSpan.FromSeconds(6), ct))
        {
            var run = TryFindByAutomationId("Export_Run") ?? TryFindByAutomationId("Toolbar_Execute");
            if (run is not null)
                InvokeControl(run);
        }

        string? produced = null;
        await WaitUntilUiAsync(() =>
        {
            produced = NewestExportSince(before);
            return produced is not null || ScreenStatusContains("Export abgeschlossen");
        },
        TimeSpan.FromMinutes(_options.ExportTimeoutMinutes),
        "exported workbook", ct);

        produced ??= NewestExportSince(before);
        if (produced is null)
            throw new WilkenAutomationException("DOWNLOAD_TIMEOUT",
                "Gitterbox export finished in the UI but no new .xlsx was found in Downloads or the export folder.");

        // File appears -> size stops changing -> file is unlocked -> only then validate.
        _lastObservedExportSize = -1;
        await WaitUntilUiAsync(
            () => FileIsStableAndUnlocked(produced!),
            TimeSpan.FromMinutes(Math.Max(1, _options.ExportTimeoutMinutes)),
            "export file size stable and unlocked", ct);

        _logger.LogInformation("CS/2 export produced {Path} for {JobId}.", produced, job.JobId);

        await ReplicaReturnToProcessManagerAsync(ct);
        return produced!;
    }

    private long _lastObservedExportSize = -1;

    /// <summary>
    /// True only when the file has kept the same non-zero size across two polls
    /// and can be opened exclusively (writer has released it).
    /// </summary>
    private bool FileIsStableAndUnlocked(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0)
            {
                _lastObservedExportSize = -1;
                return false;
            }
            if (info.Length != _lastObservedExportSize)
            {
                _lastObservedExportSize = info.Length;
                return false;
            }
            using var _ = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private IEnumerable<string> ExportSearchFolders()
    {
        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(downloads);
        yield return downloads;
        if (!string.IsNullOrWhiteSpace(_exportSettings.RootDirectory))
        {
            Directory.CreateDirectory(_exportSettings.RootDirectory);
            yield return _exportSettings.RootDirectory;
        }
    }

    private string[] ExportFilePatterns() =>
        IsReplica ? ["CTLP12*.xlsx"] : ["*.xlsx", "*.xls"];

    private List<(string Path, DateTime Time)> SnapshotExportFiles()
    {
        var before = new List<(string Path, DateTime Time)>();
        foreach (var folder in ExportSearchFolders())
        {
            foreach (var pattern in ExportFilePatterns())
            {
                try
                {
                    before.AddRange(Directory.GetFiles(folder, pattern)
                        .Select(f => (Path: f, Time: File.GetLastWriteTimeUtc(f))));
                }
                catch { }
            }
        }
        return before;
    }

    private string? NewestExportSince(List<(string Path, DateTime Time)> before)
    {
        var candidates = new List<FileInfo>();
        foreach (var folder in ExportSearchFolders())
        {
            foreach (var pattern in ExportFilePatterns())
            {
                try
                {
                    candidates.AddRange(Directory.GetFiles(folder, pattern).Select(f => new FileInfo(f)));
                }
                catch { }
            }
        }

        return candidates
            .Where(f => f.Length > 0
                        && (before.All(b => b.Path != f.FullName)
                            || File.GetLastWriteTimeUtc(f.FullName) > _runStartedAtUtc.AddSeconds(-5)))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => f.FullName)
            .FirstOrDefault();
    }

    // CS/2 exports are always forced to XLSX regardless of the configured extension.
    private static string ReplicaFormatRadioId() => "Export_Format_XLSX";

    private async Task<bool> TryWaitForExportStartAsync(TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            await WaitUntilUiAsync(
                () => ScreenStatusContains("exportiert") || ScreenStatusContains("Export abgeschlossen"),
                timeout,
                "export start", ct);
            return true;
        }
        catch (WaitTimeoutException)
        {
            return false;
        }
    }
}

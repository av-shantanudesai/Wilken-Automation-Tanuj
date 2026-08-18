using WilkenExport.Api.Domain;

namespace WilkenExport.Api.Engine;

public record ValidationReport(ValidationOutcome Outcome, int? RecordCount, string Detail);

/// <summary>
/// Multi-level validation of the original Wilken export.
///  Level 1: file exists, size &gt; 0, readable, not locked.
///  Level 2: client / fiscal year / department inside the content match the job.
///  Level 3: expected structure (header block, section list, data header, end marker).
///  Level 4: record count consistency; distinguishes valid-empty from valid-with-data.
/// The file itself is never modified.
/// </summary>
public class FileValidator
{
    public async Task<ValidationReport> ValidateAsync(string filePath, ExportJob job, bool contentValidation, CancellationToken ct)
    {
        // Level 1 - technical file validation
        var info = new FileInfo(filePath);
        if (!info.Exists)
            return new(ValidationOutcome.Invalid, null, "File does not exist.");
        if (info.Length == 0)
            return new(ValidationOutcome.Invalid, null, "File size is zero.");
        if (IsLocked(filePath))
            return new(ValidationOutcome.Invalid, null, "File is still locked/being written.");

        if (!contentValidation)
            return new(ValidationOutcome.Valid, null, "Level 1 passed (content validation disabled).");

        string[] lines;
        try
        {
            lines = await File.ReadAllLinesAsync(filePath, ct);
        }
        catch (Exception ex)
        {
            return new(ValidationOutcome.Invalid, null, $"File could not be read: {ex.Message}");
        }

        // Level 3 - structural validation
        var header = ParseHeader(lines);
        if (!header.TryGetValue("Client", out var client) ||
            !header.TryGetValue("FiscalYear", out var year) ||
            !header.TryGetValue("Department", out var department) ||
            !header.TryGetValue("RecordCount", out var recordCountRaw))
            return new(ValidationOutcome.Invalid, null, "Expected header fields (Client/FiscalYear/Department/RecordCount) are missing.");

        if (!lines.Contains("---DATA---"))
            return new(ValidationOutcome.Invalid, null, "Data section marker missing.");
        if (lines.LastOrDefault(l => l.Length > 0) != "---END_OF_REPORT---")
            return new(ValidationOutcome.Invalid, null, "End-of-report marker missing - file appears truncated.");

        // Level 2 - job identity validation from content, not filename
        if (client != job.Client)
            return new(ValidationOutcome.Invalid, null, $"Content client '{client}' does not match job client '{job.Client}'.");
        if (year != job.FiscalYear.ToString())
            return new(ValidationOutcome.Invalid, null, $"Content fiscal year '{year}' does not match job year '{job.FiscalYear}'.");
        if (department != job.Department)
            return new(ValidationOutcome.Invalid, null, $"Content department '{department}' does not match job department '{job.Department}'.");

        // Level 4 - data validation
        if (!int.TryParse(recordCountRaw, out var declaredCount))
            return new(ValidationOutcome.Invalid, null, $"RecordCount '{recordCountRaw}' is not numeric.");

        var dataStart = Array.IndexOf(lines, "---DATA---");
        var dataEnd = Array.IndexOf(lines, "---END_OF_REPORT---");
        var actualCount = Math.Max(0, dataEnd - dataStart - 2); // minus column header line

        if (actualCount != declaredCount)
            return new(ValidationOutcome.Invalid, actualCount,
                $"Declared record count {declaredCount} does not match actual rows {actualCount} - output may be truncated.");

        // Zero records is a legitimate result, not a failure.
        return declaredCount == 0
            ? new(ValidationOutcome.ValidEmpty, 0, "All validation levels passed; period legitimately contains no data.")
            : new(ValidationOutcome.Valid, declaredCount, $"All validation levels passed; {declaredCount} records.");
    }

    private static Dictionary<string, string> ParseHeader(string[] lines)
    {
        var header = new Dictionary<string, string>();
        foreach (var line in lines)
        {
            if (line == "---DATA---") break;
            var idx = line.IndexOf(';');
            if (idx > 0) header[line[..idx]] = line[(idx + 1)..];
        }
        return header;
    }

    private static bool IsLocked(string path)
    {
        try
        {
            using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }
}

using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;

namespace WilkenAutomation.Application.Validators;

/// <summary>
/// Four-level export validation:
///  1. File-level     - exists, size > 0, readable
///  2. Job-level      - content contains the expected client / year / department
///  3. Structural     - expected report marker, data section, header, end marker
///  4. Data-level     - record count; zero records => ValidEmpty, never automatic failure
///
/// Levels 2-4 parse the report metadata header that the mock export writes and
/// that the real Wilken export handler is expected to map onto its own format
/// (for xlsx exports a format-specific reader can replace this implementation
/// behind the same interface).
/// </summary>
public class ExportFileValidator : IExportFileValidator
{
    public const string ReportMarker = "WILKEN CS/2 ASSET ACCOUNTING EXPORT";
    public const string DataMarker = "---DATA---";
    public const string EndMarker = "---END_OF_REPORT---";

    public async Task<FileValidationResult> ValidateAsync(
        string filePath, ExportJob job, bool contentValidation, CancellationToken ct)
    {
        // Level 1 - file
        var info = new FileInfo(filePath);
        if (!info.Exists)
            return new FileValidationResult(ValidationStatus.Invalid, null, "File does not exist.");
        if (info.Length == 0)
            return new FileValidationResult(ValidationStatus.Invalid, null, "File is empty (0 bytes).");

        string[] lines;
        try
        {
            lines = await File.ReadAllLinesAsync(filePath, ct);
        }
        catch (Exception ex)
        {
            return new FileValidationResult(ValidationStatus.Invalid, null, $"File is not readable: {ex.Message}");
        }

        if (!contentValidation)
            return new FileValidationResult(ValidationStatus.Valid, null, "File-level validation only (content validation disabled).");

        // Level 3 - structure
        if (lines.Length == 0 || !lines[0].Contains(ReportMarker))
            return new FileValidationResult(ValidationStatus.Invalid, null, "Missing report marker - unexpected file format.");
        if (!lines.Contains(EndMarker))
            return new FileValidationResult(ValidationStatus.Invalid, null, "Missing end-of-report marker - file appears truncated.");

        var meta = lines
            .TakeWhile(l => l != DataMarker)
            .Where(l => l.Contains(';'))
            .Select(l => l.Split(';', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.OrdinalIgnoreCase);

        // Level 2 - job identity from content, not filename
        if (!meta.TryGetValue("Client", out var client) || client != job.Client)
            return new FileValidationResult(ValidationStatus.Invalid, null,
                $"Client mismatch: file contains '{meta.GetValueOrDefault("Client", "<missing>")}', expected '{job.Client}'.");
        if (!meta.TryGetValue("FiscalYear", out var yearText) || yearText != job.FiscalYear.ToString())
            return new FileValidationResult(ValidationStatus.Invalid, null,
                $"Fiscal year mismatch: file contains '{meta.GetValueOrDefault("FiscalYear", "<missing>")}', expected '{job.FiscalYear}'.");
        if (!meta.TryGetValue("Department", out var department) || department != job.Department)
            return new FileValidationResult(ValidationStatus.Invalid, null,
                $"Department mismatch: file contains '{meta.GetValueOrDefault("Department", "<missing>")}', expected '{job.Department}'.");

        // Level 4 - data
        var dataIndex = Array.IndexOf(lines, DataMarker);
        var endIndex = Array.IndexOf(lines, EndMarker);
        if (dataIndex < 0 || endIndex < dataIndex)
            return new FileValidationResult(ValidationStatus.Invalid, null, "Data section missing or malformed.");

        // Rows between the data marker (followed by one header row) and the end marker.
        var recordCount = Math.Max(0, endIndex - dataIndex - 2);

        if (meta.TryGetValue("RecordCount", out var declared) &&
            int.TryParse(declared, out var declaredCount) && declaredCount != recordCount)
        {
            return new FileValidationResult(ValidationStatus.Invalid, recordCount,
                $"Declared record count {declaredCount} does not match actual {recordCount} - file may be truncated.");
        }

        return recordCount == 0
            ? new FileValidationResult(ValidationStatus.ValidEmpty, 0, "Valid export, period legitimately contains no data.")
            : new FileValidationResult(ValidationStatus.Valid, recordCount, $"Valid export containing {recordCount} records.");
    }
}

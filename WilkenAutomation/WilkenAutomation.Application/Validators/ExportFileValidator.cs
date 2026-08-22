using System.IO.Compression;
using System.Xml.Linq;
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
/// CSV mock exports use the metadata header below. Replica/Wilken XLSX files
/// are validated as Office Open XML packages with sheet row counts.
/// </summary>
public class ExportFileValidator : IExportFileValidator
{
    public const string ReportMarker = "WILKEN CS/2 ASSET ACCOUNTING EXPORT";
    public const string DataMarker = "---DATA---";
    public const string EndMarker = "---END_OF_REPORT---";

    public async Task<FileValidationResult> ValidateAsync(
        string filePath, ExportJob job, bool contentValidation, CancellationToken ct)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists)
            return new FileValidationResult(ValidationStatus.Invalid, null, "File does not exist.");
        if (info.Length == 0)
            return new FileValidationResult(ValidationStatus.Invalid, null, "File is empty (0 bytes).");

        if (filePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return ValidateXlsx(filePath, job, contentValidation);

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

        if (!meta.TryGetValue("Client", out var client) || client != job.Client)
            return new FileValidationResult(ValidationStatus.Invalid, null,
                $"Client mismatch: file contains '{meta.GetValueOrDefault("Client", "<missing>")}', expected '{job.Client}'.");
        if (!meta.TryGetValue("FiscalYear", out var yearText) || yearText != job.FiscalYear.ToString())
            return new FileValidationResult(ValidationStatus.Invalid, null,
                $"Fiscal year mismatch: file contains '{meta.GetValueOrDefault("FiscalYear", "<missing>")}', expected '{job.FiscalYear}'.");
        if (!meta.TryGetValue("Department", out var department) || department != job.Department)
            return new FileValidationResult(ValidationStatus.Invalid, null,
                $"Department mismatch: file contains '{meta.GetValueOrDefault("Department", "<missing>")}', expected '{job.Department}'.");

        var dataIndex = Array.IndexOf(lines, DataMarker);
        var endIndex = Array.IndexOf(lines, EndMarker);
        if (dataIndex < 0 || endIndex < dataIndex)
            return new FileValidationResult(ValidationStatus.Invalid, null, "Data section missing or malformed.");

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

    private static FileValidationResult ValidateXlsx(string filePath, ExportJob job, bool contentValidation)
    {
        try
        {
            using var zip = ZipFile.OpenRead(filePath);
            var sheet = zip.GetEntry("xl/worksheets/sheet1.xml")
                ?? zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)
                                                 && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
            if (sheet is null)
                return new FileValidationResult(ValidationStatus.Invalid, null, "XLSX is missing a worksheet part.");

            using var stream = sheet.Open();
            var xml = XDocument.Load(stream);
            var texts = xml.Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value).ToList();
            var rows = xml.Descendants().Count(e => e.Name.LocalName == "row");
            var recordCount = Math.Max(0, rows - 1);

            if (!contentValidation)
                return new FileValidationResult(
                    recordCount == 0 ? ValidationStatus.ValidEmpty : ValidationStatus.Valid,
                    recordCount,
                    "XLSX package is readable (content validation disabled).");

            var blob = string.Join(" ", texts);
            if (!string.IsNullOrWhiteSpace(job.Department)
                && !blob.Contains(job.Department, StringComparison.OrdinalIgnoreCase))
            {
                return new FileValidationResult(ValidationStatus.Invalid, recordCount,
                    $"Department mismatch: XLSX does not contain '{job.Department}'.");
            }

            return recordCount == 0
                ? new FileValidationResult(ValidationStatus.ValidEmpty, 0, "Valid XLSX, period contains no data rows.")
                : new FileValidationResult(ValidationStatus.Valid, recordCount, $"Valid XLSX containing {recordCount} records.");
        }
        catch (InvalidDataException ex)
        {
            return new FileValidationResult(ValidationStatus.Invalid, null, $"XLSX is not a valid Office Open XML package: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new FileValidationResult(ValidationStatus.Invalid, null, $"XLSX is not a valid Office Open XML package: {ex.Message}");
        }
    }
}

using System.Text.RegularExpressions;
using WilkenAutomation.Application.Models;

namespace WilkenAutomation.Application.Services;

public static class ExportFilename
{
    public static string Render(string template, ExportJob job, DateTime? timestampUtc = null)
    {
        var stamp = (timestampUtc ?? DateTime.UtcNow).ToString("yyyyMMddHHmmss");
        var year = job.FiscalYear > 0 ? job.FiscalYear.ToString() : "";
        var replaced = template
            .Replace("{CLIENT}", job.Client ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{EXPORT}", job.ExportDefinition ?? "Export", StringComparison.OrdinalIgnoreCase)
            .Replace("{YEAR}", year, StringComparison.OrdinalIgnoreCase)
            .Replace("{PERIOD}", job.Period ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{ACCOUNTINGLAW}", Sanitize(job.AccountingLaw ?? job.Department), StringComparison.OrdinalIgnoreCase)
            .Replace("{ACCOUNTING_LAW}", Sanitize(job.AccountingLaw ?? job.Department), StringComparison.OrdinalIgnoreCase)
            .Replace("{TIMESTAMP}", stamp, StringComparison.OrdinalIgnoreCase)
            .Replace("{COMPANY}", job.Company ?? "", StringComparison.OrdinalIgnoreCase);

        var collapsed = Regex.Replace(replaced, "_{2,}", "_").Trim('_');
        return string.IsNullOrWhiteSpace(collapsed) ? $"WILKEN_{stamp}" : collapsed;
    }

    public static string DirectoryFor(ExportJob job, string root, string? template)
    {
        if (!string.IsNullOrWhiteSpace(template))
        {
            var rendered = Render(template, job);
            return Path.IsPathRooted(rendered) ? rendered : Path.Combine(root, rendered);
        }

        var yearFolder = job.FiscalYear > 0 ? job.FiscalYear.ToString() : "master";
        return Path.Combine(root, $"Mandant_{job.Client}", yearFolder);
    }

    private static string Sanitize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : Regex.Replace(value, @"[^\w\-]+", "");
}

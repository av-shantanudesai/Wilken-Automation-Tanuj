using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text;
using WilkenCs2ReplicaMock.Models;

namespace WilkenCs2ReplicaMock.Services;

public sealed class XlsxExportService
{
    public string Export(ReportDefinition report)
    {
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(downloads);
        var path = Path.Combine(downloads, "CTLP12.xlsx");
        if (File.Exists(path))
        {
            var suffix = 1;
            do
            {
                path = Path.Combine(downloads, $"CTLP12 ({suffix}).xlsx");
                suffix++;
            }
            while (File.Exists(path));
        }

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(archive, "[Content_Types].xml", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
 <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
 <Default Extension="xml" ContentType="application/xml"/>
 <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
 <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
</Types>
""");
        Write(archive, "_rels/.rels", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
 <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
</Relationships>
""");
        Write(archive, "xl/workbook.xml", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
 <sheets><sheet name="Export" sheetId="1" r:id="rId1"/></sheets>
</workbook>
""");
        Write(archive, "xl/_rels/workbook.xml.rels", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
 <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
</Relationships>
""");

        var rows = new List<string[]> { new[] { "Report", "Fachbereich", "Wertart", "Zeitraum", "Datensatz", "Wert" } };
        for (var i = 1; i <= report.RecordCount; i++)
            rows.Add(new[] { report.Description, report.Fachbereich, report.Wertart, $"{report.PeriodFrom}-{report.PeriodTo}", i.ToString(), (1000 + i * 17).ToString() });

        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        for (int r = 0; r < rows.Count; r++)
        {
            sb.Append($"<row r=\"{r + 1}\">");
            for (int c = 0; c < rows[r].Length; c++)
            {
                var col = (char)('A' + c);
                var v = SecurityElement.Escape(rows[r][c]) ?? "";
                sb.Append($"<c r=\"{col}{r + 1}\" t=\"inlineStr\"><is><t>{v}</t></is></c>");
            }
            sb.Append("</row>");
        }
        sb.Append("</sheetData></worksheet>");
        Write(archive, "xl/worksheets/sheet1.xml", sb.ToString());
        return path;
    }

    private static void Write(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}

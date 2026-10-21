using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Kor.Opportunities.Knowledge.Readers;
using UglyToad.PdfPig;

/// <summary>
/// A pursuit file's text, read from its bytes in memory (nothing is written to disk): PDF page by page, Word and Excel from
/// their XML parts, plain text as is. For the RFP review session, which reads the documents with <c>pursuit files text</c>.
/// </summary>
internal static class PursuitFileText
{
    internal static string Read(string fileName, byte[] bytes)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        try
        {
            return ext switch
            {
                ".pdf" => Pdf(bytes),
                ".docx" => Docx(bytes),
                ".xlsx" or ".xlsm" => Xlsx(bytes),
                ".txt" or ".csv" or ".md" or ".htm" or ".html" => Encoding.UTF8.GetString(bytes),
                _ => $"[no text reader for {ext} files; open it from the pursuit to read it]",
            };
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or System.Xml.XmlException or UglyToad.PdfPig.Core.PdfDocumentFormatException)
        {
            return $"[could not read this file: {ex.Message}]";
        }
    }

    private static string Pdf(byte[] bytes)
    {
        using var doc = PdfDocument.Open(bytes);
        var sb = new StringBuilder();
        foreach (var page in doc.GetPages())
        {
            var text = PdfTextReader.PageText(page);
            sb.AppendLine($"--- page {page.Number}");
            sb.AppendLine(string.IsNullOrWhiteSpace(text) ? "[no text on this page: scanned or a picture]" : text);
        }
        return sb.ToString();
    }

    private static string Docx(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var part = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException("no word/document.xml");
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        using var s = part.Open();
        var body = XDocument.Load(s);
        var sb = new StringBuilder();
        foreach (var p in body.Descendants(w + "p"))
            sb.AppendLine(string.Concat(p.Descendants().Where(e => e.Name == w + "t" || e.Name == w + "tab").Select(e => e.Name == w + "tab" ? "\t" : e.Value)));
        return sb.ToString();
    }

    private static string Xlsx(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        XNamespace m = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var shared = new List<string>();
        if (zip.GetEntry("xl/sharedStrings.xml") is { } ss)
        {
            using var s = ss.Open();
            shared.AddRange(XDocument.Load(s).Root!.Elements(m + "si").Select(si => string.Concat(si.Descendants(m + "t").Select(t => t.Value))));
        }

        // Sheet names and their parts, in workbook order.
        var targets = new Dictionary<string, string>();
        if (zip.GetEntry("xl/_rels/workbook.xml.rels") is { } rels)
        {
            using var s = rels.Open();
            foreach (var r in XDocument.Load(s).Root!.Elements())
                targets[(string)r.Attribute("Id")!] = "xl/" + ((string)r.Attribute("Target")!).TrimStart('/').Replace("xl/", "", StringComparison.Ordinal);
        }
        var sb = new StringBuilder();
        using (var wb = (zip.GetEntry("xl/workbook.xml") ?? throw new InvalidDataException("no xl/workbook.xml")).Open())
        {
            foreach (var sheet in XDocument.Load(wb).Descendants(m + "sheet"))
            {
                var id = (string?)sheet.Attribute(rel + "id");
                if (id is null || !targets.TryGetValue(id, out var path) || zip.GetEntry(path) is not { } entry) continue;
                sb.AppendLine($"--- sheet {(string?)sheet.Attribute("name")}");
                using var s = entry.Open();
                foreach (var row in XDocument.Load(s).Descendants(m + "row"))
                {
                    var cells = row.Elements(m + "c").Select(c =>
                    {
                        var type = (string?)c.Attribute("t");
                        var v = c.Element(m + "v")?.Value;
                        var text = type == "s" && int.TryParse(v, out var i) && i < shared.Count ? shared[i]
                            : type == "inlineStr" ? string.Concat(c.Descendants(m + "t").Select(t => t.Value)) : v;
                        return string.IsNullOrWhiteSpace(text) ? null : $"{(string?)c.Attribute("r")}: {text}";
                    }).Where(t => t is not null).ToList();
                    if (cells.Count > 0) sb.AppendLine(string.Join(" | ", cells));
                }
            }
        }
        return sb.ToString();
    }
}

// ShareTextExtract — runs ON the file server, reads a manifest of share-relative paths, writes ONE JSONL bundle.
//
//   ShareTextExtract <root> <manifest.txt> <out.jsonl> [--max-chars 400000] [--max-bytes 60000000]
//
// Why it exists: FS01 has no Python, pdftotext, Office or .NET runtime, and the rule is that nothing walks or
// bulk-reads the share over the VPN. Publish self-contained single-file (-r win-x64 --self-contained
// -p:PublishSingleFile=true), copy the exe to FS01, start it there with Win32_Process Create, read the bundle back.
//
// One line per manifest entry: {"path","ext","bytes","pages","chars","status","text"}; status is ok | empty |
// skipped-size | unsupported | error:<type>. It never walks a directory: the manifest is the whole input, so the
// file list comes from the inventory, not from enumeration.
//
// Readers: .pdf (PdfPig, text layer only; scans come back empty), .docx/.docm/.dotx/.xlsx/.xlsm/.pptx (zip XML),
// .doc (Word 97-2003 piece table via OpenMcdf), .msg (Outlook body + subject via OpenMcdf), .txt/.csv (UTF-8 or 1252),
// .rtf (control words stripped). Not covered: OCR, .xls binary, .wpd, embedded objects, text in images, headers and
// footers of .doc, tracked-change deletions (shown as present text).

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using OpenMcdf;
using UglyToad.PdfPig;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
if (args.Length < 3) { Console.Error.WriteLine("usage: ShareTextExtract <root> <manifest.txt> <out.jsonl> [--max-chars N] [--max-bytes N]"); return 2; }
var root = args[0];
var manifest = File.ReadAllLines(args[1], Encoding.UTF8).Where(l => l.Trim().Length > 0).ToArray();
var maxChars = Opt("--max-chars", 400_000);
var maxBytes = Opt("--max-bytes", 60_000_000);
var w1252 = Encoding.GetEncoding(1252);
var ok = 0; var failed = 0;
// --annotations: PDFs only, one line per file listing every markup - page, type, subject, interior and stroke colour
// (RGB 0-1), its words and its size in points - in place of the text. Added 2026-10-19 to measure what an engineer's
// markup colours mean across the markup corpus (a yellow band on 143 E 3rd's L1), on the server, never over the VPN.
var annotationsMode = args.Contains("--annotations");
if (annotationsMode)
{
    using var aout = new StreamWriter(args[2], false, new UTF8Encoding(false)) { AutoFlush = true };   // progress is visible from the share
    foreach (var rel in manifest)
    {
        var rel2 = rel.Trim().TrimStart('\\');
        string status = "ok"; object[] annots = Array.Empty<object>();
        try { annots = Annotations(Path.Combine(root, rel2)); }
        catch (Exception ex) { status = "error:" + ex.GetType().Name; failed++; }
        if (status == "ok") ok++;
        aout.WriteLine(JsonSerializer.Serialize(new { path = rel2, status, annots }));
    }
    aout.Flush();
    File.WriteAllText(args[2] + ".done", $"done {DateTime.Now:s} entries={manifest.Length} ok={ok} errors={failed}\r\n");
    Console.WriteLine($"{ok} of {manifest.Length} ok, {failed} errors");
    return 0;
}
using (var output = new StreamWriter(args[2], false, new UTF8Encoding(false)))
{
    foreach (var rel in manifest)
    {
        var rel2 = rel.Trim().TrimStart('\\');
        var full = Path.Combine(root, rel2);
        var ext = Path.GetExtension(full).ToLowerInvariant();
        long bytes = 0; int pages = 0; string status; string text = "";
        try
        {
            var fi = new FileInfo(full);
            bytes = fi.Length;
            if (bytes > maxBytes) status = "skipped-size";
            else
            {
                (text, pages) = ext switch
                {
                    ".pdf" => Pdf(full),
                    ".docx" or ".docm" or ".dotx" => (ZipXml(full, n => n == "word/document.xml" || n.StartsWith("word/footnotes") , "w:p"), 0),
                    ".xlsx" or ".xlsm" => (Xlsx(full), 0),
                    ".pptx" => (ZipXml(full, n => n.StartsWith("ppt/slides/slide") && n.EndsWith(".xml"), "a:p"), 0),
                    ".doc" or ".dot" => (Doc(full), 0),
                    ".msg" => (Msg(full), 0),
                    ".txt" or ".csv" => (ReadText(full), 0),
                    ".rtf" => (Rtf(ReadText(full)), 0),
                    _ => (null!, 0),
                };
                if (text is null) { status = "unsupported"; text = ""; }
                else
                {
                    text = Clean(text);
                    if (text.Length > maxChars) text = text[..maxChars];
                    status = text.Trim().Length == 0 ? "empty" : "ok";
                }
            }
            if (status == "ok") ok++;
        }
        catch (Exception ex) { status = "error:" + ex.GetType().Name; failed++; }
        output.WriteLine(JsonSerializer.Serialize(new { path = rel2, ext, bytes, pages, chars = text.Length, status, text }));
    }
}
File.WriteAllText(args[2] + ".done", $"done {DateTime.Now:s} entries={manifest.Length} ok={ok} errors={failed}\r\n");
Console.WriteLine($"{ok} of {manifest.Length} ok, {failed} errors");
return 0;

int Opt(string name, int dflt)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) ? v : dflt;
}

static object[] Annotations(string path)
{
    using var doc = PdfDocument.Open(path);
    var list = new List<object>();
    int n = 0;
    foreach (var page in doc.GetPages())
    {
        n++;
        foreach (var a in page.GetAnnotations())
        {
            var d = a.AnnotationDictionary.Data;
            double[]? Colour(string key) => d.TryGetValue(key, out var t) && t is UglyToad.PdfPig.Tokens.ArrayToken arr
                ? arr.Data.OfType<UglyToad.PdfPig.Tokens.NumericToken>().Select(x => Math.Round(x.Double, 3)).ToArray() : null;
            string? Text(string key) => d.TryGetValue(key, out var t) && t is UglyToad.PdfPig.Tokens.StringToken s ? s.Data
                : d.TryGetValue(key, out var h) && h is UglyToad.PdfPig.Tokens.HexToken x ? x.Data : null;
            list.Add(new
            {
                page = n,
                type = a.Type.ToString(),
                subject = Text("Subj"),
                fill = Colour("IC"),
                stroke = Colour("C"),
                text = a.Content is { Length: > 0 } c ? (c.Length > 300 ? c[..300] : c) : null,
                w = Math.Round(a.Rectangle.Width, 1),
                h = Math.Round(a.Rectangle.Height, 1),
            });
        }
    }
    return list.ToArray();
}

static (string, int) Pdf(string path)
{
    using var doc = PdfDocument.Open(path);
    var sb = new StringBuilder();
    var n = 0;
    foreach (var page in doc.GetPages())
    {
        n++;
        sb.Append("\n[page ").Append(n).Append("]\n");
        var words = page.GetWords().ToList();
        // Rebuild lines by baseline so tables and fee schedules keep their row order.
        foreach (var line in words.GroupBy(w => Math.Round(w.BoundingBox.Bottom / 3.0)).OrderByDescending(g => g.Key))
            sb.Append(string.Join(" ", line.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text))).Append('\n');
    }
    return (sb.ToString(), n);
}

static string ZipXml(string path, Func<string, bool> pick, string paraTag)
{
    using var zip = ZipFile.OpenRead(path);
    var sb = new StringBuilder();
    foreach (var e in zip.Entries.Where(e => pick(e.FullName)).OrderBy(e => e.FullName, StringComparer.Ordinal))
    {
        using var s = e.Open();
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var r = XmlReader.Create(s, settings);
        while (r.Read())
        {
            if (r.NodeType == XmlNodeType.Element)
            {
                if (r.Name is "w:t" or "a:t") sb.Append(r.ReadElementContentAsString());
                else if (r.Name is "w:tab") sb.Append('\t');
                else if (r.Name is "w:br" or "w:cr") sb.Append('\n');
            }
            if (r.NodeType == XmlNodeType.EndElement)
            {
                if (r.Name == paraTag) sb.Append('\n');
                else if (r.Name == "w:tc") sb.Append(" | ");
            }
        }
    }
    return sb.ToString();
}

static string Xlsx(string path)
{
    using var zip = ZipFile.OpenRead(path);
    var shared = new List<string>();
    var sst = zip.GetEntry("xl/sharedStrings.xml");
    if (sst != null)
    {
        using var r = XmlReader.Create(sst.Open(), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var cur = new StringBuilder(); var inSi = false;
        while (r.Read())
        {
            if (r.NodeType == XmlNodeType.Element && r.Name == "si") { inSi = true; cur.Clear(); }
            else if (r.NodeType == XmlNodeType.Element && r.Name == "t" && inSi) cur.Append(r.ReadElementContentAsString());
            if (r.NodeType == XmlNodeType.EndElement && r.Name == "si") { shared.Add(cur.ToString()); inSi = false; }
        }
    }
    var sb = new StringBuilder();
    foreach (var e in zip.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/sheet") && e.FullName.EndsWith(".xml")).OrderBy(e => e.FullName))
    {
        sb.Append("\n[sheet ").Append(e.Name).Append("]\n");
        using var r = XmlReader.Create(e.Open(), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        string? type = null; string? cref = null;
        while (r.Read())
        {
            if (r.NodeType == XmlNodeType.Element && r.Name == "c") { type = r.GetAttribute("t"); cref = r.GetAttribute("r"); }
            else if (r.NodeType == XmlNodeType.Element && (r.Name == "v" || r.Name == "t"))
            {
                var v = r.ReadElementContentAsString();
                if (type == "s" && int.TryParse(v, out var i) && i < shared.Count) v = shared[i];
                sb.Append(cref).Append('=').Append(v).Append('\t');
            }
            else if (r.NodeType == XmlNodeType.EndElement && r.Name == "row") sb.Append('\n');
        }
    }
    return sb.ToString();
}

static byte[] ReadStream(RootStorage storage, string name)
{
    using var s = storage.OpenStream(name);
    using var ms = new MemoryStream();
    s.CopyTo(ms);
    return ms.ToArray();
}

static string Doc(string path)
{
    using var storage = RootStorage.OpenRead(path);
    var wd = ReadStream(storage, "WordDocument");
    var flags = BitConverter.ToUInt16(wd, 0x0A);
    if ((flags & 0x0100) != 0) throw new InvalidDataException("encrypted");
    var table = ReadStream(storage, (flags & 0x0200) != 0 ? "1Table" : "0Table");
    var fcClx = BitConverter.ToInt32(wd, 0x01A2);
    var lcbClx = BitConverter.ToInt32(wd, 0x01A6);
    var w1252 = Encoding.GetEncoding(1252);
    var pos = fcClx; var end = fcClx + lcbClx;
    while (pos < end && table[pos] == 0x01) pos += 3 + BitConverter.ToUInt16(table, pos + 1);
    if (pos >= end || table[pos] != 0x02) throw new InvalidDataException("no piece table");
    var lcb = BitConverter.ToInt32(table, pos + 1);
    var plc = pos + 5;
    var n = (lcb - 4) / 12;
    var sb = new StringBuilder();
    for (var i = 0; i < n; i++)
    {
        var cpStart = BitConverter.ToInt32(table, plc + i * 4);
        var cpEnd = BitConverter.ToInt32(table, plc + (i + 1) * 4);
        var pcd = plc + (n + 1) * 4 + i * 8;
        var fc = BitConverter.ToUInt32(table, pcd + 2);
        var len = cpEnd - cpStart;
        if (len <= 0) continue;
        if ((fc & 0x40000000) != 0)
        {
            var off = (int)((fc & 0x3FFFFFFF) / 2);
            if (off + len <= wd.Length) sb.Append(w1252.GetString(wd, off, len));
        }
        else if (fc + len * 2 <= wd.Length) sb.Append(Encoding.Unicode.GetString(wd, (int)fc, len * 2));
    }
    return Regex.Replace(sb.ToString(), "[\u0013][^\u0014\u0015]*[\u0014]?|[\u0015]", "").Replace('\r', '\n').Replace('\u0007', '\t').Replace('\u000b', '\n').Replace('\u000c', '\n');
}

static string Msg(string path)
{
    using var storage = RootStorage.OpenRead(path);
    string Get(string id)
    {
        try { return Encoding.Unicode.GetString(ReadStream(storage, "__substg1.0_" + id + "001F")); } catch { }
        try { return Encoding.GetEncoding(1252).GetString(ReadStream(storage, "__substg1.0_" + id + "001E")); } catch { }
        return "";
    }
    return "Subject: " + Get("0037") + "\nFrom: " + Get("0C1A") + "\nTo: " + Get("0E04") + "\n\n" + Get("1000");
}

static string ReadText(string path)
{
    var b = File.ReadAllBytes(path);
    try { return new UTF8Encoding(false, true).GetString(b); } catch { return Encoding.GetEncoding(1252).GetString(b); }
}

static string Rtf(string s) =>
    Regex.Replace(Regex.Replace(Regex.Replace(s, @"\\par[d]?", "\n"), @"\{\\\*[^{}]*\}|\\[a-z]+-?\d* ?|[{}]", ""), @"\\'[0-9a-f]{2}", "");

static string Clean(string s)
{
    var sb = new StringBuilder(s.Length);
    foreach (var ch in s)
        sb.Append(ch == '\n' || ch == '\t' || !char.IsControl(ch) ? ch : ' ');
    return Regex.Replace(Regex.Replace(sb.ToString(), "[  ]{2,}", " "), "\n{3,}", "\n\n");
}

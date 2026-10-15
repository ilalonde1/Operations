using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Kor.Opportunities.Knowledge.Readers;

/// <summary>An EPUB is a zip of XHTML in spine order: read the OPF for the order, then each document through the HTML reader.</summary>
public sealed partial class EpubReader : IReader
{
    public async Task<IReadOnlyList<Chunk>> ReadAsync(string path, CancellationToken ct = default)
    {
        using var zip = ZipFile.OpenRead(path);
        var container = zip.GetEntry("META-INF/container.xml") ?? throw new InvalidDataException("not an EPUB: META-INF/container.xml missing");
        var opfPath = RootFile().Match(await ReadEntryAsync(container, ct)).Groups[1].Value;
        var opfEntry = zip.GetEntry(opfPath) ?? throw new InvalidDataException($"OPF not found at {opfPath}");
        var opf = await ReadEntryAsync(opfEntry, ct);
        var baseDir = opfPath.Contains('/') ? opfPath[..(opfPath.LastIndexOf('/') + 1)] : "";

        var hrefById = ManifestItem().Matches(opf).ToDictionary(m => m.Groups["id"].Value, m => WebUtility.UrlDecode(m.Groups["href"].Value));
        var spine = SpineRef().Matches(opf).Select(m => m.Groups["idref"].Value).ToList();

        if (spine.Count == 0) throw new InvalidDataException("The EPUB has no readable spine.");
        var chunks = new List<Chunk>();
        foreach (var id in spine)
        {
            ct.ThrowIfCancellationRequested();
            if (!hrefById.TryGetValue(id, out var href)) throw new InvalidDataException($"Spine item {id} is absent from the manifest.");
            var entry = zip.GetEntry(baseDir + href) ?? zip.GetEntry(href);
            if (entry is null) throw new InvalidDataException($"Spine document {href} is missing.");
            var html = await ReadEntryAsync(entry, ct);
            var title = HtmlText.Title(html);
            foreach (var s in HtmlText.Sections(html))
            {
                var reference = s.Heading ?? (title.Length > 0 ? title : Path.GetFileNameWithoutExtension(href));
                foreach (var piece in Chunking.Split(s.Text))
                    chunks.Add(new Chunk(reference, piece));
            }
        }
        return chunks;
    }

    private static async Task<string> ReadEntryAsync(ZipArchiveEntry entry, CancellationToken ct)
    {
        await using var s = entry.Open();
        using var r = new StreamReader(s, Encoding.UTF8);
        return await r.ReadToEndAsync(ct);
    }

    [GeneratedRegex(@"full-path=""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex RootFile();
    // an OPF may prefix its elements ("<opf:item", "<opf:itemref" - HarperCollins' New Market Wizards, 2026-09-19: "no text found" until this); the prefix is optional
    [GeneratedRegex(@"<(?:[A-Za-z]+:)?item\b[^>]*\bid=""(?<id>[^""]+)""[^>]*\bhref=""(?<href>[^""]+)""[^>]*>|<(?:[A-Za-z]+:)?item\b[^>]*\bhref=""(?<href>[^""]+)""[^>]*\bid=""(?<id>[^""]+)""[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ManifestItem();
    [GeneratedRegex(@"<(?:[A-Za-z]+:)?itemref\b[^>]*\bidref=""(?<idref>[^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex SpineRef();
}

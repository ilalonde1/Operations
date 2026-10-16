#nullable enable
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;

namespace Kor.Operations.Rendering.Proposal;

public sealed record ProposalDocxMetadata(string? Author, string? LastModifiedBy, string? Revision, DateTime? LastSaved);
public sealed record ProposalDocxText(string Text, ProposalDocxMetadata Metadata);

/// <summary>Reads paragraphs, cells and nested text boxes without counting markup fallbacks twice.</summary>
public static class ProposalDocxReader
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    public static ProposalDocxText Read(Stream stream)
    {
        using var document = WordprocessingDocument.Open(stream, false);
        var main = document.MainDocumentPart ?? throw new InvalidDataException("The Word document has no body.");
        var parts = new List<OpenXmlPart> { main };
        parts.AddRange(main.HeaderParts);
        parts.AddRange(main.FooterParts);
        if (main.FootnotesPart is { } footnotes) parts.Add(footnotes);
        if (main.EndnotesPart is { } endnotes) parts.Add(endnotes);
        var paragraphs = new List<string>();
        foreach (var part in parts)
        {
            using var xml = part.GetStream(FileMode.Open, FileAccess.Read);
            var tree = XDocument.Load(xml);
            // Pick one representation per AlternateContent, never deduplicate by text:
            // two distinct boxes with the same words must both survive.
            foreach (var alternate in tree.Descendants(Mc + "AlternateContent").Reverse().ToList())
            {
                var branch = alternate.Element(Mc + "Choice") ?? alternate.Element(Mc + "Fallback");
                if (branch is null) alternate.Remove();
                else alternate.ReplaceWith(branch.Nodes());
            }
            foreach (var paragraph in tree.Descendants(W + "p"))
            {
                var text = string.Concat(paragraph.Descendants()
                    .Where(e => e.Ancestors(W + "p").FirstOrDefault() == paragraph && !e.Ancestors(W + "del").Any())
                    .Select(e => e.Name == W + "t" ? e.Value : e.Name == W + "tab" ? "\t"
                        : e.Name == W + "br" || e.Name == W + "cr" ? "\n" : ""));
                if (!string.IsNullOrWhiteSpace(text)) paragraphs.Add(text);
            }
        }
        var properties = document.PackageProperties;
        return new(string.Join("\n", paragraphs), new(properties.Creator, properties.LastModifiedBy,
            properties.Revision, properties.Modified));
    }
}

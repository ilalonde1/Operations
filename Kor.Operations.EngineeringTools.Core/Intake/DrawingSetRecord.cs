using UglyToad.PdfPig;
using UglyToad.PdfPig.Outline;

namespace Kor.Operations.EngineeringTools.Intake;

public sealed record DrawingSetRecord(DocumentFacts Facts, IReadOnlyList<SheetRecord> Sheets);

public sealed record DocumentFacts(
        int Pages,
        string? Producer,
        string? Creator,
        IReadOnlyDictionary<int, string> Bookmarks,
        bool OutlinesPresent,
        bool Encrypted)
{
    /// <summary>What the document says about itself, and its sheet index if it carries one.</summary>
    public static DocumentFacts From(PdfDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var marks = new Dictionary<int, string>();
        if (doc.TryGetBookmarks(out var bookmarks))
        {
            void Walk(IEnumerable<BookmarkNode> nodes)
            {
                foreach (var n in nodes)
                {
                    if (n is DocumentBookmarkNode d && !marks.ContainsKey(d.PageNumber))
                        marks[d.PageNumber] = d.Title ?? "";
                    Walk(n.Children);
                }
            }
            Walk(bookmarks.Roots);
        }
        bool outlines = false;
        try { outlines = doc.Structure.Catalog.CatalogDictionary.Data.ContainsKey("Outlines"); } catch { }
        var info = doc.Information;
        return new DocumentFacts(doc.NumberOfPages, info?.Producer, info?.Creator, marks, outlines, doc.IsEncrypted);
    }
}

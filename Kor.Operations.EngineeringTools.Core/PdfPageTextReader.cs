#nullable enable
using System.Collections.Generic;
using System.Linq;
using UglyToad.PdfPig;

namespace Kor.Operations.EngineeringTools.RebarChange
{
    /// <summary>Reads a PDF into one text string per page (words joined by spaces). Fake-bold
    /// double-draws are removed first (<see cref="PdfWordDedupe"/>) so a merely-bolded call-out
    /// doesn't appear twice in the text and read as a count change downstream.</summary>
    public static class PdfPageTextReader
    {
        public static IReadOnlyList<string> ReadPages(string pdfPath)
        {
            var pages = new List<string>();
            using var doc = PdfDocument.Open(pdfPath);
            foreach (var page in doc.GetPages())
                pages.Add(string.Join(" ", PdfWordDedupe.Filter(page.GetWords()).Select(w => w.Text)));
            return pages;
        }
    }
}

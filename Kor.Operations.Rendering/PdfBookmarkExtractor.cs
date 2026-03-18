#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Kor.Operations.Rendering
{
    /// <summary>
    /// Small helper to read top-level PDF bookmarks (outlines).
    /// Completely self-contained and safe to call; it never throws.
    /// </summary>
    public static class PdfBookmarkExtractor
    {
        public static List<string> TryGetBookmarks(string? pdfPath)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(pdfPath))
            {
                return result;
            }

            if (!File.Exists(pdfPath))
            {
                return result;
            }

            try
            {
                using var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);

                // Walk the raw /Outlines dictionary instead of doc.Outlines
                var catalog = doc.Internals.Catalog;
                var outlinesDict = catalog.Elements.GetDictionary("/Outlines");
                if (outlinesDict == null)
                {
                    return result;
                }

                var first = outlinesDict.Elements.GetDictionary("/First");
                if (first == null)
                {
                    return result;
                }

                // Iterate top-level siblings via /Next
                var current = first;
                while (current != null)
                {
                    WalkOutlineDictionary(current, result, depth: 0);

                    // Move to the next top-level outline item
                    current = current.Elements.GetDictionary("/Next");
                }
            }
            catch
            {
                // Swallow everything – bookmark extraction must never
                // break a transmittal or cover sheet render.
            }

            return result;
        }

        private static void WalkOutlineDictionary(PdfDictionary item, List<string> target, int depth)
        {
            // Limit depth so we don’t spam the cover sheet.
            if (item == null || depth > 8)
            {
                return;
            }

            try
            {
                var title = item.Elements.GetString("/Title");
                if (!string.IsNullOrWhiteSpace(title))
                {
                    var line = new string(' ', depth * 2) + title.Trim();
                    target.Add(line);
                }
            }
            catch
            {
                // Ignore bad titles but still try to walk children.
            }

            try
            {
                // Children of this item live under /First with /Next siblings.
                var child = item.Elements.GetDictionary("/First");
                while (child != null)
                {
                    WalkOutlineDictionary(child, target, depth + 1);

                    child = child.Elements.GetDictionary("/Next");
                }
            }
            catch
            {
                // Ignore broken child chains on this node only.
            }
        }
    }
}

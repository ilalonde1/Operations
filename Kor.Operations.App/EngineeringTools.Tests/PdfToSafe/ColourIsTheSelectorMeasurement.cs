using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

/// <summary>
/// CAN THE THING AN ENGINEER ASKS FOR BE PICKED OUT BY COLOUR?
///
/// The ask, in his words: "I would like to get a dxf for this pdf. The tower outline, the red
/// markups I did and the balcony outline." — Omar Alcazar, 28 August, Parcel 11.
///
/// Two of those three are the ARCHITECT'S page content and one is his own annotation, so the
/// annotations-only filter cannot serve it. But nothing about the request needs a structural
/// classifier either: he is not asking what anything IS, he is asking for three sets of lines out
/// of a drawing that already contains them. On a vector PDF the draughtsman's own separation is
/// still there — colour — and the parser has always kept it per shape, while DxfExporter already
/// takes an excludedColors set.
///
/// So this counts what colours the page actually holds and how much sits on each. If the red is a
/// handful of distinct values carrying tens of shapes, and the architect's outlines sit on their
/// own colours, the request is a selection and not a classification problem. If everything is one
/// black, it is not, and the honest answer is different.
///
/// Reporting only — the numbers are the deliverable, as with
/// <see cref="PageContentVsAnnotationsMeasurement"/> beside it.
/// </summary>
public sealed class ColourIsTheSelectorMeasurement
{
    private readonly ITestOutputHelper _out;
    public ColourIsTheSelectorMeasurement(ITestOutputHelper output) => _out = output;

    private static readonly string Desktop =
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    public static IEnumerable<object[]> Sets()
    {
        yield return new object[] { "Parcel 11, architect's sheet + Omar's red", "OAP-parcel11-arch-markup.pdf" };
        yield return new object[] { "Andrea's parking markup", "AN-parking-markup.pdf" };
    }

    [Theory]
    [MemberData(nameof(Sets))]
    public void WhatColoursThePageSeparatesInto(string label, string file)
    {
        string path = Path.Combine(Desktop, file);
        if (!File.Exists(path)) { _out.WriteLine($"SKIPPED {label}: not at {path}"); return; }

        _out.WriteLine($"===== {label}");
        _out.WriteLine($"      {file}");

        // The markup can be on any sheet of a set; scan until the pages run out.
        for (int page = 1; page <= 16; page++)
        {
            ExtractedGeometry whole;
            try
            {
                using var s = File.OpenRead(path);
                whole = PageContentVsAnnotationsMeasurement.ExtractWholePageForMeasurement(s, page, 100);
            }
            catch (ArgumentOutOfRangeException) { break; }
            catch (Exception ex) { _out.WriteLine($"  page {page}: {ex.GetType().Name} — {ex.Message}"); break; }

            var tally = new Dictionary<(byte R, byte G, byte B), (int Slabs, int Cols, int Lines, double Biggest)>();

            void Add((byte R, byte G, byte B) c, int slab, int col, int line, double size)
            {
                tally.TryGetValue(c, out var had);
                tally[c] = (had.Slabs + slab, had.Cols + col, had.Lines + line, Math.Max(had.Biggest, size));
            }

            for (int i = 0; i < whole.Slabs.Count && i < whole.SlabColors.Count; i++)
                Add(whole.SlabColors[i], 1, 0, 0, Diagonal(whole.Slabs[i]));
            // A column is a centroid, not a ring; its size lives alongside in ColumnSizes.
            for (int i = 0; i < whole.Columns.Count && i < whole.ColumnColors.Count; i++)
            {
                double across = i < whole.ColumnSizes.Count
                    ? Math.Max(whole.ColumnSizes[i].WidthMm, whole.ColumnSizes[i].DepthMm)
                    : 0;
                Add(whole.ColumnColors[i], 0, 1, 0, across);
            }
            for (int i = 0; i < whole.Lines.Count && i < whole.LineColors.Count; i++)
                Add(whole.LineColors[i], 0, 0, 1, Diagonal(whole.Lines[i]));

            if (tally.Count == 0) continue;

            _out.WriteLine($"  --- page {page}: {tally.Count} distinct colour(s) over " +
                           $"{whole.Slabs.Count} slab(s), {whole.Columns.Count} column(s), {whole.Lines.Count} line(s)");
            _out.WriteLine($"      {"colour",-16} {"slabs",6} {"cols",6} {"lines",7} {"biggest mm",12}  note");

            foreach (var kv in tally.OrderByDescending(k => k.Value.Slabs + k.Value.Cols + k.Value.Lines).Take(16))
            {
                var (r, g, b) = kv.Key;
                var v = kv.Value;
                string note = r > 120 && g < 90 && b < 90 ? "RED — an engineer's markup"
                            : r < 60 && g < 60 && b < 60 ? "black — the drawing itself"
                            : "";
                _out.WriteLine($"      #{r:X2}{g:X2}{b:X2} ({r,3},{g,3},{b,3}) {v.Slabs,6} {v.Cols,6} {v.Lines,7} {v.Biggest,12:N0}  {note}");
            }
        }

        Assert.True(true);   // reporting only
    }

    private static double Diagonal(IReadOnlyList<(double X, double Y)> pts)
    {
        if (pts.Count == 0) return 0;
        double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
        double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
        return Math.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY));
    }
}

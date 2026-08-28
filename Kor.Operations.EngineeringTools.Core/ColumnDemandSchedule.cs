#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;

namespace Kor.Operations.EngineeringTools.ColumnDesign
{
    /// <summary>One column on one storey, and the worst demand on it across every case.</summary>
    public sealed record ColumnRow(
        string Storey, string Mark, string Section, string Strength, double? EffectiveLength,
        int Cases, double MaxCompression, double MaxTension, double MaxMfy, double MaxMfz,
        string Source);

    /// <summary>The same column and case appearing in two files with different forces.</summary>
    public sealed record DemandConflict(
        string Storey, string Mark, string Case,
        string FileA, double NfA, string FileB, double NfB)
    {
        public double Difference => Math.Abs(NfA - NfB);

        /// <summary>How far apart, as a share of the larger. A count of disagreements is alarming
        /// and useless; the size of them is what says whether it is rounding or a stale file.</summary>
        public double Percent => Difference / Math.Max(Math.Max(Math.Abs(NfA), Math.Abs(NfB)), 1e-9) * 100.0;
    }

    /// <summary>Two files that disagree about columns they both contain, and how often.</summary>
    public sealed record ConflictingPair(string FileA, string FileB, int Demands, double WorstPercent);

    /// <summary>Everything a folder of S-Concrete files says, and everywhere it contradicts itself.</summary>
    public sealed record ColumnDemandReport(
        IReadOnlyList<ColumnRow> Columns,
        IReadOnlyList<DemandConflict> Conflicts,
        IReadOnlyList<ConflictingPair> ConflictingPairs,
        IReadOnlyList<ColumnDemand> Truncated,
        int FilesRead,
        int DemandsRead)
    {
        /// <summary>Disagreements too small to be anything but rounding.</summary>
        public int TrivialConflicts => Conflicts.Count(c => c.Percent < 1.0);

        /// <summary>Disagreements a person has to resolve.</summary>
        public int MaterialConflicts => Conflicts.Count - TrivialConflicts;
    }

    /// <summary>
    /// What a project's column design actually says, gathered from the S-Concrete files themselves.
    ///
    /// Today that question — "what demand did we design this column for?" — is answered by opening
    /// files one at a time. On 30961-01 there are 66 of them across three engineers' folders, named
    /// by typing the column marks into the filename, and no index anywhere.
    ///
    /// It also answers a question nobody can ask today: where the files DISAGREE. The same column on
    /// the same storey for the same load case, typed into two files from two different analysis
    /// runs, is invisible until someone opens both. It is reported here as a conflict, with the
    /// difference, so an engineer can say which is current. The tool does not guess.
    /// </summary>
    public static class ColumnDemandSchedule
    {
        public static ColumnDemandReport Read(IEnumerable<string> scoFiles)
        {
            ArgumentNullException.ThrowIfNull(scoFiles);

            var byFile = new List<(string File, IReadOnlyList<ColumnDemand> Demands)>();
            foreach (string path in scoFiles)
            {
                var demands = SConcreteFile.ReadDemands(path);
                if (demands.Count > 0) byFile.Add((Path.GetFileName(path), demands));
            }

            var all = byFile.SelectMany(f => f.Demands.Select(d => (f.File, Demand: d))).ToList();

            // Where two files state different forces for one column, storey and case. Report the
            // FURTHEST APART pair, not the first two found — the worst case is what needs deciding.
            var conflicts = new List<DemandConflict>();
            foreach (var g in all.GroupBy(x => x.Demand.Key))
            {
                var byValue = g.GroupBy(x => Math.Round(x.Demand.Nf, 3))
                    .Select(v => v.First())
                    .OrderBy(x => x.Demand.Nf)
                    .ToList();
                if (byValue.Count < 2) continue;

                var a = byValue[0];
                var b = byValue[^1];
                var parts = g.Key.Split('|');
                conflicts.Add(new DemandConflict(parts[0], parts[1], parts[2],
                    a.File, a.Demand.Nf, b.File, b.Demand.Nf));
            }

            // WHICH FILES disagree, not just how many demands do. On 30961-01 the worst pair is
            // "14X36 (L02-L8)(C18 to C21).SCO" against "14X36 (L2-L8)(C18 to C21).SCO" — two names
            // one character apart, the same four columns, ninety demands, up to 84% apart. A count
            // of 549 is alarming and unusable; that sentence is something a person can go and settle.
            var pairs = conflicts
                .GroupBy(c => c.FileA.CompareTo(c.FileB) <= 0 ? (c.FileA, c.FileB) : (c.FileB, c.FileA))
                .Select(g => new ConflictingPair(g.Key.Item1, g.Key.Item2, g.Count(), g.Max(c => c.Percent)))
                .OrderByDescending(p => p.Demands)
                .ToList();

            var columns = all
                .GroupBy(x => (x.Demand.Storey, x.Demand.Mark))
                .Select(g =>
                {
                    var first = g.First().Demand;
                    return new ColumnRow(
                        g.Key.Storey, g.Key.Mark,
                        g.Select(x => x.Demand.Section).FirstOrDefault(s => s.Length > 0) ?? "",
                        g.Select(x => x.Demand.Strength).FirstOrDefault(s => s.Length > 0) ?? "",
                        g.Select(x => x.Demand.EffectiveLength).FirstOrDefault(k => k is not null),
                        g.Select(x => x.Demand.Case).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                        // S-Concrete writes compression negative.
                        -g.Min(x => x.Demand.Nf) is var c && c > 0 ? c : 0,
                        g.Max(x => x.Demand.Nf) is var t && t > 0 ? t : 0,
                        g.Max(x => Math.Abs(x.Demand.Mfy)),
                        g.Max(x => Math.Abs(x.Demand.Mfz)),
                        string.Join(", ", g.Select(x => x.File).Distinct().OrderBy(f => f)));
                })
                .OrderBy(r => r.Storey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Mark, new MarkComparer())
                .ToList();

            return new ColumnDemandReport(
                columns,
                conflicts.OrderByDescending(c => c.Percent).ToList(),
                pairs,
                all.Select(x => x.Demand).Where(d => d.IdentityTruncated).ToList(),
                byFile.Count,
                all.Count);
        }

        /// <summary>C9 before C10 — a column mark is a letter and a number, not a string.</summary>
        private sealed class MarkComparer : IComparer<string>
        {
            public int Compare(string? x, string? y)
            {
                static (string Prefix, int Number) Split(string s)
                {
                    int i = 0;
                    while (i < s.Length && !char.IsDigit(s[i])) i++;
                    return (s[..i], int.TryParse(s[i..], out int n) ? n : 0);
                }

                var a = Split(x ?? "");
                var b = Split(y ?? "");
                int p = string.CompareOrdinal(a.Prefix, b.Prefix);
                return p != 0 ? p : a.Number.CompareTo(b.Number);
            }
        }

        // ---- the workbook ----------------------------------------------------------------------

        private static readonly XLColor Navy = XLColor.FromHtml("#1F3864");
        private static readonly XLColor Warn = XLColor.FromHtml("#F4B183");
        private static readonly XLColor Grey = XLColor.FromHtml("#808080");

        public static byte[] BuildXlsx(ColumnDemandReport report, string project)
        {
            ArgumentNullException.ThrowIfNull(report);

            using var wb = new XLWorkbook();
            Columns(wb.Worksheets.Add("Column Demands"), report, project);
            if (report.Conflicts.Count > 0) Conflicts(wb.Worksheets.Add("Disagreements"), report);

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        private static void Columns(IXLWorksheet ws, ColumnDemandReport r, string project)
        {
            ws.Cell(1, 1).Value = $"{project} — column demands, read from the S-Concrete files";
            ws.Cell(1, 1).Style.Font.SetBold().Font.FontSize = 14;
            ws.Cell(2, 1).Value = $"{r.FilesRead} file(s), {r.DemandsRead} demands, {r.Columns.Count} columns. "
                + "Compression positive here; S-Concrete writes it negative.";
            ws.Cell(2, 1).Style.Font.SetItalic().Font.FontColor = Grey;

            string[] head = { "Storey", "Mark", "Section", "Strength", "kl", "Cases",
                              "Max compression", "Max tension", "Max Mfy", "Max Mfz", "From file(s)" };
            for (int c = 0; c < head.Length; c++)
            {
                var cell = ws.Cell(4, c + 1);
                cell.Value = head[c];
                cell.Style.Font.SetBold().Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = Navy;
            }

            int row = 5;
            foreach (var x in r.Columns)
            {
                ws.Cell(row, 1).Value = x.Storey;
                ws.Cell(row, 2).Value = x.Mark;
                ws.Cell(row, 3).Value = x.Section;
                ws.Cell(row, 4).Value = x.Strength;
                if (x.EffectiveLength is double kl) ws.Cell(row, 5).Value = kl;
                else
                {
                    // Not a blank: the file genuinely does not record it, and that is worth seeing.
                    ws.Cell(row, 5).Value = "not recorded";
                    ws.Cell(row, 5).Style.Fill.BackgroundColor = Warn;
                }
                ws.Cell(row, 6).Value = x.Cases;
                ws.Cell(row, 7).Value = x.MaxCompression;
                ws.Cell(row, 8).Value = x.MaxTension;
                ws.Cell(row, 9).Value = x.MaxMfy;
                ws.Cell(row, 10).Value = x.MaxMfz;
                ws.Cell(row, 11).Value = x.Source;
                ws.Range(row, 7, row, 10).Style.NumberFormat.Format = "#,##0.0";
                row++;
            }

            ws.SheetView.FreezeRows(4);
            ws.Columns(1, 10).AdjustToContents();
            ws.Column(11).Width = 46;
        }

        private static void Conflicts(IXLWorksheet ws, ColumnDemandReport r)
        {
            ws.Cell(1, 1).Value = "The same column and load case, stated differently in two files";
            ws.Cell(1, 1).Style.Font.SetBold().Font.FontSize = 14;
            ws.Cell(2, 1).Value =
                "Each row is one column that was typed into two S-Concrete files with different axial "
                + "demands — usually two analysis runs, where one file was not redone. Which is current "
                + "is an engineering question; this only says they disagree.";
            ws.Cell(2, 1).Style.Font.SetItalic().Font.FontColor = Grey;

            // The pairs first: which two files disagree, and how badly. That is the actionable list.
            int p = 4;
            ws.Cell(p, 1).Value = "Files that disagree with each other";
            ws.Cell(p, 1).Style.Font.SetBold().Font.FontColor = XLColor.White;
            ws.Cell(p, 1).Style.Fill.BackgroundColor = Navy;
            ws.Range(p, 1, p, 4).Merge();

            p++;
            foreach (var h in new[] { "File A", "File B", "Demands", "Worst" })
            {
                ws.Cell(p, Array.IndexOf(new[] { "File A", "File B", "Demands", "Worst" }, h) + 1).Value = h;
                ws.Cell(p, Array.IndexOf(new[] { "File A", "File B", "Demands", "Worst" }, h) + 1).Style.Font.SetBold();
            }
            p++;
            foreach (var pair in r.ConflictingPairs.Take(25))
            {
                ws.Cell(p, 1).Value = pair.FileA;
                ws.Cell(p, 2).Value = pair.FileB;
                ws.Cell(p, 3).Value = pair.Demands;
                ws.Cell(p, 4).Value = pair.WorstPercent / 100.0;
                ws.Cell(p, 4).Style.NumberFormat.Format = "0.0%";
                ws.Cell(p, 4).Style.Fill.BackgroundColor = Warn;
                p++;
            }

            int top = p + 2;
            ws.Cell(top - 1, 1).Value = "Every disagreeing demand, worst first";
            ws.Cell(top - 1, 1).Style.Font.SetBold();

            string[] head = { "Storey", "Mark", "Case", "File A", "Nf (A)", "File B", "Nf (B)", "Difference", "Apart" };
            for (int c = 0; c < head.Length; c++)
            {
                var cell = ws.Cell(top, c + 1);
                cell.Value = head[c];
                cell.Style.Font.SetBold().Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = Navy;
            }

            int row = top + 1;
            foreach (var c in r.Conflicts)
            {
                ws.Cell(row, 1).Value = c.Storey;
                ws.Cell(row, 2).Value = c.Mark;
                ws.Cell(row, 3).Value = c.Case;
                ws.Cell(row, 4).Value = c.FileA;
                ws.Cell(row, 5).Value = c.NfA;
                ws.Cell(row, 6).Value = c.FileB;
                ws.Cell(row, 7).Value = c.NfB;
                ws.Cell(row, 8).Value = c.Difference;
                ws.Cell(row, 9).Value = c.Percent / 100.0;
                ws.Range(row, 5, row, 8).Style.NumberFormat.Format = "#,##0.0";
                ws.Cell(row, 9).Style.NumberFormat.Format = "0.0%";
                if (c.Percent >= 1.0) ws.Cell(row, 9).Style.Fill.BackgroundColor = Warn;
                row++;
            }

            ws.Columns(1, 9).AdjustToContents();
        }
    }
}

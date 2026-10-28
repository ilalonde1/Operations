using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// WHICH SHEETS DRAW THE WALLS A MODEL HOLDS TWICE (2026-10-28, board 76 consumer 2; VD's yes, read only). The gate models carry
/// 1,028 overlapping collinear wall pairs (tools/CorpusGateBisect/wall_overlap_dangle.py on the d4352fc0-equivalent run), and
/// on 31053 L2 one such pair is the north wall drawn by both the Level 1 and the Level 2 sheet, which the composer's exact-
/// endpoint duplicate check keeps twice once their ends differ. This rebuilds each set of a corpus-gate work dir the way
/// PdfOnlyBuild composes it - the dxf/ views, levels.csv in mm, PairOpenFaces off, the rules DB - with the stack merge OFF, so
/// every wall keeps the composer's name and ComposeSummary.SourceSheetOfObject names its sheet; then lists every overlapping pair
/// with both sheets. Silent unless KOR_OVERLAP_PROBE names the work dir (KOR_OVERLAP_PROBE_JOBS narrows it to a,b).
/// Writes TestResults/overlap-provenance/pairs.tsv and summary.txt.
/// COVERS: KW panels (two plan points), per storey, the same overlap test as wall_overlap_dangle.py (parallel within 0.5 deg,
/// centrelines within 25 mm, sharing more than 100 mm). DOES NOT: headers (KS), columns, plates; prove the probe's rebuild is the
/// gate's model - the total is printed beside wall_overlap_dangle.py's so the two can be compared.
/// </summary>
[Collection(SheetNamingVocabularyCollection.Name)]   // DxfToEtabsService sets PlanSheetNaming.Vocabulary, a shared static
public sealed class WallOverlapProvenanceProbe
{
    [Fact]
    public void ListEveryOverlappingWallPairWithItsSheets()
    {
        string? work = Environment.GetEnvironmentVariable("KOR_OVERLAP_PROBE");
        if (string.IsNullOrWhiteSpace(work)) return;
        var only = (Environment.GetEnvironmentVariable("KOR_OVERLAP_PROBE_JOBS") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string outDir = Path.Combine(AppContext.BaseDirectory, "TestResults", "overlap-provenance");
        Directory.CreateDirectory(outDir);
        var rows = new List<string> { "job\tstorey\twallA\tsheetA\twallB\tsheetB\tshared_mm\tsame_sheet" };
        var summary = new List<string>();
        int pairs = 0, same = 0, walls = 0, failed = 0;

        foreach (string jobDir in Directory.GetDirectories(work).OrderBy(d => d, StringComparer.Ordinal))
        {
            string job = Path.GetFileName(jobDir);
            if (only.Count > 0 && !only.Contains(job)) continue;
            string dxf = Path.Combine(jobDir, "dxf"), levels = Path.Combine(jobDir, "levels.csv");
            if (!Directory.Exists(dxf) || !File.Exists(levels) || !File.Exists(Path.Combine(jobDir, "out.e2k"))) continue;
            string e2k = Path.Combine(outDir, job + ".e2k");
            DxfToEtabsReport report;
            try
            {
                report = DxfToEtabsService.Run(new DxfToEtabsRequest
                {
                    DxfFolder = dxf,
                    ReferenceE2k = string.Empty,
                    OutputE2k = e2k,
                    LevelsFile = levels,
                    LevelsUnit = "mm",
                    Classification = new PlanClassificationOptions { PairOpenFaces = false },
                    Compose = new ComposeOptions { IncludeFloors = true, MembersRiseToStoreyAbove = true },
                    MergeStacksIntoOneLabel = false,
                    RequireRuleSettings = true,
                });
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or FormatException)
            {
                summary.Add($"{job}: not built - {ex.Message}");
                failed++;
                continue;
            }

            var sheetOf = report.Summary.SourceSheetOfObject;
            var byStorey = Walls(File.ReadAllText(e2k));
            int jobPairs = 0, jobSame = 0;
            foreach (var (storey, list) in byStorey)
            {
                walls += list.Count;
                for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                {
                    double shared = Shared(list[i], list[j]);
                    if (shared <= 100) continue;
                    string sa = sheetOf.TryGetValue(list[i].Name, out var a) ? a : "?";
                    string sb = sheetOf.TryGetValue(list[j].Name, out var b) ? b : "?";
                    bool sameSheet = sa == sb;
                    jobPairs++; if (sameSheet) jobSame++;
                    rows.Add($"{job}\t{storey}\t{list[i].Name}\t{sa}\t{list[j].Name}\t{sb}\t{shared:F0}\t{(sameSheet ? 1 : 0)}");
                }
            }
            pairs += jobPairs; same += jobSame;
            summary.Add($"{job}: {jobPairs} overlapping pair(s), {jobSame} with both walls from one sheet, {jobPairs - jobSame} from two");
        }

        summary.Insert(0, $"walls {walls}; overlapping pairs {pairs}; both from one sheet {same}; from two sheets {pairs - same}; sets not built {failed}");
        File.WriteAllLines(Path.Combine(outDir, "pairs.tsv"), rows);
        File.WriteAllLines(Path.Combine(outDir, "summary.txt"), summary);
    }

    private sealed record Wall(string Name, double X1, double Y1, double X2, double Y2);

    private static Dictionary<string, List<Wall>> Walls(string text)
    {
        var unit = Regex.Match(text, "^\\s*UNITS\\s+\"[^\"]*\"\\s+\"([^\"]+)\"", RegexOptions.Multiline);
        double u = unit.Success ? unit.Groups[1].Value.ToUpperInvariant() switch { "M" => 1000, "IN" => 25.4, "FT" => 304.8, _ => 1 } : 1;
        var points = Regex.Matches(text, "^\\s+POINT\\s+\"(\\S+)\"\\s+(\\S+)\\s+(\\S+)", RegexOptions.Multiline)
            .ToDictionary(m => m.Groups[1].Value, m => (X: double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) * u,
                                                       Y: double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture) * u));
        var seg = new Dictionary<string, Wall>();
        foreach (Match m in Regex.Matches(text, "^\\s+AREA\\s+\"(KW\\S*)\"\\s+PANEL\\s+4\\s+((?:\"\\S+\"\\s+){4})", RegexOptions.Multiline))
        {
            var xy = Regex.Matches(m.Groups[2].Value, "\"(\\S+)\"").Select(p => p.Groups[1].Value).Where(points.ContainsKey)
                .Select(p => points[p]).Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
            if (xy.Count == 2) seg[m.Groups[1].Value] = new Wall(m.Groups[1].Value, xy[0].X, xy[0].Y, xy[1].X, xy[1].Y);
        }
        var by = new Dictionary<string, List<Wall>>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(text, "^\\s+AREAASSIGN\\s+\"(\\S+)\"\\s+\"([^\"]+)\"", RegexOptions.Multiline))
            if (seg.TryGetValue(m.Groups[1].Value, out var w))
            {
                string storey = string.Join(" ", m.Groups[2].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                if (!by.TryGetValue(storey, out var list)) by[storey] = list = new List<Wall>();
                list.Add(w);
            }
        return by;
    }

    /// <summary>How much of two walls' length is shared when they lie on one line (0.5 deg, 25 mm); 0 otherwise.</summary>
    private static double Shared(Wall a, Wall b)
    {
        double ux = a.X2 - a.X1, uy = a.Y2 - a.Y1, la = Math.Sqrt(ux * ux + uy * uy);
        double vx = b.X2 - b.X1, vy = b.Y2 - b.Y1, lb = Math.Sqrt(vx * vx + vy * vy);
        if (la < 1e-9 || lb < 1e-9) return 0;
        ux /= la; uy /= la; vx /= lb; vy /= lb;
        if (Math.Asin(Math.Min(1, Math.Abs(ux * vy - uy * vx))) * 180 / Math.PI > 0.5) return 0;
        double Off(double x, double y) => Math.Abs((x - a.X1) * -uy + (y - a.Y1) * ux);
        if (Off(b.X1, b.Y1) > 25 || Off(b.X2, b.Y2) > 25) return 0;
        double s0 = (b.X1 - a.X1) * ux + (b.Y1 - a.Y1) * uy, s1 = (b.X2 - a.X1) * ux + (b.Y2 - a.Y1) * uy;
        return Math.Min(la, Math.Max(s0, s1)) - Math.Max(0, Math.Min(s0, s1));
    }
}

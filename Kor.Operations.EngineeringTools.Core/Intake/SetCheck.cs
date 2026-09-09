using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Kor.Operations.EngineeringTools.QuantityTakeoff;

namespace Kor.Operations.EngineeringTools.Intake;

/// <summary>
/// THE SET CHECKS ITSELF (foundation §3.3): what the gatekeeper looks for before an issue goes
/// out, from what the record already holds — a sheet with no number or no scale, a scale stated
/// twice, a schedule mark never placed, a column of a size no schedule declares, a footing box no
/// label names and a label no footing answers, a grid name drawn twice on one sheet, a grid axis a
/// sheet draws somewhere else than the rest of the set, and the storeys against the model when
/// there is one. Each finding says the sheet, the class, and the detail a person checks.
/// </summary>
public static class SetCheck
{
    public sealed record Finding(int Page, string? Sheet, string Kind, string Detail)
    {
        public string Line() => $"p{Page,-3} {Sheet ?? "-",-10} {Kind,-34} {Detail}";
    }

    public sealed record Report(IReadOnlyList<Finding> Findings, int Pages, int Plans)
    {
        public IEnumerable<string> Lines() => Findings.Select(f => f.Line());
        public IEnumerable<(string Kind, int Count)> ByKind() =>
            Findings.GroupBy(f => f.Kind).Select(g => (g.Key, g.Count())).OrderByDescending(t => t.Item2);
    }

    /// <summary>A grid axis drawn farther than this from where the set's reference sheet draws it, after the frame by name, is a finding.</summary>
    public const double GridDisagreeMm = 50;

    /// <summary>The findings on one sheet, from its record alone.</summary>
    public static List<Finding> Sheet(SheetRecord r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var f = new List<Finding>();
        void Add(string kind, string detail) => f.Add(new Finding(r.PageNumber, r.SheetNumber, kind, detail));

        bool structural = r.SheetType is "plan" or "section/elevation" or "details" or "schedule";
        if (r.SheetNumber is null && r.SheetType != "cover/index") Add("sheet number: none read", "the title block gives no SHEET NUMBER this reader finds");
        if (r.SheetType == "unknown") Add("sheet type: unknown", "neither the title nor the bookmark says what the sheet is");
        if (r.ScaleConflict) Add("scale: stated twice, differently", "the title block's SCALE field and a scale note on the sheet disagree");
        if (r.SheetType is "plan" && r.ScaleNote is null && r.ScaleDenominator is null) Add("scale: none stated", "a plan with no ratio and no AS NOTED");
        if (structural && !r.TitleBlock.ContainsKey("SHEET TITLE") && !r.TitleBlock.ContainsKey("DRAWING TITLE")) Add("title block: no SHEET TITLE", "the field is missing or its label unread");

        if (r.ColumnAgreement is { } ca)
        {
            // marks never placed and sizes never declared are the SET's business (below): a mark
            // declared on the foundation plan is placed on the level above; a tower column's size is
            // declared on the column schedule sheet, not on the plan that draws it
            int mismatched = ca.Columns.Count(c => c.NearestMark is not null && c.SizeIsDeclaredSomewhere && !c.SizeMatchesItsOwnMark);
            if (mismatched > 0)
                Add("column size differs from its own mark's", $"{mismatched}: " + string.Join(", ", ca.Columns.Where(c => c.NearestMark is not null && c.SizeIsDeclaredSomewhere && !c.SizeMatchesItsOwnMark).Take(6).Select(c => $"{c.NearestMark} drawn {c.WidthMm / 25.4:0}x{c.DepthMm / 25.4:0} in")));
        }
        else if (r.ColumnAgreementError is not null && r.SheetType == "plan")
            Add("plan against schedule: not checked", r.ColumnAgreementError);

        var unlabelled = r.Geometry.Footings.Where(x => !x.LabelledOnThePlan).ToList();
        if (unlabelled.Count > 0)
            Add("footing box no label names", $"{unlabelled.Count}: " + string.Join(", ", unlabelled.Take(6).Select(x => $"{x.LengthMm:0}x{x.WidthMm:0} at ({x.Centre.X / 1000:0.0}, {x.Centre.Y / 1000:0.0}) m")));
        var unanswered = r.FootingLabels.Where(l => !r.Geometry.Footings.Any(x => Contains(x, l.X, l.Y))).ToList();
        if (unanswered.Count > 0 && r.Geometry.Footings.Count + r.FootingLabels.Count > 0)
            Add("footing label no footing answers", $"{unanswered.Count} of {r.FootingLabels.Count}: " + string.Join(", ", unanswered.Take(8).Select(l => $"{l.Mark} at ({l.X / 1000:0.0}, {l.Y / 1000:0.0}) m")));

        // on a plan; an elevation sheet carries the same bubbles on every elevation it draws
        var twice = r.SheetType != "plan" ? new List<string>() : r.Geometry.GridAxes.GroupBy(a => (a.Name.ToUpperInvariant(), a.Vertical))
            .Where(g => g.Select(a => Math.Round(a.AtMm / 100)).Distinct().Count() > 1).Select(g => g.Key.Item1).ToList();
        if (twice.Count > 0)
            Add("grid name drawn twice on the sheet", $"{string.Join(", ", twice)} — two views on one sheet, or a duplicate bubble");

        return f;
    }

    /// <summary>A mark declared on any sheet's schedule and placed on no plan in the set; a column of a size no schedule in the set declares.</summary>
    private static IEnumerable<Finding> AcrossTheSet(IReadOnlyList<SheetRecord> records, double toleranceMm)
    {
        var placed = new HashSet<string>(records.SelectMany(r => r.ColumnAgreement?.Columns.Select(c => c.NearestMark) ?? Array.Empty<string?>())
            .Where(m => m is not null)!, StringComparer.OrdinalIgnoreCase);
        var declaredSizes = records.SelectMany(r => r.Furniture.DeclaredColumnSizesMm).Distinct().ToList();
        bool DeclaredAnywhere(double w, double d) => declaredSizes.Any(s =>
            (Math.Abs(s.W - w) <= toleranceMm && Math.Abs(s.D - d) <= toleranceMm) ||
            (Math.Abs(s.W - d) <= toleranceMm && Math.Abs(s.D - w) <= toleranceMm));

        foreach (var r in records)
        {
            if (r.ColumnAgreement is not { } ca) continue;
            var never = ca.MarksDeclaredButNeverFound.Where(m => !placed.Contains(m)).ToList();
            if (never.Count > 0)
                yield return new Finding(r.PageNumber, r.SheetNumber, "schedule marks placed on no plan in the set", $"{never.Count}: {string.Join(", ", never.Take(12))}{(never.Count > 12 ? " …" : "")}");
            var undeclared = ca.Columns.Where(c => !c.SizeIsDeclaredSomewhere && !DeclaredAnywhere(c.WidthMm, c.DepthMm)).ToList();
            if (undeclared.Count > 0 && ca.ColumnsFound > 0)
                yield return new Finding(r.PageNumber, r.SheetNumber, "columns of a size no schedule in the set declares",
                    $"{undeclared.Count} of {ca.ColumnsFound}: " + string.Join(", ", undeclared.Take(6).Select(c => $"{c.WidthMm / 25.4:0}x{c.DepthMm / 25.4:0} in at ({c.XMm / 1000:0.0}, {c.YMm / 1000:0.0}) m")));
        }
    }

    /// <summary>The findings across a set: every sheet's own, then the grid against the set's reference sheet, then the storeys against the model.</summary>
    public static Report Set(IReadOnlyList<SheetRecord> records, StoreyAgreement.Result? storeys = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        var findings = new List<Finding>();
        foreach (var r in records) findings.AddRange(Sheet(r));
        findings.AddRange(AcrossTheSet(records, PlanAgreesWithItsSchedule.DefaultToleranceMm));

        var numbered = records.Where(r => r.SheetNumber is not null).GroupBy(r => r.SheetNumber!, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToList();
        foreach (var g in numbered)
            findings.Add(new Finding(g.First().PageNumber, g.Key, "sheet number used twice", $"pages {string.Join(", ", g.Select(r => r.PageNumber))}"));

        // A GRID AXIS IS WHERE THE SET DRAWS IT. The plan with the most named axes is the reference;
        // every other plan is set on it by name, and an axis that then sits elsewhere is a finding.
        var plans = records.Where(r => r.SheetType == "plan" && r.Geometry.GridAxes.Count >= 3).ToList();
        var reference = plans.OrderByDescending(r => r.Geometry.GridAxes.Count).FirstOrDefault();
        if (reference is not null)
        {
            var refGrids = reference.Geometry.GridAxes.Select(a => new GridAlignment.ReferenceGrid(a.Name, a.Vertical, a.AtMm)).ToList();
            foreach (var r in plans.Where(p => !ReferenceEquals(p, reference)))
            {
                var axes = r.Geometry.GridAxes.Select(a => new GridAlignment.NamedAxis(a.Name, a.Vertical, a.AtMm)).ToList();
                var fit = GridAlignment.SolveByName(axes, refGrids);
                if (fit is null) continue;
                var off = new List<string>();
                foreach (var a in axes)
                {
                    var p0 = fit.Frame.Apply(new DxfPoint(a.Vertical ? a.At : 0, a.Vertical ? 0 : a.At));
                    bool vertical = Math.Abs(fit.Frame.Apply(new DxfPoint(a.Vertical ? a.At : 1000, a.Vertical ? 1000 : a.At)).X - p0.X) <= 1e-6;
                    double at = vertical ? p0.X : p0.Y;
                    var same = refGrids.Where(g => g.DirX == vertical && g.Label.Equals(a.Name, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (same.Count == 0) continue;
                    double nearest = same.Min(g => Math.Abs(g.Coord - at));
                    if (nearest > GridDisagreeMm) off.Add($"{a.Name} {nearest:0} mm");
                }
                if (off.Count > 0)
                    findings.Add(new Finding(r.PageNumber, r.SheetNumber, "grid axis elsewhere than the set draws it", $"against {reference.SheetNumber ?? "p" + reference.PageNumber}: {string.Join(", ", off.Take(8))}"));
            }
        }

        if (storeys is not null)
            foreach (var row in storeys.Rows.Where(x => x.ModelMm is double m && Math.Abs(m - x.DrawingMm) > StoreyAgreement.WithinMm))
                findings.Add(new Finding(0, null, "storey height differs from the model", $"{row.Level} -> {row.LevelBelow}: drawings {row.DrawingMm:0} mm, model {row.ModelMm:0} mm, on {row.Sheets} sheet(s)"));

        return new Report(findings, records.Count, records.Count(r => r.SheetType == "plan"));
    }

    private static bool Contains(FootingOutline x, double px, double py)
    {
        double x0 = x.Outline.Min(p => p.X), x1 = x.Outline.Max(p => p.X), y0 = x.Outline.Min(p => p.Y), y1 = x.Outline.Max(p => p.Y);
        double reach = Math.Max(x.LengthMm, x.WidthMm) / 2;
        return px >= x0 - reach && px <= x1 + reach && py >= y0 - reach && py <= y1 + reach;
    }
}

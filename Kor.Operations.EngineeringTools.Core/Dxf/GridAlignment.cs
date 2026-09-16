using Frame = Kor.Operations.EngineeringTools.Dxf.AnnotationOverlay.Frame;

namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>
/// Puts the drawing on the engineer's grid -- turned the right way, not merely centred.
///
/// WHY THIS EXISTS. The model published on 28 August opened with the building running north-south
/// while every drawing and DXF of it runs east-west. Andrea Neuviale, 31 August: "the rotation,
/// which is actually kind of important... when I import the DXF, I can't compare. Well, I could if
/// I rotate it and then move it. I mean, it's doable, but it's kind of a pain." Until it matches,
/// she cannot lay the DXF over the model, which is the first thing she does.
///
/// The cause was that alignment only ever translated. A Revit export in shared site coordinates
/// lands thousands of feet from a model built at its own origin AND, on this job, ninety degrees
/// around from it -- project north against plan north. Centring fixed the first and left the second.
///
/// HOW IT IS SOLVED. Not by guessing from a bounding box, which cannot tell 0 from 180 and is
/// ambiguous on a square building. The drawings carry their own grid lines and the engineer's model
/// carries hers, with the spacings of a real building: on 31168 the X grids run
/// 93.4, 141.2, 326, 326, 326, 287, 39, 175, 188, 194.5, 131.5, 86.5, 239.5, 326, 326, 326, 326, 127
/// inches apart. That sequence is a fingerprint. It appears in the drawing exactly once, along the
/// drawing's Y axis and in reverse -- which is the whole answer: the rotation, its direction, and
/// the offset that makes the two coincide.
///
/// Measured on 31168 across 19 grid lines on one axis and 16 on the other, every spacing matching
/// within a tenth of an inch and the two spans agreeing to 3984.6 against 3984.7.
/// </summary>
public static class GridAlignment
{
    /// <summary>Two grid lines nearer than this are the same line drawn twice.</summary>
    private const double SamePosition = 1.0;

    /// <summary>A matched pair may be this far apart. Grid coordinates are drafted, not derived.</summary>
    private const double Tolerance = 2.0;

    /// <summary>Below this many matched lines the fingerprint is a coincidence, not a fit.</summary>
    private const int LeastConvincing = 4;

    public sealed record Fit(Frame Frame, int MatchedX, int MatchedY, string Note);

    /// <summary>
    /// The frame that carries drawing coordinates onto the reference model's grid, or null when the
    /// drawings and the model do not share enough grid to say. Null means "leave it alone": a wrong
    /// rotation is far worse than none, because it looks deliberate.
    /// </summary>
    public static Fit? Solve(
        IEnumerable<DxfSegment> segments,
        IReadOnlyList<double> referenceX,
        IReadOnlyList<double> referenceY,
        Func<string, bool>? isGridLayer = null)
    {
        isGridLayer ??= LooksLikeAGridLayer;

        var (drawingX, drawingY) = GridPositions(segments, isGridLayer);
        if (drawingX.Count == 0 && drawingY.Count == 0) return null;
        if (referenceX.Count == 0 && referenceY.Count == 0) return null;

        Fit? best = null;
        foreach (int degrees in new[] { 0, 90, 180, 270 })
        {
            // Where each drawing axis lands, and with what sign, under this rotation.
            //   0:   X <- +x   Y <- +y        90:  X <- -y   Y <- +x
            //   180: X <- -x   Y <- -y        270: X <- +y   Y <- -x
            var (forX, signX) = degrees switch
            {
                0 => (drawingX, 1.0),
                90 => (drawingY, -1.0),
                180 => (drawingX, -1.0),
                _ => (drawingY, 1.0),
            };
            var (forY, signY) = degrees switch
            {
                0 => (drawingY, 1.0),
                90 => (drawingX, 1.0),
                180 => (drawingY, -1.0),
                _ => (drawingX, -1.0),
            };

            var (offsetX, matchedX) = BestShift(forX.Select(v => v * signX).ToList(), referenceX);
            var (offsetY, matchedY) = BestShift(forY.Select(v => v * signY).ToList(), referenceY);

            if (best is null || matchedX + matchedY > best.MatchedX + best.MatchedY)
                best = new Fit(
                    new Frame(degrees, offsetX, offsetY), matchedX, matchedY,
                    $"{matchedX} of {referenceX.Count} grid line(s) on the model's X and " +
                    $"{matchedY} of {referenceY.Count} on its Y matched the drawing's own grid at " +
                    $"{degrees}°.");
        }

        if (best is null) return null;

        // One axis fitting well is enough to fix the rotation -- 31168's model carries nineteen
        // grids on X and two on Y, so demanding both would refuse a job that is plainly aligned.
        int strongest = Math.Max(best.MatchedX, best.MatchedY);
        return strongest >= LeastConvincing ? best : null;
    }

    /// <summary>
    /// The distinct positions of axis-parallel grid lines: one list of constant-x lines, one of
    /// constant-y. A grid line is long -- bubbles, tags and leaders on the same layer are not.
    /// </summary>
    private static (List<double> X, List<double> Y) GridPositions(
        IEnumerable<DxfSegment> segments, Func<string, bool> isGridLayer)
    {
        var xs = new List<double>();
        var ys = new List<double>();

        foreach (var s in segments)
        {
            if (!isGridLayer(s.Layer)) continue;

            double dx = Math.Abs(s.End.X - s.Start.X), dy = Math.Abs(s.End.Y - s.Start.Y);
            if (Math.Max(dx, dy) < 120.0) continue; // ten feet: a bubble is not a grid line

            if (dx <= SamePosition && dy > SamePosition) xs.Add(s.Start.X);
            else if (dy <= SamePosition && dx > SamePosition) ys.Add(s.Start.Y);
        }

        return (Distinct(xs), Distinct(ys));
    }

    private static List<double> Distinct(List<double> values)
    {
        values.Sort();
        var kept = new List<double>();
        foreach (double v in values)
            if (kept.Count == 0 || v - kept[^1] > SamePosition)
                kept.Add(v);
        return kept;
    }

    /// <summary>
    /// The translation that lands the most drawing lines on reference lines. Every pairing is a
    /// candidate shift; the best one wins, which is what makes an irregular grid a fingerprint
    /// rather than a guess.
    /// </summary>
    private static (double Offset, int Matched) BestShift(
        IReadOnlyList<double> drawing, IReadOnlyList<double> reference)
    {
        if (drawing.Count == 0 || reference.Count == 0) return (0.0, 0);

        double bestOffset = 0.0;
        int bestCount = 0;

        foreach (double d in drawing)
        {
            foreach (double r in reference)
            {
                double shift = r - d;
                int count = 0;
                foreach (double other in drawing)
                {
                    double moved = other + shift;
                    foreach (double candidate in reference)
                    {
                        if (Math.Abs(candidate - moved) > Tolerance) continue;
                        count++;
                        break;
                    }
                }

                if (count > bestCount) { bestCount = count; bestOffset = shift; }
            }
        }

        // One pairing fixes WHICH lines correspond; it should not fix the distance. Anchoring on a
        // single pair inherits that pair's drafting rounding -- a sixth of an inch on 31168, which
        // is nothing structurally and is still a model that does not quite land on its grid. The
        // residual is averaged over every matched pair instead, so the fit is the whole grid's
        // answer rather than one line's.
        if (bestCount > 0)
        {
            double total = 0.0;
            int counted = 0;
            foreach (double d in drawing)
            {
                double moved = d + bestOffset;
                double nearest = double.NaN;
                double gap = Tolerance;
                foreach (double candidate in reference)
                {
                    double away = Math.Abs(candidate - moved);
                    if (away > gap) continue;
                    gap = away;
                    nearest = candidate;
                }

                if (double.IsNaN(nearest)) continue;
                total += nearest - d;
                counted++;
            }

            if (counted > 0) bestOffset = total / counted;
        }

        return (bestOffset, bestCount);
    }

    /// <summary>
    /// A layer whose name says grid. Kept deliberately loose: the office's own layer names are the
    /// firm's and belong in KorStandards, but a grid layer is called a grid layer everywhere this
    /// tool has looked -- JBP_G_GRID, JBP_G_GRID-1, S-GRID, A-GRID.
    /// </summary>
    public static bool LooksLikeAGridLayer(string layer) =>
        layer.Contains("GRID", StringComparison.OrdinalIgnoreCase);

    // ── By name (intake step 15) ───────────────────────────────────────────────────────────────
    //
    // A SHEET FROM THE STICK FILE SITS ON THE MODEL'S GRID BY THE NAMES OF ITS AXES. The fit above
    // fingerprints grid POSITIONS across a whole set, one frame for all of it, which is right for a
    // Revit export in shared coordinates and wrong for a sheet read off a PDF: each page is in its
    // own frame, millimetres from its own corner. But the intake reads the grid as NAMED axes
    // (step 8) and writes each name at its line's ends on the GRID layer, and the model's GRIDS
    // table names its lines too. Axis "5" is grid "5": the match is exact, needs no fingerprint,
    // and is one frame per sheet.

    /// <summary>A grid axis the drawing names: a grid-layer line with its label at an end.</summary>
    public sealed record NamedAxis(string Name, bool Vertical, double At);

    /// <summary>A grid line of the model: LABEL, DIR and COORD from its GRIDS table.</summary>
    public sealed record ReferenceGrid(string Label, bool DirX, double Coord);

    /// <summary>Text on the grid layer names the line whose end it sits at, within this many text heights, or 60 units.</summary>
    private const double NameReachHeights = 4.0, NameReachFloor = 60.0;

    /// <summary>
    /// Two named lines may disagree by this much and still be the same line: a plan read at 1:96
    /// resolves to about an inch, and the drafter's grid is drafted, not derived. Wider than the
    /// fingerprint's <see cref="Tolerance"/>, because a name is the match; the position only confirms it.
    /// </summary>
    public const double NameTolerance = 6.0;

    /// <summary>Fewer named lines than this agreeing on one frame is a coincidence, not a fit.</summary>
    public const int LeastConvincingByName = 3;

    /// <summary>
    /// A grid name is what the bubble says (intake step 89, 2026-09-16; WP6a item 4(b)). Measured over every grid-layer
    /// text the 294 read sets write (`corpus-query grid-names`): "1", "19", "R", "AA", "1A" — three characters — and the
    /// marks drafters put in a grid name: the building's tag and a hyphen ("A1-5", "E-P1", "0-11"; 16 sets), a point for
    /// a grid between two ("A.8", "T1.1", "C1.3", "P.11"; 30884, 90097, 31083 …), a prime for a grid beside one ("P2'",
    /// "0'", "D'"; 31040, 70057, 31052 …), a four-character name of letters and digits ("MH14"; 31139), and a bar
    /// between two names of one line ("1|P-1", "EA|WA"; 31128, 30867 — the tower's grid and the parkade's are the same
    /// line under two names). With "three characters at most" the rule, 31183's every axis (A1-5 … A1-C) was refused:
    /// its ZONE A and ZONE B plans found no name in common with the model and stood on their own page origins, one
    /// over the other; 86 of 294 sets carried grid text the rule refused. GRID, the word, and a bare number of four
    /// digits (a dimension on the grid layer) are not names.
    /// </summary>
    public static bool IsGridName(string? text)
    {
        if (text is null) return false;
        string s = text.Trim().TrimEnd('\'');            // a prime beside a name is the drafter's mark
        if (s.EndsWith('.') && s[..^1].Any(char.IsDigit)) s = s[..^1];   // and a period after a numbered one (31150's "19."); "TYP." stays a word
        int sep = s.IndexOfAny(['-', '.']);
        if (sep < 0) return Plain(s);
        return Plain(s[..sep]) && Plain(s[(sep + 1)..], allowFour: false);
        static bool Plain(string part, bool allowFour = true) =>
            part.Length is >= 1 and <= 3 && part.All(char.IsLetterOrDigit)
            || allowFour && part.Length == 4 && part.All(char.IsLetterOrDigit) && part.Any(char.IsLetter) && part.Any(char.IsDigit);
    }

    /// <summary>
    /// The names one grid-layer text gives a line: one, or two joined by a bar ("1|P-1" — the tower's name and the
    /// parkade's for one line); none for a word. Three or more behind bars ("1|1'|12'|8|9" on 31065, "CA|CB|…|CK" on
    /// 31083) is a bubble the reader found several labels in — a stack of tags, not a line's name — and names nothing:
    /// taken as five names it wrote five Y grids at one coordinate into 31065's model.
    /// </summary>
    public static IReadOnlyList<string> GridNamesIn(string? text)
    {
        if (text is null) return [];
        var parts = text.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length > 2) return [];
        return parts.Where(IsGridName).ToList();
    }

    /// <summary>
    /// The drawing's named axes: each grid-layer text of a grid-name's length paired with the
    /// nearest end of a grid-layer line within reach; the axis is that line's constant coordinate.
    /// </summary>
    public static List<NamedAxis> NamedAxes(
        IEnumerable<DxfSegment> segments, IEnumerable<DxfPositionedTag> tags, Func<string, bool>? isGridLayer = null)
    {
        isGridLayer ??= LooksLikeAGridLayer;
        var lines = new List<(bool Vertical, double At, DxfPoint A, DxfPoint B)>();
        foreach (var s in segments)
        {
            if (!isGridLayer(s.Layer)) continue;
            double dx = Math.Abs(s.End.X - s.Start.X), dy = Math.Abs(s.End.Y - s.Start.Y);
            if (Math.Max(dx, dy) < 120.0) continue;
            if (dx <= SamePosition && dy > SamePosition) lines.Add((true, s.Start.X, s.Start, s.End));
            else if (dy <= SamePosition && dx > SamePosition) lines.Add((false, s.Start.Y, s.Start, s.End));
        }
        var axes = new List<NamedAxis>();
        if (lines.Count == 0) return axes;
        foreach (var t in tags)
        {
            if (!isGridLayer(t.Layer)) continue;
            // a grid name is what the bubble says (IsGridName); a bar joins two names of one line; GRID is a word
            var names = GridNamesIn(t.Text);
            if (names.Count == 0) continue;
            double reach = Math.Max(NameReachFloor, NameReachHeights * t.Height);
            (bool Vertical, double At, DxfPoint A, DxfPoint B) best = default;
            double bestDistance = double.MaxValue;
            foreach (var l in lines)
            {
                double d = Math.Min(t.Point.DistanceTo(l.A), t.Point.DistanceTo(l.B));
                if (d < bestDistance) { bestDistance = d; best = l; }
            }
            if (bestDistance > reach) continue;
            foreach (string name in names)
            {
                if (axes.Any(a => a.Vertical == best.Vertical && Math.Abs(a.At - best.At) <= SamePosition
                                  && a.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                axes.Add(new NamedAxis(name, best.Vertical, best.At));
            }
        }
        return axes;
    }

    /// <summary>
    /// The frame that carries this sheet onto the model's grid by the names of its axes, in the
    /// model's unit (<paramref name="scale"/> is drawing unit per model unit), or null when fewer
    /// than <see cref="LeastConvincingByName"/> named lines agree on one. Tried at each quarter
    /// turn: a sheet drawn to plan north whose vertical axes carry the model's Y labels is turned.
    /// </summary>
    /// <param name="preferSmallerMove">Break a tie between two clusters of one label each by the smaller move - for a REISSUE, where
    /// both issues share one page frame and the page is the same page until the names say otherwise. Off for placing a sheet on a
    /// model, where the offset is where the sheet happens to sit against the model's origin and a tie goes to the first cluster.</param>
    public static Fit? SolveByName(IReadOnlyList<NamedAxis> axes, IReadOnlyList<ReferenceGrid> reference, double scale = 1.0, bool preferSmallerMove = false)
    {
        if (axes.Count == 0 || reference.Count == 0) return null;
        var refX = reference.Where(g => g.DirX).ToList();
        var refY = reference.Where(g => !g.DirX).ToList();
        var vertical = axes.Where(a => a.Vertical).ToList();
        var horizontal = axes.Where(a => !a.Vertical).ToList();

        Fit? best = null;
        double bestSpread = double.MaxValue;
        foreach (int degrees in new[] { 0, 90, 180, 270 })
        {
            // Which drawing axes land on the model's X lines, and with what sign: Frame.Apply turns
            // about the origin, so at 90° a horizontal axis at y becomes a constant-x line at -y.
            var (toX, signX, toY, signY) = degrees switch
            {
                0 => (vertical, 1.0, horizontal, 1.0),
                90 => (horizontal, -1.0, vertical, 1.0),
                180 => (vertical, -1.0, horizontal, -1.0),
                _ => (horizontal, 1.0, vertical, -1.0),
            };
            var (ox, mx, sx) = AgreedOffset(toX, refX, signX * scale, preferSmallerMove);
            var (oy, my, sy) = AgreedOffset(toY, refY, signY * scale, preferSmallerMove);
            if (mx == 0 || my == 0) continue;
            double spread = Math.Max(sx, sy);
            if (best is null || mx + my > best.MatchedX + best.MatchedY
                || (mx + my == best.MatchedX + best.MatchedY && spread < bestSpread))
            {
                best = new Fit(new Frame(degrees, ox, oy), mx, my,
                    $"{mx} of {refX.Count} X and {my} of {refY.Count} Y grid lines matched by name at {degrees}°, agreeing within {spread:0.0}.");
                bestSpread = spread;
            }
        }
        return best is not null && best.MatchedX + best.MatchedY >= LeastConvincingByName ? best : null;
    }

    /// <summary>
    /// The translation the most named pairs agree on. Every axis carrying a label is paired with
    /// every grid line of that label and each pair casts one vote, coord - factor * at; the
    /// largest cluster of votes within <see cref="NameTolerance"/> is the frame, its mean the
    /// offset, the labels in it the match count (each label once), the farthest vote its spread.
    /// A vote and not a median because a sheet may carry a name more than once — a key plan with
    /// four buildings' grids, a sheet with two views — and pairing the first "2" with the first
    /// "2" set 31065's S1.11 on the wrong building (2026-09-08). The true pairs all vote for one
    /// offset; the cross pairs scatter theirs.
    /// </summary>
    private static (double Offset, int Matched, double Spread) AgreedOffset(
        List<NamedAxis> axes, List<ReferenceGrid> grids, double factor, bool preferSmallerMove)
    {
        var votes = new List<(string Label, double Offset)>();
        foreach (var g in grids)
            foreach (var axis in axes.Where(a => a.Name.Equals(g.Label, StringComparison.OrdinalIgnoreCase)))
                votes.Add((g.Label, g.Coord - axis.At * factor));
        if (votes.Count == 0) return (0, 0, 0);

        var best = new List<(string Label, double Offset)>();
        int bestLabels = -1;
        double bestSpread = double.MaxValue;
        foreach (var centre in votes)
        {
            var cluster = votes.Where(v => LoopGeometry.Within(Math.Abs(v.Offset - centre.Offset), NameTolerance)).ToList();
            int labels = cluster.Select(v => v.Label.ToUpperInvariant()).Distinct().Count();
            double mean = cluster.Average(v => v.Offset);
            double spread = cluster.Max(v => Math.Abs(v.Offset - mean));
            // more labels wins; then more votes; then the TIGHTEST cluster; then the first - or, for a reissue,
            // the smaller move. The smaller move was the rule for every fit, and it is a property of where the
            // sheet happens to sit against the model's origin, not of the fit: with the frame the page's instead
            // of the content's, 31065's fits chose other clusters and every member stood 3 mm off its grid, and
            // its L1 plan still did once the spread came first (intake step 54, 2026-09-13). For a reissue both
            // issues share one page frame, one name each way IS a tie, and the page is the same page until the
            // names say otherwise (AReissueIsWhatMovedTests); there the smaller move stays.
            bool better = labels > bestLabels
                          || (labels == bestLabels && cluster.Count > best.Count)
                          || (labels == bestLabels && cluster.Count == best.Count && spread < bestSpread - 1e-6)
                          || (preferSmallerMove && labels == bestLabels && cluster.Count == best.Count && Math.Abs(spread - bestSpread) <= 1e-6
                              && Math.Abs(mean) < Math.Abs(best.Average(v => v.Offset)) - 1e-6);
            if (better)
            {
                best = cluster;
                bestLabels = labels;
                bestSpread = spread;
            }
        }
        double offset = best.Average(v => v.Offset);
        return (offset, bestLabels, best.Max(v => Math.Abs(v.Offset - offset)));
    }

    /// <summary>
    /// A placed sheet's named axes as grid lines of the model: each carried through the sheet's
    /// frame in the model's unit. An axis placed on the grid is a grid line for the rest of the
    /// set — the model's GRIDS names only what the engineer drew, and a sheet whose letters the
    /// model lacks can still be placed by a sheet that shares them and was placed.
    /// </summary>
    /// <summary>
    /// A FIT AT THE SHEET'S OWN SCALE (intake step 51, 2026-09-12). A key plan or a design load plan
    /// draws the whole grid in one view, usually smaller than the plans - 31130's DESIGN LOAD PLANS
    /// carries axes 1-16 and 17-28 together at half the plans' scale - and that is the one sheet on
    /// which the two halves of a building split on a bay share a frame. The ratio between the sheet's
    /// spacing and the model's for the widest pair of named axes it shares, in each direction, is its
    /// scale; where the two directions agree within two per cent the fit is solved at that scale and
    /// the ratio is in the note. Null where fewer than two names are shared in either direction, the
    /// directions disagree, or the fit at that scale is no fit.
    /// </summary>
    public static (Fit Fit, double Scale)? SolveByNameAtOwnScale(IReadOnlyList<NamedAxis> axes, IReadOnlyList<ReferenceGrid> reference, double scale = 1.0)
    {
        ArgumentNullException.ThrowIfNull(axes);
        ArgumentNullException.ThrowIfNull(reference);
        double? Ratio(bool vertical)
        {
            var mine = axes.Where(a => a.Vertical == vertical).ToList();
            var theirs = reference.Where(g => g.DirX == vertical).ToList();
            var shared = mine.Select(a => (a, g: theirs.FirstOrDefault(g => g.Label.Equals(a.Name, StringComparison.OrdinalIgnoreCase))))
                             .Where(x => x.g is not null).OrderBy(x => x.a.At).ToList();
            if (shared.Count < 2) return null;
            var lo = shared[0]; var hi = shared[^1];
            double mineSpan = (hi.a.At - lo.a.At) * scale, theirSpan = hi.g!.Coord - lo.g!.Coord;
            if (Math.Abs(mineSpan) < 1e-6 || Math.Abs(theirSpan) < 1e-6) return null;
            return Math.Abs(theirSpan / mineSpan);
        }
        double? rx = Ratio(true), ry = Ratio(false);
        double ratio = rx is double a && ry is double b ? (Math.Abs(a - b) <= 0.02 * Math.Max(a, b) ? (a + b) / 2 : double.NaN) : (rx ?? ry ?? double.NaN);
        if (double.IsNaN(ratio) || ratio <= 0) return null;
        var fit = SolveByName(axes, reference, scale * ratio);
        return fit is null ? null : (fit with { Note = fit.Note + $" (at its own scale, {ratio:0.###} times the plans')" }, scale * ratio);
    }

    /// <summary>
    /// Fewer of a sheet's members than this standing over placed members is a coincidence, not a fit (step 55) -
    /// and never fewer than half of them: a small top plan has five members (31168's LEVEL 35: two columns, three
    /// wall outlines), all of which stand over the tower's.
    /// </summary>
    public const int LeastConvincingByColumns = 4;

    /// <summary>A column stands over a placed one when it lands within this of it, in the model's unit at 1 mm; scaled with the sheet.</summary>
    public const double ColumnRegistrationMm = 100.0;

    /// <summary>
    /// A SHEET THAT NAMES NO AXIS STANDS WHERE ITS MEMBERS STAND (intake step 55, 2026-09-12). A small top
    /// plan carries two bubbles or none - 31168's LEVEL 35 PLAN - BLDG A names axes 4 and 5, its LEVEL 36
    /// names nothing - and stayed "in its own frame", which is wherever the page put it: near the tower
    /// while the frame was the drawn content's centroid, fifty metres off once the frame was the page's.
    /// Its columns and its core are the tower's: the displacement most of its members (column and wall
    /// outline centres) share with the members already placed (the yardstick's own registration - votes
    /// in 100 mm bins, the fullest bin refined to the median) is its frame, at 0 degrees, when at least
    /// <see cref="LeastConvincingByColumns"/> of them land within <see cref="ColumnRegistrationMm"/>.
    /// Null otherwise.
    /// </summary>
    public static Fit? SolveByColumns(IReadOnlyList<DxfPoint> sheetColumns, IReadOnlyList<DxfPoint> placedColumns, double scale = 1.0)
        => SolveByColumns(sheetColumns, placedColumns, scale, out _);

    /// <summary>As above, and says why when it is null: the fullest bin's support against what a fit takes.</summary>
    public static Fit? SolveByColumns(IReadOnlyList<DxfPoint> sheetColumns, IReadOnlyList<DxfPoint> placedColumns, double scale, out string why)
    {
        ArgumentNullException.ThrowIfNull(sheetColumns);
        ArgumentNullException.ThrowIfNull(placedColumns);
        why = string.Empty;
        double bin = ColumnRegistrationMm * scale;
        // A placed member is a PLACE, however many sheets or storeys stand one there: 31168's model holds 2,056
        // wall corners for some 70 walls, one panel per storey, and a vote per PAIR let two of a top plan's
        // corners over a thirty-storey core outvote its twenty-four columns (114 pairs against 24) - the
        // first cut of this step reported "2 of 44" on a sheet whose every column stood over the model's.
        // And a sheet's member is a place too: four wall axes meeting at one junction are one point, not
        // four of the quorum (Codex audit 2026-09-13, F5). Both sets are distinct BY DISTANCE (a tenth of a
        // bin, the first kept) and the minimum is asked of the places, not the entries.
        var sheet = DistinctPlaces(sheetColumns, bin * 0.1, scale);
        var placed = DistinctPlaces(placedColumns, bin * 0.1, 1.0);
        if (sheet.Count < LeastConvincingByColumns || placed.Count < LeastConvincingByColumns)
        { why = $"{sheet.Count} member place(s) on the sheet, {placed.Count} placed: fewer than {LeastConvincingByColumns}"; return null; }
        var votes = new Dictionary<(long, long), int>();
        foreach (var p in sheet)
        {
            var mine = new HashSet<(long, long)>();
            foreach (var q in placed)
                mine.Add(((long)Math.Round((q.X - p.X * scale) / bin), (long)Math.Round((q.Y - p.Y * scale) / bin)));
            foreach (var key in mine) votes[key] = votes.TryGetValue(key, out int n) ? n + 1 : 1;
        }
        if (votes.Count == 0) { why = "no pairs"; return null; }

        // EVERY BIN THAT COULD WIN IS REFINED, NOT THE FULLEST ALONE. The bins are a phase the frame chose: a
        // displacement its members share can straddle a bin edge and split three-and-two under a bin that
        // gathers four of someone else's (Codex audit 2026-09-13, F9). Each bin within one vote of the
        // fullest is refined to the median of the pairs around it and judged by its SUPPORT - the members
        // it lands within a bin of a placed one - and the best support wins. Two places that fit alike are
        // refused: a top plan of tower A over towers A and B with one core plan fits both (F4).
        int fullest = votes.Values.Max();
        var fits = new List<((double X, double Y) Shift, int Support)>();
        foreach (var (key, count) in votes.Where(kv => kv.Value >= Math.Max(1, fullest - 1)).OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.Item1).ThenBy(kv => kv.Key.Item2))
        {
            var coarse = (X: key.Item1 * bin, Y: key.Item2 * bin);
            var dxs = new List<double>(); var dys = new List<double>();
            foreach (var p in sheet)
                foreach (var q in placed)
                    if (Math.Abs(q.X - p.X * scale - coarse.X) <= 1.5 * bin && Math.Abs(q.Y - p.Y * scale - coarse.Y) <= 1.5 * bin) { dxs.Add(q.X - p.X * scale); dys.Add(q.Y - p.Y * scale); }
            if (dxs.Count == 0) continue;
            dxs.Sort(); dys.Sort();
            var shift = (X: dxs[dxs.Count / 2], Y: dys[dys.Count / 2]);
            int support = sheet.Count(p => placed.Any(q => LoopGeometry.Within(Math.Sqrt((q.X - p.X * scale - shift.X) * (q.X - p.X * scale - shift.X) + (q.Y - p.Y * scale - shift.Y) * (q.Y - p.Y * scale - shift.Y)), bin)));
            if (!fits.Any(f => Math.Abs(f.Shift.X - shift.X) <= bin && Math.Abs(f.Shift.Y - shift.Y) <= bin)) fits.Add((shift, support));
        }
        if (fits.Count == 0) { why = "the fullest bin holds no pair"; return null; }
        var best = fits.OrderByDescending(f => f.Support).First();
        if (best.Support < LeastConvincingByColumns || best.Support * 2 < sheet.Count)
        { why = $"the displacement most share, ({best.Shift.X:0}, {best.Shift.Y:0}), has {best.Support} of {sheet.Count} members standing over placed ones - a fit takes {LeastConvincingByColumns} and at least half"; return null; }
        var rival = fits.Where(f => f.Shift != best.Shift && f.Support == best.Support).ToList();
        if (rival.Count > 0)
        { why = $"two places fit alike: ({best.Shift.X:0}, {best.Shift.Y:0}) and ({rival[0].Shift.X:0}, {rival[0].Shift.Y:0}) each stand {best.Support} of {sheet.Count} members over placed ones - not placed"; return null; }
        return new Fit(new Frame(0, best.Shift.X, best.Shift.Y), 0, 0,
            $"set where {best.Support} of its {sheet.Count} columns and walls stand over members already placed (no axis named in common)");
    }

    /// <summary>The distinct places among points, by distance: a point within <paramref name="within"/> of one already kept is that one. The scale is applied before comparing; the result is in the caller's unit.</summary>
    private static List<DxfPoint> DistinctPlaces(IReadOnlyList<DxfPoint> points, double within, double scale)
    {
        var kept = new List<DxfPoint>();
        foreach (var p in points)
        {
            var q = new DxfPoint(p.X * scale, p.Y * scale);
            if (!kept.Any(k => LoopGeometry.Within(k.DistanceTo(q), within))) kept.Add(q);
        }
        return Math.Abs(scale - 1.0) < 1e-12 ? kept : kept.Select(k => new DxfPoint(k.X / scale, k.Y / scale)).ToList();
    }

    public static List<ReferenceGrid> Carried(IReadOnlyList<NamedAxis> axes, Frame frame, double scale)
    {
        var carried = new List<ReferenceGrid>();
        foreach (var a in axes)
        {
            var p0 = frame.Apply(a.Vertical ? new DxfPoint(a.At * scale, 0) : new DxfPoint(0, a.At * scale));
            var p1 = frame.Apply(a.Vertical ? new DxfPoint(a.At * scale, 1000) : new DxfPoint(1000, a.At * scale));
            bool dirX = Math.Abs(p1.X - p0.X) <= 1e-6;
            carried.Add(new ReferenceGrid(a.Name, dirX, dirX ? p0.X : p0.Y));
        }
        return carried;
    }

    /// <summary>
    /// The GRIDS table a model built without a reference gets: every named axis of every sheet,
    /// carried through the sheet's frame into the model, one line per name, at the median where
    /// several sheets draw it. ETABS's own form, as the office's exports carry it.
    /// </summary>
    public static List<string> GridLines(IEnumerable<(IReadOnlyList<NamedAxis> Axes, Frame Frame, double Scale)> sheets)
    {
        var byName = new Dictionary<(string Name, bool DirX), List<double>>();
        foreach (var (axes, frame, scale) in sheets)
        {
            foreach (var a in axes)
            {
                var p0 = frame.Apply(a.Vertical ? new DxfPoint(a.At * scale, 0) : new DxfPoint(0, a.At * scale));
                var p1 = frame.Apply(a.Vertical ? new DxfPoint(a.At * scale, 1000) : new DxfPoint(1000, a.At * scale));
                bool dirX = Math.Abs(p1.X - p0.X) <= 1e-6;
                var key = (a.Name.ToUpperInvariant(), dirX);
                if (!byName.TryGetValue(key, out var list)) byName[key] = list = new List<double>();
                list.Add(dirX ? p0.X : p0.Y);
            }
        }
        if (byName.Count == 0) return new List<string>();
        var lines = new List<string> { "  GRIDSYSTEM \"G1\"  TYPE \"CARTESIAN\"  BUBBLESIZE 60 " };
        foreach (var entry in byName
                     .Select(kv => (kv.Key.Name, kv.Key.DirX, Coord: Median(kv.Value)))
                     .OrderByDescending(e => e.DirX).ThenBy(e => e.Coord))
        {
            lines.Add($"  GRID \"G1\"  LABEL \"{entry.Name}\"  DIR \"{(entry.DirX ? "X" : "Y")}\"  COORD " +
                      entry.Coord.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                      " VISIBLE \"Yes\"  BUBBLELOC \"End\"  ");
        }
        return lines;

        static double Median(List<double> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            int n = sorted.Count;
            return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2;
        }
    }
}

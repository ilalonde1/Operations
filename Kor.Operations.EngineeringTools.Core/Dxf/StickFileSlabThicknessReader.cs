using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using UglyToad.PdfPig;

namespace Kor.Operations.EngineeringTools.Dxf;

public sealed record StickFileSlabThicknessMatch(int PageNumber, int ThicknessInches, string MatchedTitle);

public sealed record StickFileSlabThicknessPage(
    int PageNumber,
    IReadOnlyList<string> Titles,
    IReadOnlyList<string> Lines)
{
    /// <summary>
    /// The titles drawn in this page's OWN title block, as opposed to every plan-shaped line on
    /// the sheet. Only these say what drawing this is.
    ///
    /// It matters because a details sheet mentions storeys constantly. Matching a sheet to a page
    /// on any plan-shaped line put LEVEL 1's thickness on page 6, a typical-details sheet, when
    /// LEVEL 1's plan is page 16 -- a 5in field slab where the drawing says 14in.
    /// </summary>
    public IReadOnlyList<string> TitleBlockTitles { get; init; } = Array.Empty<string>();
}

public static class StickFileSlabThicknessReader
{
    private static readonly Regex NonIdentity = new(@"[^A-Z0-9]+", RegexOptions.Compiled);
    private static readonly Regex Building = new(@"BLDG\s*(?<tags>[A-Z](?:\s*(?:&|AND)\s*[A-Z])*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// How far below a title its continuation may sit, and how far its left edge may wander.
    /// Measured on 31168's page 16, where the two halves are 21.3pt apart and share x to a tenth
    /// of a point. 30pt admits a slightly looser title block without reaching the next entry.
    /// </summary>
    private const double TitleWrapDropPt = 30.0;
    private const double TitleWrapColumnPt = 6.0;

    /// <summary>A building written as a prefix on the storey: "A-LEVEL 28", "C-ROOF".</summary>
    private static readonly Regex PrefixBuilding = new(@"(?<![A-Z0-9])(?<tag>[A-Z])-(?=LEVEL|ROOF)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IReadOnlyDictionary<string, StickFileSlabThicknessMatch> ReadBySheet(
        IReadOnlyList<PlanSheetInfo> sheets,
        string? stickFilePdf)
    {
        if (string.IsNullOrWhiteSpace(stickFilePdf))
            return new Dictionary<string, StickFileSlabThicknessMatch>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(stickFilePdf))
            throw new FileNotFoundException("The stick file PDF was not found.", stickFilePdf);

        using var doc = PdfDocument.Open(stickFilePdf);
        var pages = new List<StickFileSlabThicknessPage>();
        for (int pageNumber = 1; pageNumber <= doc.NumberOfPages; pageNumber++)
        {
            var page = VectorPageReader.ReadPage(doc.GetPage(pageNumber));
            pages.Add(FromPageContent(page));
        }

        return MatchSheetsToPages(sheets, pages);
    }

    public static IReadOnlyDictionary<string, StickFileSlabThicknessMatch> MatchSheetsToPages(
        IReadOnlyList<PlanSheetInfo> sheets,
        IReadOnlyList<StickFileSlabThicknessPage> pages)
    {
        var pageTitles = pages
            .SelectMany(p => p.Titles
                .Select(t => new PageTitle(p, RawTitle: t, Key: TitleKey(t),
                    FromTitleBlock: p.TitleBlockTitles.Contains(t, StringComparer.OrdinalIgnoreCase)))
                .Where(t => t.Key.Length > 0))
            .ToList();

        var result = new Dictionary<string, StickFileSlabThicknessMatch>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in sheets)
        {
            string sheetKey = TitleKey(sheet.FileName);
            if (sheetKey.Length == 0) continue;

            var sheetId = SheetIdentity.Of(sheet.FileName);

            var candidates = pageTitles
                .Where(t => string.Equals(t.Key, sheetKey, StringComparison.OrdinalIgnoreCase)
                            || (t.FromTitleBlock && SheetIdentity.Of(t.RawTitle).SameStoreyAs(sheetId)))
                .Select(t => (t.Page, t.RawTitle, Thickness: SlabThicknessReader.DominantThicknessIn(t.Page.Lines)))
                .Where(t => t.Thickness is > 0)
                .ToList();
            if (candidates.Count == 0) continue;

            var chosen = candidates
                .OrderBy(c => IsArchitecturalBackgroundDuplicate(c.Page) ? 1 : 0)
                // On 31168 the structural set is pages 10-31 and the with-architectural-background
                // repeat is pages 42-63, so the lower page is the structural answer when both titles
                // otherwise look the same.
                .ThenBy(c => c.Page.PageNumber)
                .ThenBy(c => c.RawTitle, StringComparer.OrdinalIgnoreCase)
                .First();

            result[sheet.FileName] = new StickFileSlabThicknessMatch(
                chosen.Page.PageNumber,
                chosen.Thickness!.Value,
                chosen.RawTitle);
        }

        return result;
    }

    public static StickFileSlabThicknessPage FromPageContent(VectorPageReader.PageContent page)
    {
        var textLines = VectorPageReader.ReadTextLines(page)
            .Where(l => l.Text.Trim().Length > 0)
            .ToList();

        var lines = textLines.Select(l => l.Text.Trim()).ToList();

        // A DRAWN TITLE WRAPS, AND HALF A TITLE MATCHES NOTHING.
        //
        // 31168's page 16 sets its title on two baselines: "LEVEL 1 PLAN - CONCRETE" at
        // (3190.6, 2382.5) and "OUTLINE - BLDG C" at (3190.6, 2403.8). Read one baseline at a
        // time that yields "LEVEL 1 PLAN CONCRETE" against a sheet key of "LEVEL 1 PLAN CONCRETE
        // OUTLINE", and the page is passed over. Three of fourteen storeys matched before this,
        // and the three that did had their whole title on one baseline.
        //
        // The join has to be GEOMETRIC, not by reading order. Lines come back sorted down the
        // page, and at the title's height the far-left general notes interleave with it -- the
        // line after the title in that list sits at x=1051 and is about dowels. What continues a
        // title is the line directly BELOW IT IN THE SAME COLUMN.
        var joined = new List<string>();
        foreach (var head in textLines)
        {
            var tail = textLines
                .Where(l => l.Y < head.Y && head.Y - l.Y <= TitleWrapDropPt)
                .Where(l => Math.Abs(l.MinX - head.MinX) <= TitleWrapColumnPt)
                .OrderByDescending(l => l.Y)
                .FirstOrDefault();

            if (tail.Text is { Length: > 0 })
                joined.Add($"{head.Text.Trim()} {tail.Text.Trim()}");
        }

        var titles = lines.Concat(joined)
            .SelectMany(PlanTitleCandidates)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // THE TITLE BLOCK, AND WHAT SITS IN IT.
        //
        // A title-block entry is the page saying what drawing it IS. Everything else that looks
        // like a plan title is the page MENTIONING a drawing -- a detail keyed to a level, a note
        // about the storey above. Only the first can be matched on storey identity, so they are
        // kept apart here rather than pooled.
        //
        // The block is the column the title is drawn in, so a wrapped title and the entries around
        // it come from the same x. SheetTitleReader finds the block's own title; the joined lines
        // sharing its column are the rest of what it says.
        var blockTitles = new List<string>();
        if (SheetTitleReader.FromPage(page)?.Raw is { Length: > 0 } titleBlockTitle &&
            LooksLikePlanTitle(titleBlockTitle))
        {
            blockTitles.Add(titleBlockTitle);
            if (!titles.Contains(titleBlockTitle, StringComparer.OrdinalIgnoreCase))
                titles.Add(titleBlockTitle);

            var block = textLines.FirstOrDefault(l =>
                string.Equals(l.Text.Trim(), titleBlockTitle, StringComparison.OrdinalIgnoreCase));

            if (block.Text is { Length: > 0 })
            {
                foreach (string candidate in joined
                             .Where(j => j.StartsWith(titleBlockTitle, StringComparison.OrdinalIgnoreCase))
                             .SelectMany(PlanTitleCandidates))
                {
                    if (!blockTitles.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                        blockTitles.Add(candidate);
                    if (!titles.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                        titles.Add(candidate);
                }
            }
        }

        return new StickFileSlabThicknessPage(page.PageNumber, titles, lines)
        {
            TitleBlockTitles = blockTitles,
        };
    }

    private static bool LooksLikePlanTitle(string line)
    {
        if (line.Contains("SCHEDULE", StringComparison.OrdinalIgnoreCase)) return false;
        if (SheetTitleReader.ParseTitleLine(line) is not null) return true;

        var sheet = PlanSheetNaming.Parse(line + ".dxf");
        return sheet.HasPlacement &&
               (line.Contains("PLAN", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("FOUNDATION", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> PlanTitleCandidates(string line)
    {
        if (LooksLikePlanTitle(line)) yield return line;

        var parts = Regex.Split(line, @"(?=\bLEVEL\b)", RegexOptions.IgnoreCase)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();
        if (parts.Count < 2) yield break;

        foreach (string part in parts)
            if (LooksLikePlanTitle(part))
                yield return part;
    }

    private static bool IsArchitecturalBackgroundDuplicate(StickFileSlabThicknessPage page)
        => page.Titles.Concat(page.Lines).Any(t =>
            t.Contains("ARCHITECTURAL", StringComparison.OrdinalIgnoreCase) &&
            t.Contains("BACKGROUND", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// WHICH STOREY, AND WHOSE, rather than what the title happens to say.
    ///
    /// Matching on normalised title text can only ever join sheets whose names were written the
    /// same way, and on 31168 they are not. The drafter names a DXF for the storey alone --
    /// "C-LEVEL 6" -- and titles the drawing in full: "LEVEL 5 (L5-L8) PLAN - CONCRETE OUTLINE -
    /// BLDG C". Three of fourteen storeys matched on text, and the three that did were the ones
    /// whose file name happened to carry the whole title.
    ///
    /// PlanSheetNaming already reads a name into the levels and buildings it serves, which is the
    /// thing actually being compared. A sheet and a page are the same drawing when they claim the
    /// same storey of the same building.
    ///
    /// The building is REQUIRED to agree where both state one. 31168's towers carry an A and a B
    /// LEVEL 28 that stand 130 ft apart, and a thickness landing on the wrong one is precisely the
    /// fault this matcher must be incapable of. Where neither names a building -- a podium sheet
    /// on a single-building job -- storey identity alone is enough.
    /// </summary>
    private readonly record struct SheetIdentity(
        IReadOnlyList<int> Levels,
        IReadOnlyList<int> Parkade,
        IReadOnlyList<string> Buildings,
        bool IsRoof,
        bool IsFoundation,
        bool IsMezzanine)
    {
        public static SheetIdentity Of(string name)
        {
            var s = PlanSheetNaming.Parse(name.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase)
                ? name
                : name + ".dxf");

            // A BUILDING WRITTEN AS A PREFIX STILL NAMES A BUILDING.
            //
            // PlanSheetNaming reads BuildingTags from a "BLDG C" tag, which is how the page titles
            // are written. The DXF file names say it the other way -- "C-LEVEL 6" -- and come back
            // with no tags at all. Treated as "this sheet names no building" that matches anything,
            // and C-LEVEL 4 through 9 and C-ROOF took their thickness off page 22 and page 24,
            // which are BLDG A. The YMCA is building C; those are the towers.
            var tags = s.BuildingTags;
            if (tags.Count == 0 && PrefixBuildingKey(name.ToUpperInvariant()) is { } prefixed)
                tags = new[] { prefixed["BLDG ".Length..] };

            return new SheetIdentity(
                s.Levels, s.ParkadeLevels, tags, s.IsRoof, s.IsFoundation, s.IsMezzanine);
        }

        public bool SameStoreyAs(SheetIdentity other)
        {
            if (IsMezzanine != other.IsMezzanine) return false;
            if (IsFoundation != other.IsFoundation) return false;

            bool storey =
                (Levels.Count > 0 && Levels.Intersect(other.Levels).Any()) ||
                (Parkade.Count > 0 && Parkade.Intersect(other.Parkade).Any()) ||
                (IsFoundation && other.IsFoundation) ||
                (IsRoof && other.IsRoof && Levels.Count == 0 && other.Levels.Count == 0);
            if (!storey) return false;

            // WHERE EITHER SIDE NAMES A BUILDING, BOTH MUST, AND THEY MUST AGREE.
            //
            // Letting a page that names no building match a sheet that does is how C-LEVEL 4
            // through 9 and C-ROOF took their thickness from pages 22 and 24 -- tower A's
            // drawings -- while the YMCA is building C. On a job whose towers stand 130 ft apart
            // that is a thickness on the wrong building, which is the one fault this matcher is
            // supposed to be incapable of. A storey that cannot prove which building it belongs to
            // keeps the engineer's default and is reported as assumed.
            if (Buildings.Count == 0 && other.Buildings.Count == 0) return true;
            if (Buildings.Count == 0 || other.Buildings.Count == 0) return false;
            return Buildings.Intersect(other.Buildings, StringComparer.OrdinalIgnoreCase).Any();
        }
    }

    private static string TitleKey(string title)
    {
        string s = Path.GetExtension(title).Equals(".dxf", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(title)
            : Path.GetFileName(title);
        s = s.ToUpperInvariant();
        string? building = BuildingKey(s) ?? PrefixBuildingKey(s);

        int marker = new[] { "LEVEL", "ROOF", "GROUND", "MAIN", "FOUNDATION" }
            .Select(m => s.IndexOf(m, StringComparison.OrdinalIgnoreCase))
            .Where(i => i >= 0)
            .DefaultIfEmpty(-1)
            .Min();
        if (marker > 0) s = s[marker..];

        s = s.Replace("WITH ARCHITECTURAL BACKGROUND", " ", StringComparison.OrdinalIgnoreCase)
             .Replace("ARCHITECTURAL BACKGROUND", " ", StringComparison.OrdinalIgnoreCase);
        s = NonIdentity.Replace(s, " ");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        if (building is not null && !s.Contains("BLDG ", StringComparison.OrdinalIgnoreCase))
            s = $"{s} {building}";
        return s;
    }

    /// <summary>
    /// The building a storey is named for, where the drafter wrote it as a prefix rather than a
    /// BLDG tag: "A-LEVEL 28", "C-LEVEL 6".
    ///
    /// This matters more than it looks. The key is cut at the first LEVEL, which throws the prefix
    /// away, so A-LEVEL 28 and B-LEVEL 28 both used to reduce to "LEVEL 28" -- two different
    /// buildings, one key, on a job whose towers are 130 ft apart. Nothing wrong shipped, because
    /// this job's page titles carry BLDG tags and so failed to match either, but that is luck
    /// rather than a design. A thickness on the wrong tower is exactly the fault this matcher is
    /// supposed to be incapable of.
    /// </summary>
    private static string? PrefixBuildingKey(string text)
    {
        var m = PrefixBuilding.Match(text);
        return m.Success ? $"BLDG {char.ToUpperInvariant(m.Groups["tag"].Value[0])}" : null;
    }

    private static string? BuildingKey(string text)
    {
        var m = Building.Match(text);
        if (!m.Success) return null;

        var tags = Regex.Split(m.Groups["tags"].Value, @"\s*(?:&|AND)\s*", RegexOptions.IgnoreCase)
            .Select(p => p.Trim())
            .Where(p => p.Length == 1 && char.IsLetter(p[0]))
            .Select(p => char.ToUpperInvariant(p[0]))
            .Distinct()
            .OrderBy(c => c)
            .ToArray();
        return tags.Length == 0 ? null : $"BLDG {string.Join(" ", tags)}";
    }

    private sealed record PageTitle(StickFileSlabThicknessPage Page, string RawTitle, string Key, bool FromTitleBlock);
}

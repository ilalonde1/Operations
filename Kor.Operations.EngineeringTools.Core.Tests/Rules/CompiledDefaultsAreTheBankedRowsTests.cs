#nullable enable
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Rules;

/// <summary>
/// A rule's number lives in two places — the KorStandards row and the compiled default that stands
/// in for it when no connection is given — and until 2026-09-08 nothing compared them. Measured
/// that day: 46 of 49 numeric rows agreed with the code; `dxf.max-wall-thickness` was 60 in the
/// row (migration 038, corpus of 1,126 models) and 36 in the code, `dxf.max-column-size` 132
/// against 96, and `dxf.outline-self-touch-tolerance` 0.5 in the row against 0.05 in the code and
/// in the row's own migration. A production run read one number and every default-mode run —
/// tests, the WPF window, the intake's instruments — read another.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: every key `DxfToEtabsService.BuiltInRuleValues` and
/// `PdfIntakeOptions.BuiltInRuleValues` know, against the value `RuleSettings.Load` returns for
/// it; a key with no row must be declared unbanked here with a reason; and every public numeric
/// property on the two option records must be a rule or be declared not one (the orphan detector).
/// WHAT IT DOES NOT: whether a row is RIGHT — that is the corpus measurement's job; whether a
/// reader honours the row it loads; list-valued rows (layer vocabularies, words).
/// It needs KorStandards. An unset connection or an unreachable database FAILS this test; a gate
/// that passes by not running is the fault it exists to catch (see LiveProjects).
/// </remarks>
[Trait("Speed", "Slow")]
public sealed class CompiledDefaultsAreTheBankedRowsTests
{
    /// <summary>Keys the code reads that have no row yet, each with the reason. Empty is the goal.</summary>
    private static readonly IReadOnlyDictionary<string, string> UnbankedByDesign = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["dxf.pdf.slab-min-diagonal-mm"]     = "PDF-side threshold; no corpus measurement yet (PdfIntake.md §5 item 6)",
        ["dxf.pdf.line-min-length-mm"]       = "PDF-side threshold; no corpus measurement yet",
        ["dxf.pdf.column-max-size-mm"]       = "PDF-side size window, deliberately not the DXF row (PdfIntakeOptions remarks); no corpus measurement yet",
        ["dxf.pdf.column-min-dim-mm"]        = "PDF-side size window; no corpus measurement yet",
        ["dxf.pdf.agreement-tolerance-mm"]   = "self-check tolerance; no corpus measurement yet",
        ["dxf.pdf.agreement-label-reach-mm"] = "self-check reach; measured 2026-09-10 on twelve banked pages of five sets (PlanAgreesWithItsSchedule.DefaultLabelReachMm), a row is owed",
    };

    /// <summary>Public numeric properties on the option records that are facts, not rules, each with the reason.</summary>
    private static readonly IReadOnlyDictionary<string, string> NotARule = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["PlanClassificationOptions.ExpectedSlabCount"] = "her count for one storey of one job (slab-count.<job>.<storey>), not a portfolio rule",
        ["ComposeOptions.ModelUnitInInches"]            = "a fact about the reference model, read off its UNITS line",
        ["ComposeOptions.MembersRiseToStoreyAbove"]     = "the storey convention; a statement, not a number anyone banks",
        ["PlanClassificationOptions.SpandrelDepth"]     = "superseded by dxf.spandrel-depth-floor / -ceiling; candidate for removal",
        ["PlanClassificationOptions.UnitInInches"] = "the drawing's unit in inches, set by InUnitOf; not a rule about drawings, the thing the rules are converted by (step 74)",
        ["PlanClassificationOptions.WallFloorSlack"]    = "a tolerance on the DRAWING of a wall, not a rule about walls: a six-inch wall is drawn at 5.6-5.9 in; the PDF side's WallFloorSlackMm, triaged there (step 63)",
        ["PlanClassificationOptions.PairOpenFaces"] = "which route this is, not a rule about drawings: the Revit route pairs wall faces across open chains, the PDF route's reader already did with the fill in hand (step 75)",
        ["PlanClassificationOptions.MaxOpenFacePairThickness"] = "a cap on what two OPEN chains may pair into (18 in), against inventing a core wall from a corridor's two sides - a literal in the method until step 73 (the audit's finding 1); a row once the corpus has measured it in millimetre sets",
        ["PlanClassificationOptions.WallFloor"]         = "computed: dxf.min-wall-thickness less its slack; not a number anyone banks",
        ["ComposeOptions.DefaultSlabThicknessInches"]   = "report-only copy of dxf.default-slab-thickness before model-unit conversion",
        ["ComposeOptions.OffsetX"]                      = "this run's translation onto the reference model's grid",
        ["ComposeOptions.OffsetY"]                      = "this run's translation onto the reference model's grid",
        ["ComposeOptions.StickFileSlabThicknessAttempted"] = "whether this run was given a stick file; state, not a rule",
        ["ComposeOptions.InferMissingFloors"]           = "the --infer-floors switch; a judgement the caller opts into per run",
    };

    private static IReadOnlyDictionary<string, RuleSetting> Rows()
    {
        string? conn = Environment.GetEnvironmentVariable(RuleSettings.ConnectionEnvironmentVariable);
        Assert.False(string.IsNullOrWhiteSpace(conn),
            $"{RuleSettings.ConnectionEnvironmentVariable} is not set. This gate compares the code with KorStandards and never skips.");
        var rows = RuleSettings.Load(conn);
        Assert.True(rows.Count > 0, "KorStandards returned no rule settings: unreachable, or the view is empty.");
        return rows;
    }

    [Fact]
    public void EveryCompiledDefaultEqualsItsRow()
    {
        var rows = Rows();
        var compiled = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in DxfToEtabsService.BuiltInRuleValues(new PlanClassificationOptions(), new ComposeOptions())) compiled[k] = v;
        foreach (var (k, v) in PdfIntakeOptions.BuiltInRuleValues())
        {
            // a key both sides read must carry the same compiled value on both sides
            if (compiled.TryGetValue(k, out double dxfSide))
                Assert.True(Math.Abs(dxfSide - v) < 1e-6, $"{k}: the DXF side compiles {dxfSide} and the PDF side {v}");
            compiled[k] = v;
        }

        var wrong = new StringBuilder();
        var unbankedNotDeclared = new List<string>();
        foreach (var (key, value) in compiled.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!rows.TryGetValue(key, out var row))
            {
                if (!UnbankedByDesign.ContainsKey(key)) unbankedNotDeclared.Add(key);
                continue;
            }
            if (!row.IsNumeric) { wrong.AppendLine($"  {key}: row is not numeric ('{row.Text}'), compiled {value}"); continue; }
            if (Math.Abs(row.Value - value) > 1e-6)
                wrong.AppendLine($"  {key}: row {row.Value} {row.Units}, compiled {value}");
        }

        Assert.True(unbankedNotDeclared.Count == 0,
            "Keys the code reads with no row and no declared reason: " + string.Join(", ", unbankedNotDeclared));
        Assert.True(wrong.Length == 0, "Compiled defaults that are not their rows:\n" + wrong);

        // and the declared-unbanked list must not go stale: a key that gained a row leaves the list
        var banked = UnbankedByDesign.Keys.Where(rows.ContainsKey).ToList();
        Assert.True(banked.Count == 0, "Declared unbanked but a row exists now — remove from UnbankedByDesign: " + string.Join(", ", banked));
    }

    /// <summary>
    /// A list-valued row that differs from its compiled default, each with the reason it differs. Empty is not
    /// the goal here — the row is the authority — but a DIFFERENCE NOBODY DECLARED is a fault, because it means
    /// the code and the office disagree about words and no one decided that.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ListRowsThatDifferByDesign = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        // ⚠ THE THREE THIS GATE FOUND ON ITS FIRST RUN, 2026-09-23. All three are on PlanClassificationOptions,
        // where the row is RICHER than the code, so a production run reads more than a default-mode run — the
        // same split the numeric gate was written for on 2026-09-08, in the layer vocabularies instead of the
        // numbers. Declared, not silenced: bringing the compiled defaults up to the rows changes what every
        // default-mode run reads (tests, the WPF window, the intake's instruments), so it is a change that wants
        // its own gate run and not a quiet edit.
        //
        // NONE of the sixteen DrawingVocabulary rows differs, which is the honest limit of the record-copy fault
        // mended the same day: it could only bite where a row differs from its default, and today none of the
        // WORD rows does. It would have bitten the moment migration 099 added FLR to dxf.floor-nouns.
        ["dxf.column-layer-patterns"] = "row [_COL; -COL; S-COL], code [_COL]: the row carries two more layer spellings than the compiled default",
        ["dxf.slab-layer-patterns"] = "row [SLABEDG; A-FLOR; S-FLOR], code [SLABEDG]: the same, for slabs",
        ["dxf.non-structural-sheet-patterns"] = "row carries sixteen patterns (REINFORC, KEY PLAN, DESIGN LOAD, SITE PLAN, LOADING DIAGRAM …), code carries NONE: a default-mode run stands no sheet down at all",
    };

    /// <summary>Keys read as a list that have no row yet, each with the reason.</summary>
    private static readonly IReadOnlyDictionary<string, string> ListRowsUnbankedByDesign = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        // Found by this gate on its first run, 2026-09-23. No migration in KOR.Drafter/db mentions the key, and
        // DxfToEtabsService.RequiredRuleKeys does not name it either, so a run without the row does not stop: the
        // reader falls back to the one compiled word, MATCH. It is a rule the office should own — what a match
        // line is drawn on — and until it is banked this is where that is written down.
        ["dxf.match-line-layer-patterns"] = "no migration banks it and no run requires it; the compiled default is the single word MATCH (MatchLineSheetJoin.DefaultLayerPatterns), a row is owed",
    };

    /// <summary>
    /// THE WORDS AND THE LAYER PATTERNS, AGAINST THEIR COMPILED DEFAULTS (2026-09-23).
    ///
    /// The numeric gate above says in its own remarks that it does NOT cover "list-valued rows (layer
    /// vocabularies, words)" — nineteen of them — and nothing else did either. They matter as much as the
    /// numbers: dxf.level-words decides what a storey is called, dxf.slab-layer-patterns decides what a slab is
    /// drawn on, dxf.non-structural-sheet-patterns decides which sheets stand down. A row that quietly differs
    /// from the code means a production run and every default-mode run — tests, the WPF window, the intake's
    /// instruments — read different words, which is the fault the numeric gate was written for in the first place.
    ///
    /// Written the day the record-copy fault was found (a `with` over a used vocabulary kept the old patterns),
    /// because that fault could only bite where a row differs from its default, and NOTHING COULD SAY WHETHER ONE
    /// DID.
    ///
    /// WHAT THIS COVERS: every key the code reads through `ListOr`, by the words it holds, in order-insensitive
    /// comparison, case-insensitively. WHAT IT DOES NOT: whether a row is RIGHT (the corpus measurement's job);
    /// whether a reader honours the row it loads; and a word the code never reads through ListOr at all.
    /// </summary>
    [Fact]
    public void EveryCompiledWordListEqualsItsRow()
    {
        var rows = Rows();
        var compiled = DxfToEtabsService.BuiltInRuleLists();

        var wrong = new StringBuilder();
        var unbankedNotDeclared = new List<string>();
        foreach (var (key, words) in compiled.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!rows.TryGetValue(key, out var row))
            {
                if (!ListRowsUnbankedByDesign.ContainsKey(key)) unbankedNotDeclared.Add(key);
                continue;
            }
            var banked = row.Text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (banked.OrderBy(w => w, StringComparer.OrdinalIgnoreCase).SequenceEqual(words.OrderBy(w => w, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase)) continue;
            if (ListRowsThatDifferByDesign.ContainsKey(key)) continue;
            wrong.AppendLine($"  {key}: row [{string.Join("; ", banked)}], compiled [{string.Join("; ", words)}]");
        }

        Assert.True(unbankedNotDeclared.Count == 0,
            "List rules the code reads with no row and no declared reason: " + string.Join(", ", unbankedNotDeclared));
        Assert.True(wrong.Length == 0,
            "Compiled word lists that are not their rows. Either bank the row or declare the difference in ListRowsThatDifferByDesign with the reason:\n" + wrong);

        var banked2 = ListRowsUnbankedByDesign.Keys.Where(rows.ContainsKey).ToList();
        Assert.True(banked2.Count == 0, "Declared unbanked but a row exists now — remove from ListRowsUnbankedByDesign: " + string.Join(", ", banked2));
    }

    /// <summary>
    /// ⚠ A BROAD NAME ON A NARROW CHECK IS WORSE THAN NO CHECK. The gate above only sees the keys
    /// <see cref="DxfToEtabsService.BuiltInRuleLists"/> names, so a list rule added without a line there would
    /// read its row in production and be compared to nothing — while the gate's name says every word list is
    /// covered. This reads the source for every key passed to <c>settings.ListOr</c> and holds the two together.
    /// </summary>
    [Fact]
    public void EveryListRuleTheCodeReadsIsInThatList()
    {
        string core = Path.Combine(RepositoryRoot(), "Kor.Operations.EngineeringTools.Core");
        Assert.True(Directory.Exists(core), $"the Core project is not where this test looked: {core}");
        var inSource = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(core, "*.cs", SearchOption.AllDirectories))
            foreach (Match m in Regex.Matches(File.ReadAllText(file), "ListOr\\(\"([^\"]+)\""))
                inSource.Add(m.Groups[1].Value);

        Assert.True(inSource.Count > 0, "no ListOr call was found in the Core project; this guard is reading the wrong place");
        var missing = inSource.Except(DxfToEtabsService.BuiltInRuleLists().Keys, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.True(missing.Count == 0,
            "List rules the code reads that BuiltInRuleLists does not name, so nothing compares them with their rows: " + string.Join(", ", missing));
        var stale = DxfToEtabsService.BuiltInRuleLists().Keys.Except(inSource, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.True(stale.Count == 0, "BuiltInRuleLists names keys the code no longer reads as a list: " + string.Join(", ", stale));
    }

    /// <summary>The repository root, from the test binary's own folder.</summary>
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Kor.Operations.EngineeringTools.Core"))) dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }

    [Fact]
    public void EveryNumericOptionIsARuleOrDeclaredNotOne()
    {
        var keys = new HashSet<string>(
            DxfToEtabsService.BuiltInRuleValues(new PlanClassificationOptions(), new ComposeOptions()).Keys,
            StringComparer.OrdinalIgnoreCase);

        var orphans = new List<string>();
        foreach (var type in new[] { typeof(PlanClassificationOptions), typeof(ComposeOptions) })
        {
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
                // every numeric kind an option could be declared as; a new decimal or long must not escape (audit F11)
                if (t != typeof(double) && t != typeof(int) && t != typeof(bool) && t != typeof(long) && t != typeof(decimal) && t != typeof(float)) continue;
                string qualified = $"{type.Name}.{p.Name}";
                if (NotARule.ContainsKey(qualified)) continue;
                if (!keys.Contains("dxf." + Kebab(p.Name))) orphans.Add(qualified);
            }
        }
        Assert.True(orphans.Count == 0,
            "Compiled numbers that are neither a banked rule nor declared NotARule: " + string.Join(", ", orphans));
    }

    private static string Kebab(string pascal)
        => Regex.Replace(pascal, "(?<=[a-z0-9])(?=[A-Z])", "-").ToLowerInvariant();
}

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
        ["dxf.pdf.agreement-label-reach-mm"] = "self-check reach; no corpus measurement yet",
    };

    /// <summary>Public numeric properties on the option records that are facts, not rules, each with the reason.</summary>
    private static readonly IReadOnlyDictionary<string, string> NotARule = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["PlanClassificationOptions.ExpectedSlabCount"] = "her count for one storey of one job (slab-count.<job>.<storey>), not a portfolio rule",
        ["ComposeOptions.ModelUnitInInches"]            = "a fact about the reference model, read off its UNITS line",
        ["ComposeOptions.MembersRiseToStoreyAbove"]     = "the storey convention; a statement, not a number anyone banks",
        ["PlanClassificationOptions.SpandrelDepth"]     = "superseded by dxf.spandrel-depth-floor / -ceiling; candidate for removal",
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

using System.Globalization;
using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// Two models of one building must not give two answers for the same slab.
///
/// 31168 ships as two files — the YMCA and the whole site — and they share a parkade, a ground
/// floor, a mezzanine and LEVEL 2. On 2026-08-26 they were briefly built from two different
/// drawing sets and every one of the twelve shared storeys disagreed: LEVEL P1 and P2 came out
/// 76,967 sq ft at 12 inches in one and 76,958 at 10 in the other, C-ROOF 2,015 at 9 against
/// 1,995 at 12, and the site model had no C-LEVEL 3 plate and no LEVEL 1 at all.
///
/// Nothing said so. Both files passed every publish-blocking invariant, both reports opened
/// "Questions for you: 0", and the counts each model was judged on — 106 plates against 89 —
/// made the wrong one look like the better one. An engineer would have found it by opening both.
///
/// So the check is between the artifacts rather than inside one of them. Skipped when the share
/// is unreachable, like every other test that needs it.
/// </summary>
public class ShippedModelsAgreeWithEachOtherTests
{
    private readonly ITestOutputHelper _out;

    public ShippedModelsAgreeWithEachOtherTests(ITestOutputHelper output) => _out = output;

    private const string Folder =
        @"\\Kor-fs01\Projects\Projects\03 Residential\31168-01 (YMCA Langara Vancouver)" +
        @"\02 Engineering\02 Lateral Design\01 ETABS Models";

    private static readonly Regex Point = new(@"^\s*POINT\s+""([^""]+)""\s+(-?[\d.]+)\s+(-?[\d.]+)", RegexOptions.Compiled);
    private static readonly Regex Floor = new(@"^\s*AREA\s+""(KF\d+)""\s+FLOOR\s+(\d+)\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex Assign = new(@"^\s*AREAASSIGN\s+""(KF\d+)""\s+""([^""]+)""\s+SECTION\s+""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex Prop = new(@"^\s*SHELLPROP\s+""([^""]+)"".*?SLABTHICKNESS\s+([\d.]+)", RegexOptions.Compiled);
    private static readonly Regex Quoted = new(@"""([^""]+)""", RegexOptions.Compiled);

    /// <summary>
    /// What a storey holds. Area and thickness alone could not see the fault they were written to
    /// catch: on 27 August C-ROOF carried 3 walls and 8 columns in one published model and 33 and
    /// 56 in the other, and its plate is the same 2,015 sq ft in both.
    /// </summary>
    private sealed record Storey(double AreaSqFt, string[] Thicknesses, int Walls, int Columns, int Plates);

    private static Dictionary<string, Storey>? Read(string path)
    {
        if (!File.Exists(path)) return null;

        var pts = new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal);
        var props = new Dictionary<string, double>(StringComparer.Ordinal);
        var areaOf = new Dictionary<string, double>(StringComparer.Ordinal);
        var byStorey = new Dictionary<string, (double Area, SortedSet<string> T)>(StringComparer.OrdinalIgnoreCase);
        var kindOf = new Dictionary<string, string>(StringComparer.Ordinal);
        var count = new Dictionary<string, (int W, int C, int P)>(StringComparer.OrdinalIgnoreCase);

        foreach (string raw in File.ReadLines(path))
        {
            var m = Point.Match(raw);
            if (m.Success)
            {
                pts[m.Groups[1].Value] = (
                    double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                    double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));
                continue;
            }

            m = Prop.Match(raw);
            if (m.Success)
            {
                props[m.Groups[1].Value] = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                continue;
            }

            m = Floor.Match(raw);
            if (m.Success)
            {
                var names = Quoted.Matches(m.Groups[3].Value).Select(x => x.Groups[1].Value)
                    .Take(int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)).ToList();
                double sum = 0;
                for (int i = 0; i < names.Count; i++)
                {
                    if (!pts.TryGetValue(names[i], out var a)) { sum = 0; break; }
                    if (!pts.TryGetValue(names[(i + 1) % names.Count], out var b)) { sum = 0; break; }
                    sum += a.X * b.Y - b.X * a.Y;
                }
                if (sum != 0) areaOf[m.Groups[1].Value] = Math.Abs(sum) / 2.0 / 144.0;
                continue;
            }

            var k = Regex.Match(raw.TrimStart(), @"^(?:AREA|LINE)\s+""([^""]+)""\s+(\w+)");
            if (k.Success) kindOf[k.Groups[1].Value] = k.Groups[2].Value;

            var any = Regex.Match(raw.TrimStart(), @"^(?:AREA|LINE)ASSIGN\s+""([^""]+)""\s+""([^""]+)""");
            if (any.Success && kindOf.TryGetValue(any.Groups[1].Value, out string? what))
            {
                count.TryGetValue(any.Groups[2].Value, out var n);
                count[any.Groups[2].Value] = what switch
                {
                    "PANEL" => (n.W + 1, n.C, n.P),
                    "COLUMN" => (n.W, n.C + 1, n.P),
                    "FLOOR" => (n.W, n.C, n.P + 1),
                    _ => n,
                };
            }

            m = Assign.Match(raw);
            if (m.Success && areaOf.TryGetValue(m.Groups[1].Value, out double area))
            {
                string storey = m.Groups[2].Value;
                if (!byStorey.TryGetValue(storey, out var acc))
                    byStorey[storey] = acc = (0, new SortedSet<string>(StringComparer.Ordinal));
                props.TryGetValue(m.Groups[3].Value, out double t);
                acc.T.Add(t.ToString("0.##", CultureInfo.InvariantCulture));
                byStorey[storey] = (acc.Area + area, acc.T);
            }
        }

        return byStorey.ToDictionary(
            x => x.Key,
            x =>
            {
                count.TryGetValue(x.Key, out var n);
                return new Storey(x.Value.Area, x.Value.T.ToArray(), n.W, n.C, n.P);
            },
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheTwoPublished31168ModelsAgreeOnEveryStoreyTheyShare()
    {
        var site = Read(Path.Combine(Folder, "31168-TOWERS-FROM-DRAWINGS.e2k"));
        var ymca = Read(Path.Combine(Folder, "31168-FROM-DRAWINGS.e2k"));

        if (site is null || ymca is null) { _out.WriteLine("SKIPPED: share unreachable."); return; }

        var wrong = new List<string>();

        foreach (string storey in site.Keys.Intersect(ymca.Keys, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
        {
            var a = site[storey];
            var b = ymca[storey];

            // WHICH INVARIANT APPLIES DEPENDS ON WHOSE STOREY IT IS.
            //
            // This asked for equality on every shared storey, which was right while the
            // one-building model kept everything standing on a shared floor. It is not right now:
            // the drawings split every shared level by building — BLDG C and WEST at level 1,
            // level 2, the mezzanine and all three parkade levels — so building C's model holds
            // C's share of them and the site model holds all three buildings'. Demanding equality
            // there demands the YMCA model carry the towers' structure.
            //
            // On a storey named for building C, equality still holds and must: the two files are
            // cut from ONE composition, so C-ROOF cannot be 3 walls and 8 columns in one and 33
            // and 56 in the other. It was, for most of 27 August, and this test could not see it —
            // it compared floor AREA, and C-ROOF's plate is the same 2,015 sq ft either way.
            bool exclusiveToThisBuilding = E2kDocument.BuildingTagOf(storey).Length > 0;

            double drift = Math.Abs(a.AreaSqFt - b.AreaSqFt) / Math.Max(a.AreaSqFt, b.AreaSqFt);
            bool sameThickness = a.Thicknesses.SequenceEqual(b.Thicknesses, StringComparer.Ordinal);

            _out.WriteLine($"{storey,-16}{a.AreaSqFt,10:N0} sf [{string.Join("/", a.Thicknesses)}]" +
                           $"   {b.AreaSqFt,10:N0} sf [{string.Join("/", b.Thicknesses)}]" +
                           $"   {a.Walls}/{a.Columns}/{a.Plates} vs {b.Walls}/{b.Columns}/{b.Plates}" +
                           (exclusiveToThisBuilding ? "   [must be identical]" : "   [subset]"));

            if (exclusiveToThisBuilding)
            {
                if (drift >= 0.02 || !sameThickness)
                    wrong.Add($"{storey}: site {a.AreaSqFt:N0} sq ft [{string.Join("/", a.Thicknesses)}\"] " +
                              $"vs YMCA {b.AreaSqFt:N0} sq ft [{string.Join("/", b.Thicknesses)}\"]");

                if (a.Walls != b.Walls || a.Columns != b.Columns || a.Plates != b.Plates)
                    wrong.Add($"{storey} belongs to one building and the two files disagree about what stands " +
                              $"on it: site {a.Walls} wall(s), {a.Columns} column(s), {a.Plates} plate(s) " +
                              $"vs YMCA {b.Walls}, {b.Columns}, {b.Plates}");

                continue;
            }

            // Shared: the building model is a subset, never a superset. More in the smaller file
            // than the larger one means they were not cut from one composition.
            if (b.Walls > a.Walls || b.Columns > a.Columns || b.Plates > a.Plates
                || b.AreaSqFt > a.AreaSqFt * 1.02)
                wrong.Add($"{storey} is shared, so the YMCA model must hold a SUBSET of the site model — " +
                          $"it holds more: site {a.Walls}/{a.Columns}/{a.Plates} at {a.AreaSqFt:N0} sq ft " +
                          $"vs YMCA {b.Walls}/{b.Columns}/{b.Plates} at {b.AreaSqFt:N0} sq ft");
        }

        Assert.True(wrong.Count == 0,
            "The two published models of 31168 disagree about storeys they both contain:\n  " +
            string.Join("\n  ", wrong) +
            "\n\nThey share a parkade, a ground floor and a mezzanine, and an engineer opening both " +
            "finds two answers for one slab. Publish both from the same drawing set, or explain the " +
            "difference in the report before either ships.");
    }
}

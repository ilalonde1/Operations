using System.Globalization;
using System.Text.RegularExpressions;
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

    private sealed record Storey(double AreaSqFt, string[] Thicknesses);

    private static Dictionary<string, Storey>? Read(string path)
    {
        if (!File.Exists(path)) return null;

        var pts = new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal);
        var props = new Dictionary<string, double>(StringComparer.Ordinal);
        var areaOf = new Dictionary<string, double>(StringComparer.Ordinal);
        var byStorey = new Dictionary<string, (double Area, SortedSet<string> T)>(StringComparer.OrdinalIgnoreCase);

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

        return byStorey.ToDictionary(x => x.Key, x => new Storey(x.Value.Area, x.Value.T.ToArray()),
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

            // Area within a fiftieth: the two runs read the same linework and round the same way,
            // so a real agreement is exact to within rounding, not merely close.
            double drift = Math.Abs(a.AreaSqFt - b.AreaSqFt) / Math.Max(a.AreaSqFt, b.AreaSqFt);
            bool sameThickness = a.Thicknesses.SequenceEqual(b.Thicknesses, StringComparer.Ordinal);

            _out.WriteLine($"{storey,-16}{a.AreaSqFt,10:N0} sf [{string.Join("/", a.Thicknesses)}]" +
                           $"   {b.AreaSqFt,10:N0} sf [{string.Join("/", b.Thicknesses)}]");

            if (drift >= 0.02 || !sameThickness)
                wrong.Add($"{storey}: site {a.AreaSqFt:N0} sq ft [{string.Join("/", a.Thicknesses)}\"] " +
                          $"vs YMCA {b.AreaSqFt:N0} sq ft [{string.Join("/", b.Thicknesses)}\"]");
        }

        Assert.True(wrong.Count == 0,
            "The two published models of 31168 disagree about storeys they both contain:\n  " +
            string.Join("\n  ", wrong) +
            "\n\nThey share a parkade, a ground floor and a mezzanine, and an engineer opening both " +
            "finds two answers for one slab. Publish both from the same drawing set, or explain the " +
            "difference in the report before either ships.");
    }
}

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

        AssertTheyAgree(site, ymca, "published");
    }

    /// <summary>
    /// ⚠ WHY THIS IS SPLIT OUT FROM THE TEST ABOVE.
    ///
    /// That test can only read what has already SHIPPED, so by construction it cannot fail until
    /// after a publish. Every cut-versus-site fault this year was therefore found on the share,
    /// with the file already sitting where the engineer opens it — the span reset that gave one
    /// building's walls two heights was caught that way, after landing.
    ///
    /// <see cref="TheModelsThisCodeBuildsNowAgreeBeforeAnyOfItShips"/> runs this identical
    /// comparison on models built in the test, so the class is caught in the ordinary loop.
    /// </summary>
    private void AssertTheyAgree(
        Dictionary<string, Storey> site, Dictionary<string, Storey> ymca, string which)
    {
        var wrong = new List<string>();

        foreach (string storey in site.Keys.Intersect(ymca.Keys, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
        {
            var a = site[storey];
            var b = ymca[storey];

            // ⚠ THESE TWO TESTS FAIL ON THE PUBLISHED PAIR, AND THE MODELS ARE NOT THE FAULT.
            //
            // C-LEVEL 3 sits 5.5 in above LEVEL 3 — under dxf.storeys-at-one-level-gap of 12 in, so
            // they are one physical level drawn twice, once for building C and once for the towers.
            // The site file holds both. The cut drops LEVEL 3, keeps the members standing inside
            // building C's footprint by re-homing them onto C-LEVEL 3, and cuts the towers' away —
            // which is why "LEVEL 3" appears zero times in that file.
            //
            // So a name-to-name comparison of C-LEVEL 3 measures C's share in one file against C's
            // share PLUS what was re-homed in the other, and reports three storeys of a correct pair
            // as wrong. Folding the whole twin in was tried and is worse: LEVEL 3 carries the
            // TOWERS' 40-odd walls, and adding those made the site side 60 against 24.
            //
            // The comparison needs to know which members were re-homed, which is the cut's business
            // and is not recoverable from the finished files. Until it does, these two are expected
            // red and must not be read as a model fault. Every other check in this class passes,
            // including TheModelsThisCodeBuildsNowAgreeBeforeAnyOfItShips, which builds both models
            // and compares them before anything ships.

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
            $"The two {which} models of 31168 disagree about storeys they both contain:\n  " +
            string.Join("\n  ", wrong) +
            "\n\nThey share a parkade, a ground floor and a mezzanine, and an engineer opening both " +
            "finds two answers for one slab. Publish both from the same drawing set, or explain the " +
            "difference in the report before either ships.");
    }

    /// <summary>
    /// The pair THIS CODE BUILDS must agree, before any of it reaches a job folder.
    /// </summary>
    /// <remarks>
    /// Builds the site model and the building-C cut the way the publisher does — same reference,
    /// same drawings, the drop list derived by <see cref="PublishPlan.ForBuildings"/> — and runs
    /// the same comparison the published pair gets.
    ///
    /// WHAT IT COVERS: every storey named for one building must be identical in the two files, and
    /// every shared storey must be a subset. That is the whole cut-versus-site class.
    ///
    /// WHAT IT DOES NOT: the suite's drawing folder is not the one the publisher discovers, so
    /// this proves the INVARIANT holds for this code, not that a given shipped pair was built from
    /// one set. The published test above is still the one that checks that. It also compares
    /// counts, areas, thicknesses and concrete — not spans, sections or materials.
    /// </remarks>
    [Trait("Speed", "Slow")]
    [Fact]
    public void TheModelsThisCodeBuildsNowAgreeBeforeAnyOfItShips()
    {
        // THE INPUTS THE PUBLISHER DISCOVERS, not the suite's own folder.
        //
        // Written against GeneratedModel first, this built from _DXF-plans-for-rebuild while every
        // shipped 31168 model comes from _DXF-from-Revit-2026-08-26. It failed on a disagreement in
        // a drawing set nothing ships, which is a false alarm about the pair that does. A gate on
        // what ships has to read what ships.
        PublishDiscoveryResult discovery;
        try
        {
            discovery = PublishDiscovery.Discover(
                new PublishDiscoveryRequest("31168", null, null, "31168-reference.e2k"));
        }
        catch (Exception ex)
        {
            // ⚠ A GATE THAT PASSES BY NOT RUNNING IS THE FAULT IT EXISTS TO CATCH. This skipped
            // silently on "Projects root not found ''" — a null root, not an unreachable share —
            // and reported green in 2 ms. Only an unreachable share may skip; anything else fails.
            if (!Directory.Exists(PublishDiscovery.DefaultProjectsRoot))
            {
                _out.WriteLine($"SKIPPED: share unreachable ({ex.Message}).");
                return;
            }

            throw;
        }

        string reference = Path.Combine(discovery.ModelFolder, discovery.Reference);
        if (!Directory.Exists(discovery.DxfFolder) || !File.Exists(reference))
        {
            _out.WriteLine("SKIPPED: share unreachable.");
            return;
        }

        string dxf = DrawingCache.Local(discovery.DxfFolder);
        var storeys = E2kDocument.Load(reference).ReadStories().Select(s => s.Name).ToList();
        var derived = PublishPlan.ForBuildings(storeys, JobPublisher.ReachByStorey(dxf, storeys));
        var cut = JobPublisher.ChoosePlans(derived, tower: "C", variant: null, perBuilding: false).Single();

        Dictionary<string, Storey>? Build(string? tower, IReadOnlyList<string> drop)
        {
            string output = Path.Combine(Path.GetTempPath(), $"kor-agree-{Guid.NewGuid():N}.e2k");
            try
            {
                DxfToEtabsService.Run(new DxfToEtabsRequest
                {
                    RequireRuleSettings = true,
                    DxfFolder = dxf,
                    ReferenceE2k = reference,
                    OutputE2k = output,
                    TowerOnly = tower,
                    DropStoreys = drop.ToList(),
                });
                return Read(output);
            }
            finally
            {
                if (File.Exists(output)) File.Delete(output);
            }
        }

        var site = Build(null, Array.Empty<string>());
        var ymca = Build(cut.Tower, cut.DropStoreys);
        if (site is null || ymca is null) { _out.WriteLine("SKIPPED: a model would not build."); return; }

        AssertTheyAgree(site, ymca, "just-built");
    }

    /// <summary>
    /// And they must not give two prices for one building either.
    ///
    /// The takeoff restates the model, so a quantity that differs between the two files is a
    /// geometry difference the storey comparison above did not catch — and it arrives at the
    /// estimator as money. Building C's own storeys are identical in both models by construction
    /// (the one-building file is the site file, cut), so every yard of its concrete must match.
    /// </summary>
    [Fact]
    public void TheTwoPublished31168ModelsPriceBuildingCTheSame()
    {
        string sitePath = Path.Combine(Folder, "31168-TOWERS-FROM-DRAWINGS.e2k");
        string ymcaPath = Path.Combine(Folder, "31168-FROM-DRAWINGS.e2k");
        if (!File.Exists(sitePath) || !File.Exists(ymcaPath)) { _out.WriteLine("SKIPPED: share unreachable."); return; }

        static Dictionary<string, double> ByStoreyElement(string path) =>
            QuantityTakeoff.E2kQuantityTakeoff.Read(E2kDocument.Load(path)).Inputs
                .Where(i => i.Level.StartsWith("C-", StringComparison.OrdinalIgnoreCase))
                .GroupBy(i => $"{i.Level}|{i.Element}")
                .ToDictionary(g => g.Key, g => g.Sum(x => x.ConcreteVolume), StringComparer.OrdinalIgnoreCase);

        var site = ByStoreyElement(sitePath);
        var ymca = ByStoreyElement(ymcaPath);

        var priced = new List<string>();
        foreach (string key in site.Keys.Union(ymca.Keys, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            double a = site.GetValueOrDefault(key), b = ymca.GetValueOrDefault(key);
            if (Math.Abs(a - b) > 0.05)
                priced.Add($"{key}: site {a:N1} yd³ vs one-building {b:N1} yd³");
        }

        // A comparison of nothing is not agreement. Without this the test passes when both readers
        // produce no C-prefixed rows at all — the exact failure it exists to catch.
        int compared = site.Keys.Intersect(ymca.Keys, StringComparer.OrdinalIgnoreCase).Count();
        Assert.True(compared >= 16,
            $"only {compared} building-C storey/element rows were comparable between the two published models. " +
            "Building C has eight storeys carrying slabs, walls and columns, so this is a broken read, not agreement.");

        _out.WriteLine($"Building C priced identically across {compared} storey/element rows.");

        Assert.True(priced.Count == 0,
            "The two published models of 31168 put a different quantity of concrete in building C:\n  " +
            string.Join("\n  ", priced) +
            "\n\nThe one-building model is the site model cut, so its own building cannot cost a " +
            "different amount in the two files. A difference here is a geometry difference that " +
            "reaches the estimator as money.");
    }
}

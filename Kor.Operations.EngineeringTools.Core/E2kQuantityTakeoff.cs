#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.Dxf;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>Something the model carried that this takeoff would not price — surfaced by name,
    /// never silently dropped, so the estimator can see the hole rather than inherit it.</summary>
    public sealed record E2kResidual(string Kind, string Storey, string Object, string Note);

    /// <summary>An inference the numbers rest on. Doctrine gate 3: anything not read off the model
    /// prints a flag naming its evidence, even when the number is right.</summary>
    public sealed record E2kTakeoffFlag(string Code, string Note);

    /// <summary>Outcome of pricing a generated ETABS model: the rows
    /// <see cref="StructuralTakeoffService"/> prices, what was left out, and what was assumed.</summary>
    public sealed record E2kTakeoffResult(
        IReadOnlyList<StructuralTakeoffInput> Inputs,
        IReadOnlyList<E2kResidual> Residual,
        IReadOnlyList<E2kTakeoffFlag> Flags,
        int MembersRead,
        double OpeningAreaDeducted,
        UnitSystem Unit,
        string UnitNote);

    /// <summary>
    /// Turns a finished ETABS model into concrete quantities. The model was already built from the
    /// drawings — every slab outline, wall thickness and column section in it was read, placed on a
    /// storey and checked against the shipped-model invariants. This measures nothing new; it states
    /// what is already there in the units an estimator prices, so the takeoff and the engineer's
    /// model can never disagree about the building.
    ///
    /// It reads the SHIPPED <c>.e2k</c>, not the geometry that produced it, so the quantities
    /// describe the file the engineer opens.
    ///
    /// Volumes, by the convention every structural set obeys:
    ///   slab   = enclosed plan area, less the openings cut in it, x its SLABTHICKNESS
    ///   wall   = panel length x the storey it rises through x its WALLTHICKNESS
    ///   column = its FRAMESECTION area x the storey it rises through
    /// Formwork is the surface concrete is poured against: a slab's soffit and edge, a wall's two
    /// faces, a column's perimeter.
    ///
    /// HONEST SCOPE. A storey's HEIGHT is its rise, so a wall or column is priced full-height
    /// between floors -- flagged, because a drawing may stop it short. Shear and non-shear walls are
    /// NOT split: every wall in a generated model carries a pier label because the generator assigns
    /// one, so a pier is our own output and not evidence, and calling them all shear would inflate
    /// wall steel by three quarters. Slab variants are taken only where the model's own storey
    /// vocabulary states one (roof, parkade); occupancy is not in the model and is left to the
    /// estimator, named. Anything below the lowest modelled storey -- footings, piles, grade beams --
    /// is not in an ETABS superstructure model and is reported as a residual, not guessed at.
    /// </summary>
    public static class E2kQuantityTakeoff
    {
        private const double CubicInchesPerCubicYard = 46656.0;   // 36^3
        private const double CubicInchesPerCubicMetre = 61023.744094732284;
        private const double SquareInchesPerSquareFoot = 144.0;
        private const double SquareInchesPerSquareMetre = 1550.0031000062;

        /// <param name="doc">A finished model, as shipped.</param>
        /// <param name="unit">Units the rows are expressed in; must match the density table used.</param>
        /// <param name="roofWords">Storey words meaning a roof — <c>dxf.roof-words</c> from the rules DB.</param>
        /// <param name="parkadeWords">Storey words meaning parking — <c>dxf.parkade-words</c>.</param>
        public static E2kTakeoffResult Read(
            E2kDocument doc,
            UnitSystem unit = UnitSystem.Imperial,
            IReadOnlyCollection<string>? roofWords = null,
            IReadOnlyCollection<string>? parkadeWords = null)
        {
            ArgumentNullException.ThrowIfNull(doc);

            var residual = new List<E2kResidual>();
            var flags = new List<E2kTakeoffFlag>();

            // ---- units -------------------------------------------------------------------------
            double? inchesPerUnit = doc.LengthUnitInInches();
            if (inchesPerUnit is null or <= 0)
            {
                inchesPerUnit = 1.0;
                flags.Add(new E2kTakeoffFlag("UNIT_ASSUMED",
                    "the model declares no length unit in $ CONTROLS; inches assumed. If it is not in inches every quantity is wrong by the cube of the ratio."));
            }
            double u = inchesPerUnit.Value;                       // model length unit -> inches
            double volDiv = unit == UnitSystem.Imperial ? CubicInchesPerCubicYard : CubicInchesPerCubicMetre;
            double areaDiv = unit == UnitSystem.Imperial ? SquareInchesPerSquareFoot : SquareInchesPerSquareMetre;
            string unitNote = unit == UnitSystem.Imperial
                ? "concrete in cubic yards, formwork in square feet"
                : "concrete in cubic metres, formwork in square metres";

            // ---- what the model declares --------------------------------------------------------
            var rise = RiseByStorey(doc);                            // storey -> rise, model units
            var shells = ShellProperties(doc);                     // section -> (kind, thickness, material)
            var frames = FrameSections(doc);                       // section -> (area, perimeter, material) in model units
            var areaKind = ConnectivityKind(doc, "AREA CONNECTIVITIES");   // object -> FLOOR | PANEL | AREA
            var lineKind = ConnectivityKind(doc, "LINE CONNECTIVITIES");   // object -> COLUMN | BEAM | BRACE
            var plan = doc.PlanPointsOfObjects();
            var areaAssign = AreaAssigns(doc);
            var lineAssign = LineAssigns(doc);

            // ---- openings, so a hole is never priced as concrete --------------------------------
            // An opening is an area object with OPENING "Yes" and no section. It is deducted from
            // the floor on its own storey that encloses its centre.
            var openings = new List<(string Name, string Storey, double Area, DxfPoint Centre)>();
            foreach (var (name, a) in areaAssign)
            {
                if (!a.IsOpening) continue;
                if (!plan.TryGetValue(name, out var pts) || pts.Count < 3)
                {
                    residual.Add(new E2kResidual("opening", a.Storey, name,
                        "an opening with no plan outline in the model; nothing was deducted for it."));
                    continue;
                }
                var ring = Ring(pts);
                openings.Add((name, a.Storey, PolygonArea(ring), Centroid(ring)));
            }

            // ---- each hole gets ONE parent plate, decided before anything is priced --------------
            //
            // Deducting from every plate whose outline happens to contain the hole's centre double
            // counts it where two floors overlap on a storey. The parent is the SMALLEST plate on
            // the same storey that contains the hole's centre and is bigger than the hole — smallest
            // because a hole in a small infill slab that sits over a larger one belongs to the
            // infill. A hole that resolves to nothing is reported, never spread around.
            var platesOnStorey = areaAssign
                .Where(x => !x.Value.IsOpening
                            && areaKind.TryGetValue(x.Key, out var pk)
                            && pk.Equals("FLOOR", StringComparison.OrdinalIgnoreCase)
                            && plan.ContainsKey(x.Key))
                .Select(x => (Name: x.Key, x.Value.Storey, Ring: Ring(plan[x.Key])))
                .Where(x => x.Ring.Count >= 3)
                .Select(x => (x.Name, x.Storey, x.Ring, Area: PolygonArea(x.Ring)))
                .ToList();

            var parentOf = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var o in openings)
            {
                var parent = platesOnStorey
                    .Where(p => string.Equals(p.Storey, o.Storey, StringComparison.OrdinalIgnoreCase)
                                && p.Area > o.Area
                                && LoopGeometry.PointInPolygon(o.Centre, p.Ring))
                    .OrderBy(p => p.Area)
                    .Select(p => p.Name)
                    .FirstOrDefault();

                if (parent is not null) parentOf[o.Name] = parent;
            }

            var inputs = new List<StructuralTakeoffInput>();
            double openingDeducted = 0;
            int read = 0;
            var slabStoreysDefaulted = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            bool anyWall = false, anyHeightFromStorey = false;

            // ---- areas: floors and wall panels ---------------------------------------------------
            foreach (var (name, a) in areaAssign)
            {
                if (a.IsOpening) continue;

                string kind = areaKind.TryGetValue(name, out var k) ? k : "";
                if (a.Section is null)
                {
                    residual.Add(new E2kResidual(kind.Length > 0 ? kind.ToLowerInvariant() : "area", a.Storey, name,
                        "the object carries no SECTION, so it has no thickness and cannot be priced."));
                    continue;
                }
                if (!shells.TryGetValue(a.Section, out var prop))
                {
                    residual.Add(new E2kResidual(kind.Length > 0 ? kind.ToLowerInvariant() : "area", a.Storey, name,
                        $"section \"{a.Section}\" is not declared in $ SLAB PROPERTIES or $ WALL PROPERTIES."));
                    continue;
                }
                if (!plan.TryGetValue(name, out var pts) || pts.Count < 2)
                {
                    residual.Add(new E2kResidual(kind.Length > 0 ? kind.ToLowerInvariant() : "area", a.Storey, name,
                        "the object has no plan outline in the model."));
                    continue;
                }

                var ring = Ring(pts);

                // A SECTION DIMENSION IS IN THE MODEL'S OWN UNITS, LIKE EVERY OTHER LENGTH IN THE FILE.
                //
                // An .e2k is a dump in whatever units the model is set to: our own say
                // UNITS "KIP" "IN" and write SLABTHICKNESS 8 for eight inches, but a metric model
                // says "MM" and writes SLABTHICKNESS 300 for the same idea. Reading that 300 as
                // inches while correctly scaling the plan coordinates priced a 10 m x 10 m x 300 mm
                // slab at about 762 m³ instead of 30 — twenty-five times over, with nothing saying so.
                double thkIn = prop.Thickness * u;

                if (string.Equals(kind, "FLOOR", StringComparison.OrdinalIgnoreCase))
                {
                    if (ring.Count < 3)
                    {
                        residual.Add(new E2kResidual("floor", a.Storey, name, "fewer than three plan points; no area to price."));
                        continue;
                    }

                    double grossU2 = PolygonArea(ring);

                    // A HOLE BELONGS TO ONE PLATE. It is deducted here only if this plate is the
                    // one it was resolved to, decided once for the whole model below — two floors
                    // overlapping on a storey would otherwise each deduct the same hole.
                    double holesU2 = openings
                        .Where(o => parentOf.TryGetValue(o.Name, out string? p) && p == name)
                        .Sum(o => o.Area);

                    double netU2 = grossU2 - holesU2;
                    if (netU2 < 0)
                    {
                        // Cannot happen once a hole must be smaller than its plate, but a clamp to
                        // zero is exactly the silent arithmetic this reader is not allowed to do.
                        residual.Add(new E2kResidual("floor", a.Storey, name,
                            $"the openings resolved to this slab come to more than the slab itself ({holesU2 * u * u / areaDiv:N0} against {grossU2 * u * u / areaDiv:N0}); it was not priced."));
                        continue;
                    }
                    openingDeducted += holesU2 * u * u / areaDiv;

                    double volume = netU2 * u * u * thkIn / volDiv;
                    double formwork = (netU2 * u * u + Perimeter(ring) * u * thkIn) / areaDiv;

                    string? variant = SlabVariant(a.Storey, roofWords, parkadeWords);
                    if (variant is null) slabStoreysDefaulted.Add(a.Storey);

                    inputs.Add(new StructuralTakeoffInput(a.Storey, TakeoffElementType.Slab, variant,
                        volume, formwork, Grade(prop.Material)));
                    read++;
                }
                else if (string.Equals(kind, "PANEL", StringComparison.OrdinalIgnoreCase))
                {
                    // A wall panel's plan trace is its base line, repeated for the top edge — usually
                    // two points, but a panel folded round a corner has three or more, and the
                    // distance from the first to the last is then the diagonal across the corner
                    // rather than the wall. Walk the line instead. (Both published 31168 models are
                    // all two-point panels, so this changes nothing there and everything on a model
                    // that is not ours.)
                    var ends = ring.Distinct().ToList();
                    double lengthU = 0;
                    for (int i = 1; i < ends.Count; i++) lengthU += Distance(ends[i - 1], ends[i]);
                    if (lengthU <= 0)
                    {
                        residual.Add(new E2kResidual("wall", a.Storey, name, "the panel has no plan length."));
                        continue;
                    }
                    if (!rise.TryGetValue(a.Storey, out double riseU) || riseU <= 0)
                    {
                        residual.Add(new E2kResidual("wall", a.Storey, name,
                            "the storey states no HEIGHT, so the wall has no height to price."));
                        continue;
                    }
                    anyWall = true; anyHeightFromStorey = true;

                    double volume = lengthU * u * riseU * u * thkIn / volDiv;
                    double formwork = 2.0 * lengthU * u * riseU * u / areaDiv;

                    inputs.Add(new StructuralTakeoffInput(a.Storey, TakeoffElementType.Wall, null,
                        volume, formwork, Grade(prop.Material)));
                    read++;
                }
                else
                {
                    residual.Add(new E2kResidual("area", a.Storey, name,
                        $"connectivity type \"{kind}\" is neither FLOOR nor PANEL; not priced."));
                }
            }

            foreach (var o in openings.Where(o => !parentOf.ContainsKey(o.Name)))
                residual.Add(new E2kResidual("opening", o.Storey, o.Name,
                    $"this {o.Area * u * u / areaDiv:N0} opening resolved to no slab on its storey — no plate on it both contains the opening's centre and is larger than the opening. Nothing was deducted for it."));

            // ---- lines: columns -------------------------------------------------------------------
            foreach (var (name, l) in lineAssign)
            {
                string kind = lineKind.TryGetValue(name, out var k) ? k : "";
                if (!string.Equals(kind, "COLUMN", StringComparison.OrdinalIgnoreCase))
                {
                    residual.Add(new E2kResidual(kind.Length > 0 ? kind.ToLowerInvariant() : "line", l.Storey, name,
                        $"line type \"{kind}\" is not a column; beams and braces are not priced by this reader."));
                    continue;
                }
                if (l.Section is null || !frames.TryGetValue(l.Section, out var sec))
                {
                    residual.Add(new E2kResidual("column", l.Storey, name,
                        l.Section is null
                            ? "the column carries no SECTION."
                            : $"section \"{l.Section}\" is not a concrete shape declared in $ FRAME SECTIONS (a steel section carries no concrete)."));
                    continue;
                }
                if (!rise.TryGetValue(l.Storey, out double riseU) || riseU <= 0)
                {
                    residual.Add(new E2kResidual("column", l.Storey, name,
                        "the storey states no HEIGHT, so the column has no height to price."));
                    continue;
                }
                anyHeightFromStorey = true;

                // Section dimensions are in the model's units, the same as every other length here.
                double volume = sec.Area * u * u * riseU * u / volDiv;
                double formwork = sec.Perimeter * u * riseU * u / areaDiv;

                inputs.Add(new StructuralTakeoffInput(l.Storey, TakeoffElementType.Column, null,
                    volume, formwork, Grade(sec.Material)));
                read++;
            }

            // ---- what the numbers rest on ----------------------------------------------------------
            if (anyHeightFromStorey)
                flags.Add(new E2kTakeoffFlag("HEIGHT_FROM_STOREY",
                    "walls and columns are priced over the full rise from their own storey to the next floor above it. A member the drawings stop short of that floor is over-measured by the difference."));

            if (anyWall)
                flags.Add(new E2kTakeoffFlag("WALL_VARIANT_UNSPLIT",
                    "shear and non-shear walls are not distinguished, so every wall takes the default reinforcing ratio. The generator gives every wall a pier label, so a pier cannot be used as evidence of a shear wall. Split by hand where it matters."));

            if (slabStoreysDefaulted.Count > 0)
                flags.Add(new E2kTakeoffFlag("SLAB_VARIANT_DEFAULTED",
                    "no roof or parkade word matched these storeys, so their slabs take the default reinforcing ratio rather than an occupancy one: "
                    + string.Join(", ", slabStoreysDefaulted) + "."));

            var lowest = rise.Count > 0 ? doc.ReadStories().OrderBy(s => s.Elevation).FirstOrDefault()?.Name : null;
            residual.Add(new E2kResidual("foundation", lowest ?? "(base)", "—",
                "footings, pile caps, piles and grade beams are below the lowest modelled storey and are not in an ETABS superstructure model. Quantify them by hand."));

            // ---- one row per storey, element, variant and grade ------------------------------------
            var grouped = inputs
                .GroupBy(r => (r.Level, r.Element, r.Variant, r.Grade))
                .Select(g => new StructuralTakeoffInput(
                    g.Key.Level, g.Key.Element, g.Key.Variant,
                    g.Sum(x => x.ConcreteVolume), g.Sum(x => x.FormworkArea), g.Key.Grade))
                .OrderBy(r => r.Level, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Element)
                .ToList();

            return new E2kTakeoffResult(grouped, residual, flags, read, openingDeducted, unit, unitNote);
        }

        // ---- reading the model -----------------------------------------------------------------

        /// <summary>
        /// How far a member on each storey rises: to the next FLOOR above it, not to the next name
        /// in the storey list.
        ///
        /// A STORY line states HEIGHT, and on a single building that is the answer. On a site model
        /// it is not: ETABS's HEIGHT is the gap to the next storey in one GLOBAL list, and where
        /// buildings interleave — the same physical floor named once per building, a few inches
        /// apart — that gap is the few inches. In the published 31168 site model C-LEVEL 3 states
        /// HEIGHT 5.5, five and a half INCHES, because the site's LEVEL 4 sits just above it; the
        /// same storey in the one-building file states 215.5. Priced off HEIGHT, building C's walls
        /// came to 6 cubic yards in one file and 236 in the other for the same building.
        ///
        /// So the rise is measured the way the placement model already measures everything else. A
        /// member belongs to the storey it RISES TO, so its height is from the floor below up to its
        /// own storey — and "the floor below" means the nearest storey more than half a storey down.
        /// Two storeys closer than that are one floor drafted twice, and nothing stands between them.
        /// The elevations agree exactly across the two published 31168 files; only the HEIGHT field,
        /// which is a position in one global list, does not.
        /// </summary>
        public static IReadOnlyDictionary<string, double> RiseByStorey(E2kDocument doc)
        {
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            var storeys = doc.ReadStories().OrderBy(s => s.Elevation).ToList();
            if (storeys.Count == 0) return result;

            double sameFloor = doc.SameFloorTolerance();

            for (int i = 0; i < storeys.Count; i++)
            {
                // The floor this member stands on: the nearest storey genuinely below its own.
                double? below = null;
                for (int j = i - 1; j >= 0; j--)
                {
                    if (storeys[i].Elevation - storeys[j].Elevation <= sameFloor) continue;
                    below = storeys[j].Elevation;
                    break;
                }

                // The lowest storey stands on nothing in the model. Fall back to the HEIGHT it
                // states, which is the only figure there is.
                if (below is null)
                {
                    if (StatedHeight(doc, storeys[i].Name) is double h && h > 0) result[storeys[i].Name] = h;
                    continue;
                }

                result[storeys[i].Name] = storeys[i].Elevation - below.Value;
            }

            return result;
        }

        private static double? StatedHeight(E2kDocument doc, string storey)
        {
            foreach (string line in doc.LinesOf("STORIES"))
            {
                var m = Regex.Match(line.Trim(), @"^STORY\s+""([^""]+)""\s+HEIGHT\s+(-?[\d.eE+]+)", RegexOptions.IgnoreCase);
                if (m.Success
                    && string.Equals(m.Groups[1].Value, storey, StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double h))
                    return h;
            }
            return null;
        }

        private readonly record struct ShellProp(string Kind, double Thickness, string Material);

        private static Dictionary<string, ShellProp> ShellProperties(E2kDocument doc)
        {
            var result = new Dictionary<string, ShellProp>(StringComparer.Ordinal);
            foreach (string header in new[] { "SLAB PROPERTIES", "WALL PROPERTIES", "DECK PROPERTIES" })
            {
                foreach (string line in doc.LinesOf(header))
                {
                    var m = Regex.Match(line.Trim(), @"^SHELLPROP\s+""([^""]+)""", RegexOptions.IgnoreCase);
                    if (!m.Success) continue;

                    var thick = Regex.Match(line, @"(?:SLABTHICKNESS|WALLTHICKNESS|DECKSLABDEPTH)\s+(-?[\d.eE+]+)", RegexOptions.IgnoreCase);
                    if (!thick.Success || !double.TryParse(thick.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double t) || t <= 0)
                        continue;

                    var mat = Regex.Match(line, @"MATERIAL\s+""([^""]+)""", RegexOptions.IgnoreCase);
                    var type = Regex.Match(line, @"PROPTYPE\s+""([^""]+)""", RegexOptions.IgnoreCase);
                    result[m.Groups[1].Value] = new ShellProp(
                        type.Success ? type.Groups[1].Value : "", t, mat.Success ? mat.Groups[1].Value : "");
                }
            }
            return result;
        }

        private readonly record struct FrameSec(double Area, double Perimeter, string Material);

        /// <summary>Concrete frame sections only. A steel shape carries no concrete and is skipped,
        /// which surfaces it as a residual rather than pricing it as though it were poured.</summary>
        private static Dictionary<string, FrameSec> FrameSections(E2kDocument doc)
        {
            var result = new Dictionary<string, FrameSec>(StringComparer.Ordinal);
            foreach (string line in doc.LinesOf("FRAME SECTIONS"))
            {
                var m = Regex.Match(line.Trim(), @"^FRAMESECTION\s+""((?:[^""]|"""")+)""", RegexOptions.IgnoreCase);
                if (!m.Success) continue;

                var shape = Regex.Match(line, @"SHAPE\s+""([^""]+)""", RegexOptions.IgnoreCase);
                if (!shape.Success) continue;
                string s = shape.Groups[1].Value;
                if (s.IndexOf("Concrete", StringComparison.OrdinalIgnoreCase) < 0) continue;

                double d = Dim(line, "D"), b = Dim(line, "B");
                var mat = Regex.Match(line, @"MATERIAL\s+""([^""]+)""", RegexOptions.IgnoreCase);
                string material = mat.Success ? mat.Groups[1].Value : "";

                if (s.IndexOf("Circle", StringComparison.OrdinalIgnoreCase) >= 0 && d > 0)
                    result[m.Groups[1].Value] = new FrameSec(Math.PI * d * d / 4.0, Math.PI * d, material);
                else if (d > 0 && b > 0)
                    result[m.Groups[1].Value] = new FrameSec(d * b, 2 * (d + b), material);
            }
            return result;
        }

        private static double Dim(string line, string key)
        {
            var m = Regex.Match(line, @"(?<![A-Z])" + key + @"\s+(-?[\d.eE+]+)");
            return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;
        }

        /// <summary>Object -> its connectivity type: FLOOR, PANEL or AREA for shells; COLUMN, BEAM or
        /// BRACE for lines.</summary>
        private static Dictionary<string, string> ConnectivityKind(E2kDocument doc, string header)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string line in doc.LinesOf(header))
            {
                var m = Regex.Match(line.Trim(), @"^(?:AREA|LINE)\s+""([^""]+)""\s+(\w+)", RegexOptions.IgnoreCase);
                if (m.Success) result[m.Groups[1].Value] = m.Groups[2].Value;
            }
            return result;
        }

        private readonly record struct AreaAssign(string Storey, string? Section, bool IsOpening);

        /// <summary>
        /// One row per object per storey. A repeated <c>AREAASSIGN</c> for the same object on the
        /// same storey is the same physical member written twice, and pricing both pours it twice.
        /// The DXF publisher drops these (<see cref="E2kDocument.DropMembersDuplicatedOnOneFloor"/>)
        /// but this reader takes any finished model, so it cannot rely on that having run.
        /// </summary>
        private static List<KeyValuePair<string, AreaAssign>> AreaAssigns(E2kDocument doc)
        {
            var result = new List<KeyValuePair<string, AreaAssign>>();
            var seen = new HashSet<(string Object, string Storey)>();
            foreach (string line in doc.LinesOf("AREA ASSIGNS"))
            {
                var m = Regex.Match(line.Trim(), @"^AREAASSIGN\s+""([^""]+)""\s+""([^""]+)""", RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                if (!seen.Add((m.Groups[1].Value, m.Groups[2].Value.ToUpperInvariant()))) continue;
                var sec = Regex.Match(line, @"SECTION\s+""([^""]+)""", RegexOptions.IgnoreCase);
                bool opening = Regex.IsMatch(line, @"OPENING\s+""Yes""", RegexOptions.IgnoreCase);
                result.Add(new(m.Groups[1].Value,
                    new AreaAssign(m.Groups[2].Value, sec.Success ? sec.Groups[1].Value : null, opening)));
            }
            return result;
        }

        private readonly record struct LineAssign(string Storey, string? Section);

        /// <summary>One row per object per storey, for the reason given on <see cref="AreaAssigns"/>.</summary>
        private static List<KeyValuePair<string, LineAssign>> LineAssigns(E2kDocument doc)
        {
            var result = new List<KeyValuePair<string, LineAssign>>();
            var seen = new HashSet<(string Object, string Storey)>();
            foreach (string line in doc.LinesOf("LINE ASSIGNS"))
            {
                var m = Regex.Match(line.Trim(), @"^LINEASSIGN\s+""([^""]+)""\s+""([^""]+)""", RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                if (!seen.Add((m.Groups[1].Value, m.Groups[2].Value.ToUpperInvariant()))) continue;
                var sec = Regex.Match(line, @"SECTION\s+""((?:[^""]|"""")+)""", RegexOptions.IgnoreCase);
                result.Add(new(m.Groups[1].Value,
                    new LineAssign(m.Groups[2].Value, sec.Success ? sec.Groups[1].Value : null)));
            }
            return result;
        }

        // ---- conventions -----------------------------------------------------------------------

        /// <summary>The reinforcing variant the model's own storey vocabulary states. Occupancy —
        /// residential, podium — is not in the model and is deliberately not guessed.</summary>
        private static string? SlabVariant(string storey, IReadOnlyCollection<string>? roofWords, IReadOnlyCollection<string>? parkadeWords)
        {
            if (Matches(storey, roofWords ?? new[] { "ROOF" })) return "roof";
            if (Matches(storey, parkadeWords ?? new[] { "PARKADE", "PARKING", "LEVEL P" })) return "parking";
            return null;
        }

        private static bool Matches(string name, IReadOnlyCollection<string> words) =>
            words.Any(w => w.Length > 0 && name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>The strength printed in an ETABS material name — "65 MPa Walls" is 65 MPa,
        /// "4000Psi" is 4000 psi. The whole name when it states no strength.</summary>
        private static string Grade(string material)
        {
            if (string.IsNullOrWhiteSpace(material)) return "";
            var m = Regex.Match(material, @"(\d+(?:\.\d+)?)\s*(MPa|Psi|ksi)", RegexOptions.IgnoreCase);
            return m.Success ? $"{m.Groups[1].Value} {m.Groups[2].Value}" : material.Trim();
        }

        // ---- plan geometry ---------------------------------------------------------------------

        private static List<DxfPoint> Ring(IReadOnlyList<(double X, double Y)> pts)
        {
            var ring = new List<DxfPoint>(pts.Count);
            foreach (var p in pts) ring.Add(new DxfPoint(p.X, p.Y));
            return ring;
        }

        private static double PolygonArea(IReadOnlyList<DxfPoint> ring)
        {
            if (ring.Count < 3) return 0;
            double sum = 0;
            for (int i = 0; i < ring.Count; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Count];
                sum += a.X * b.Y - b.X * a.Y;
            }
            return Math.Abs(sum) / 2.0;
        }

        private static double Perimeter(IReadOnlyList<DxfPoint> ring)
        {
            double sum = 0;
            for (int i = 0; i < ring.Count; i++) sum += Distance(ring[i], ring[(i + 1) % ring.Count]);
            return sum;
        }

        private static DxfPoint Centroid(IReadOnlyList<DxfPoint> ring)
        {
            if (ring.Count == 0) return new DxfPoint(0, 0);
            double x = 0, y = 0;
            foreach (var p in ring) { x += p.X; y += p.Y; }
            return new DxfPoint(x / ring.Count, y / ring.Count);
        }

        private static double Distance(DxfPoint a, DxfPoint b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}

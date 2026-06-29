#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>An element the IFC carried but could not be quantified (no NetVolume) — surfaced, never
    /// silently dropped, so the takeoff is honest about what it left out.</summary>
    public sealed record IfcResidual(string Type, string Level, string Tag, string Note);

    /// <summary>Outcome of reading a structural IFC model: priceable concrete rows (exact model volumes),
    /// any modelled reinforcing found, and the elements that could not be quantified.</summary>
    public sealed record IfcTakeoffResult(
        IReadOnlyList<StructuralTakeoffInput> Inputs,
        double ModelledRebarKg,
        int ModelledRebarBars,
        IReadOnlyList<IfcResidual> Residual,
        int ElementsRead,
        string VolumeUnitNote);

    /// <summary>
    /// Reads concrete quantities straight from a structural Revit/IFC model — the source that actually
    /// contains the whole building in 3D, so every drop panel, beam and transfer thickening is already in
    /// the volume. There is no measurement, no scale, no AI: each element's concrete volume is the model's
    /// own <c>NetVolume</c> base quantity, grouped by storey and element type into the rows the existing
    /// <see cref="StructuralTakeoffService"/> prices. This is why a model takeoff matches the QTO where a
    /// plan-PDF takeoff cannot — it reads the same model the QTO was scheduled from, not a flattened view.
    ///
    /// HONEST SCOPE: it reports exactly what the model carries. If base quantities were not exported
    /// (Revit's "Export base quantities" unticked), an element has no NetVolume and is listed as a residual
    /// rather than guessed. Reinforcing is summed ONLY where bars are 3D-modelled (IfcReinforcingBar); when
    /// rebar is detailed in 2D only — as it usually is for buildings — steel still comes from the density
    /// ratio downstream, the same hand method, and that is stated, not hidden.
    /// </summary>
    public static class IfcQuantityTakeoff
    {
        private const double SteelDensityKgPerM3 = 7850.0;

        public static IfcTakeoffResult Read(string stepText)
        {
            var map = IfcStepReader.Parse(stepText);

            var (volScale, volNote) = VolumeUnit(map);
            var levelOf = BuildContainment(map);          // elementId -> storey name
            var volumeOf = BuildQuantities(map);          // elementId -> NetVolume (model units)

            var inputs = new List<StructuralTakeoffInput>();
            var residual = new List<IfcResidual>();
            double rebarKg = 0; int rebarBars = 0; int read = 0;

            foreach (var (id, e) in map)
            {
                // Modelled reinforcing — exact steel where it exists (rare for buildings, but honoured).
                if (e.Type is "IFCREINFORCINGBAR" or "IFCREINFORCINGMESH")
                {
                    if (volumeOf.TryGetValue(id, out var rv) && rv > 0)
                    { rebarKg += rv * volScale * SteelDensityKgPerM3; rebarBars++; }
                    continue;
                }

                var element = MapElement(e);
                if (element is null) continue;            // not a concrete structural element (steel member, plate…)
                read++;

                string level = levelOf.TryGetValue(id, out var lv) && lv.Length > 0 ? lv : "(unassigned)";
                string tag = ElementTag(e);

                if (!volumeOf.TryGetValue(id, out var vol) || !(vol > 0))
                {
                    residual.Add(new IfcResidual(e.Type, level, tag,
                        "no NetVolume base quantity in the model — re-export with 'Export base quantities' ticked, or this element carries no volume."));
                    continue;
                }

                inputs.Add(new StructuralTakeoffInput(level, element.Value, Variant: null, ConcreteVolume: vol * volScale));
            }

            // Collapse to one row per (level, element) — the report groups anyway, but a tidy input set keeps
            // totals auditable and the xlsx readable.
            var grouped = inputs
                .GroupBy(r => (r.Level, r.Element))
                .Select(g => new StructuralTakeoffInput(g.Key.Level, g.Key.Element, null, g.Sum(x => x.ConcreteVolume)))
                .OrderBy(r => r.Level, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Element)
                .ToList();

            return new IfcTakeoffResult(grouped, rebarKg, rebarBars, residual, read, volNote);
        }

        // IfcSlab .BASESLAB. is a mat/raft (foundation), every other slab is a suspended slab. Walls, columns,
        // beams map straight across; footings and piles are foundations. Steel framing members/plates are not
        // concrete and are skipped (a structural-steel takeoff is a different tool).
        private static TakeoffElementType? MapElement(IfcEntity e) => e.Type switch
        {
            "IFCSLAB" or "IFCSLABSTANDARDCASE" => PredefinedType(e) == "BASESLAB" ? TakeoffElementType.Foundation : TakeoffElementType.Slab,
            "IFCWALL" or "IFCWALLSTANDARDCASE" => TakeoffElementType.Wall,
            "IFCCOLUMN" or "IFCCOLUMNSTANDARDCASE" => TakeoffElementType.Column,
            "IFCBEAM" or "IFCBEAMSTANDARDCASE" => TakeoffElementType.Beam,
            "IFCFOOTING" or "IFCPILE" or "IFCPILECAP" => TakeoffElementType.Foundation,
            _ => null,
        };

        // ---- containment: element -> storey name via IfcRelContainedInSpatialStructure ----
        private static Dictionary<int, string> BuildContainment(IReadOnlyDictionary<int, IfcEntity> map)
        {
            var result = new Dictionary<int, string>();
            foreach (var e in map.Values)
            {
                if (e.Type != "IFCRELCONTAINEDINSPATIALSTRUCTURE" || e.Args.Count < 6) continue;
                // args: guid, owner, name, desc, RelatedElements(list), RelatingStructure(ref)
                string storey = StoreyName(map, e.Args[5]);
                foreach (var item in IfcStepReader.ListItems(e.Args[4]))
                    if (IfcStepReader.RefId(item) is int eid) result[eid] = storey;
            }
            return result;
        }

        private static string StoreyName(IReadOnlyDictionary<int, IfcEntity> map, string structRef)
        {
            if (IfcStepReader.RefId(structRef) is not int sid || !map.TryGetValue(sid, out var s)) return "";
            // IfcBuildingStorey / IfcBuilding / IfcSite all carry Name at arg index 2.
            return s.Args.Count > 2 ? IfcStepReader.Text(s.Args[2]) : "";
        }

        // ---- quantities: element -> NetVolume via IfcRelDefinesByProperties -> IfcElementQuantity ----
        private static Dictionary<int, double> BuildQuantities(IReadOnlyDictionary<int, IfcEntity> map)
        {
            var result = new Dictionary<int, double>();
            foreach (var e in map.Values)
            {
                if (e.Type != "IFCRELDEFINESBYPROPERTIES" || e.Args.Count < 6) continue;
                // args: guid, owner, name, desc, RelatedObjects(list), RelatingPropertyDefinition(ref)
                if (IfcStepReader.RefId(e.Args[5]) is not int qsId
                    || !map.TryGetValue(qsId, out var qs) || qs.Type != "IFCELEMENTQUANTITY") continue;

                double? vol = NetVolume(map, qs);
                if (vol is null) continue;
                foreach (var item in IfcStepReader.ListItems(e.Args[4]))
                    if (IfcStepReader.RefId(item) is int eid) result[eid] = vol.Value;
            }
            return result;
        }

        private static double? NetVolume(IReadOnlyDictionary<int, IfcEntity> map, IfcEntity quantitySet)
        {
            // IfcElementQuantity args: guid, owner, name, desc, methodOfMeasurement, Quantities(list)
            if (quantitySet.Args.Count < 6) return null;
            double? net = null, gross = null;
            foreach (var item in IfcStepReader.ListItems(quantitySet.Args[5]))
            {
                if (IfcStepReader.RefId(item) is not int qid || !map.TryGetValue(qid, out var q)) continue;
                if (q.Type != "IFCQUANTITYVOLUME" || q.Args.Count < 4) continue;
                // IfcQuantityVolume args: name, desc, unit, VolumeValue, [formula]
                string name = IfcStepReader.Text(q.Args[0]).ToUpperInvariant();
                double? val = IfcStepReader.Number(q.Args[3]);
                if (val is null) continue;
                if (name.Contains("NET")) net = val;
                else if (name.Contains("GROSS")) gross = val;
                else net ??= val;                       // an unqualified single volume — take it
            }
            return net ?? gross;                         // prefer net of openings; fall back to gross
        }

        // ---- volume unit: cubic metre unless the model declares a milli/centi length unit ----
        private static (double scale, string note) VolumeUnit(IReadOnlyDictionary<int, IfcEntity> map)
        {
            foreach (var e in map.Values)
            {
                if (e.Type != "IFCSIUNIT") continue;
                // args: dimensions, UnitType(.VOLUMEUNIT.), [prefix(.MILLI.)], name(.CUBIC_METRE.)
                bool isVolume = e.Args.Any(a => IfcStepReader.Enum(a) == "VOLUMEUNIT");
                if (!isVolume) continue;
                string prefix = e.Args.Select(IfcStepReader.Enum)
                    .FirstOrDefault(x => x is "MILLI" or "CENTI" or "DECI") ?? "";
                return prefix switch
                {
                    "MILLI" => (1e-9, "model volume unit: cubic millimetre → m³"),
                    "CENTI" => (1e-6, "model volume unit: cubic centimetre → m³"),
                    "DECI"  => (1e-3, "model volume unit: cubic decimetre → m³"),
                    _ => (1.0, "model volume unit: cubic metre"),
                };
            }
            return (1.0, "model volume unit: assumed cubic metre (no IfcSIUnit volume declaration found)");
        }

        private static string PredefinedType(IfcEntity e)
        {
            // Predefined type is the last enum arg on the standard element entities.
            for (int k = e.Args.Count - 1; k >= 0; k--)
            {
                string en = IfcStepReader.Enum(e.Args[k]);
                if (en.Length > 0 && en != "NOTDEFINED") return en;
            }
            return "";
        }

        private static string ElementTag(IfcEntity e) =>
            e.Args.Count > 2 ? IfcStepReader.Text(e.Args[2]) : $"#{e.Id}";
    }
}

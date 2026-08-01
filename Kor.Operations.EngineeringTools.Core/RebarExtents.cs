#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.QuantityTakeoff;

namespace Kor.Operations.EngineeringTools.RebarChange
{
    /// <summary>
    /// THE FUSION SEAM between the two tools: the concrete takeoff MEASURES extents (per-level slab
    /// plate areas); the rebar change tool reads intensity changes (ΔAs from "15M @ 350 EACH WAY"
    /// call-outs) but has no extent to multiply by. This carries the measured extents across —
    /// exactly the estimator's own method (their change orders price mats as extent × intensity),
    /// but from OUR measurement with its basis stated.
    ///
    /// HARD RULE the design enforces: extent-based pounds are an ESTIMATE with a measured-area basis
    /// and are reported SEPARATELY — they never blend into the exact call-out delta. The area cells
    /// stay editable in the workbook; supplying a measured value replaces an empty cell, not a fact.
    /// v1 carries SLAB plate areas only: they are direct measurements. Wall face areas would need a
    /// length÷thickness×height chain the takeoff cannot yet state cleanly, so wall grids stay manual.
    /// </summary>
    public static class RebarExtents
    {
        /// <summary>One takeoff level: its label ("P3", "2 NORTH", "6-18 NORTH (x13)"), the floors it
        /// prices, its tower, and the measured slab plate area PER FLOOR (sq.ft).</summary>
        public sealed record LevelExtent(string Label, string? Tower, IReadOnlyList<string> Floors, double SlabSqFtPerFloor);

        /// <summary>Parse a takeoff level label into (tower, normalized floor keys) — the one shared
        /// implementation ("6-18 NORTH (x13)" → NORTH, L6..L18; "P3" → null, P3).</summary>
        public static (string? Tower, List<string> Floors) ParseLevelLabel(string label)
        {
            string u = (label ?? "").ToUpperInvariant();
            string? tw = u.Contains("NORTH") ? "NORTH" : u.Contains("SOUTH") ? "SOUTH"
                       : u.Contains("EAST") ? "EAST" : u.Contains("WEST") ? "WEST" : null;
            var band = Regex.Match(u, @"^(\d+)\s*-\s*(\d+)\b");
            if (band.Success)
            {
                int lo = int.Parse(band.Groups[1].Value), hi = int.Parse(band.Groups[2].Value);
                return (tw, Enumerable.Range(lo, hi - lo + 1).Select(i => $"L{i}").ToList());
            }
            return (tw, new List<string> { SlabTakeoffEngine.NormalizeLevelKey(label) });
        }

        public static string ToJson(IEnumerable<LevelExtent> levels) =>
            JsonSerializer.Serialize(new
            {
                levels = levels.Select(l => new { label = l.Label, slabSqFtPerFloor = l.SlabSqFtPerFloor }),
            }, new JsonSerializerOptions { WriteIndented = true });

        public static IReadOnlyList<LevelExtent> FromJson(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var outp = new List<LevelExtent>();
            foreach (var e in doc.RootElement.GetProperty("levels").EnumerateArray())
            {
                string label = e.GetProperty("label").GetString() ?? "";
                double sqft = e.TryGetProperty("slabSqFtPerFloor", out var a) ? a.GetDouble() : 0;
                var (tw, floors) = ParseLevelLabel(label);
                if (label.Length > 0 && sqft > 0) outp.Add(new LevelExtent(label, tw, floors, sqft));
            }
            return outp;
        }

        // A sheet title names its floor: "PARKING LEVEL P3 PLAN - ...", "NT -LEVEL 19 PLAN ...",
        // "LEVEL 1 PLAN - SLAB REINFORCING - ST". Level token + tower word → the extent that floor
        // belongs to. NT/ST shorthands map to their cardinals.
        private static readonly Regex TitleLevel = new(@"\bLEVEL\s+(P?\d{1,2})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Map drawing sheets to slab areas (m²) for the grid pricer: each sheet whose TITLE names a
        /// floor covered by a measured extent gets that floor's plate area. Sheets that name no
        /// recognisable floor (schedules, details) get nothing — their grids stay manual.
        /// </summary>
        public static Dictionary<string, double> SlabAreasM2BySheet(
            IEnumerable<(string Sheet, string Title)> sheets, IReadOnlyList<LevelExtent> extents)
        {
            const double SqFtToM2 = 0.09290304;
            var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var (sheet, title) in sheets)
            {
                if (string.IsNullOrWhiteSpace(title)) continue;
                var m = TitleLevel.Match(title);
                if (!m.Success) continue;
                string floor = SlabTakeoffEngine.NormalizeLevelKey(m.Groups[1].Value);
                string u = title.ToUpperInvariant();
                string? tw = u.Contains("NORTH") || Regex.IsMatch(u, @"\bNT\b") ? "NORTH"
                           : u.Contains("SOUTH") || Regex.IsMatch(u, @"\bST\b") ? "SOUTH"
                           : u.Contains("EAST") ? "EAST" : u.Contains("WEST") ? "WEST" : null;

                // Tower-qualified match first; a tower-less extent (parkade plate) covers both halves.
                var hit = extents.FirstOrDefault(x => x.Floors.Contains(floor) && (x.Tower ?? "") == (tw ?? ""))
                       ?? extents.FirstOrDefault(x => x.Floors.Contains(floor) && x.Tower is null);
                if (hit is not null) map[sheet] = hit.SlabSqFtPerFloor * SqFtToM2;
            }
            return map;
        }
    }
}

// COPY of KOR.Drafter Kor.Operations.EngineeringTools.Core/Csi/UsesByFloor.cs (tested there: UsesByFloorTests); keep the two the same.
// USES BY FLOOR (KOR.Drafter row 1982, JM-MODEL-24, 13 Nov 2026). The pure part of EtabsOpenCheck --reactions' floor loads, here so it
// is tested (EtabsOpenCheck carries a copy, as it does LateralEquilibrium):
//
//   uses.csv: storey,use,sdl_kpa,ll_kpa[,how]   one row per storey (the storey's use), and now also rows keyed "<storey>|<floor label>"
//             (e.g. "L15|KF97") that give ONE floor area its own use - a corridor or a balcony at S111's 4.8 kPa on a residential
//             storey at 1.9 kPa (SWEEP-final24 row 6.1: one use per storey put 1.9 kPa on corridors and balconies).
//   For(table, storey, label)   the floor's own row when there is one, else its storey's row, else null (no use: no load, counted).
//
// Why: ETABS took the storey's use for every floor area of the storey; the reactions it hands the SAFE models (loads from above) then
// carried residential live load on the corridors and balconies of L4-L31.
// COVERS: the key grammar (a "|" key needs both parts; a duplicate key refuses), finite kPa of 0 or more, the lookup order.
// DOES NOT: check that a floor key names a floor that exists (EtabsOpenCheck counts floor rows used, and says which were not);
// check the kPa against S111 (the model-audit's U1 does, on the same file).
// SAME-CLASS FAULT IT WOULD MISS: a floor row whose label ETABS renamed on import (the row is unused; EtabsOpenCheck lists it).
using System.Globalization;

namespace Kor.EtabsOpenCheck;

public static class UsesByFloor
{
    public sealed record Use(string Name, double SdlKpa, double LiveKpa);

    /// <summary>The rows of uses.csv (header skipped by the caller), each as its fields. Throws on a malformed key, a duplicate key
    /// or a kPa that is not a finite number of 0 or more.</summary>
    public static IReadOnlyDictionary<string, Use> Parse(IEnumerable<string[]> rows)
    {
        var t = new Dictionary<string, Use>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in rows)
        {
            if (p.Length < 4) throw new FormatException($"uses.csv row '{string.Join(",", p)}': fewer than 4 fields");
            string key = p[0].Trim();
            if (key.Length == 0) throw new FormatException("uses.csv: a row with no storey");
            if (key.Contains('|'))
            {
                var kp = key.Split('|');
                if (kp.Length != 2 || kp[0].Trim().Length == 0 || kp[1].Trim().Length == 0) throw new FormatException($"uses.csv key '{key}': want <storey>|<floor label>");
                key = kp[0].Trim() + "|" + kp[1].Trim();
            }
            double sdl = double.Parse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture), live = double.Parse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture);
            if (!double.IsFinite(sdl) || sdl < 0 || !double.IsFinite(live) || live < 0) throw new FormatException($"uses.csv key '{key}': kPa not a finite number of 0 or more");
            if (!t.TryAdd(key, new Use(p[1].Trim(), sdl, live))) throw new FormatException($"uses.csv key '{key}' given twice");
        }
        return t;
    }

    /// <summary>The use of one floor area: its own "storey|label" row, else its storey's row, else null.</summary>
    public static Use? For(IReadOnlyDictionary<string, Use> table, string storey, string label) =>
        table.TryGetValue(storey + "|" + label, out var own) ? own : table.TryGetValue(storey, out var st) ? st : null;

    /// <summary>The "storey|label" keys of the table.</summary>
    public static IEnumerable<string> FloorKeys(IReadOnlyDictionary<string, Use> table) => table.Keys.Where(k => k.Contains('|'));
}

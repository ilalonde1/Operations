// LATERAL EQUILIBRIUM AND SECTION CUTS (KOR.Drafter row 1982 / Framework row 2129, JM-LATERAL-2, 13 Nov 2026). The pure
// parts of EtabsOpenCheck --lateral's section cuts and its lateral equilibrium gate, here so they are tested:
//
//   ParseCuts("BASE-W=-1e6..61000,-1e6..1e6@P1/GF;CORE=74800..84800,50000..59600@P3@79800,54800")
//       the cuts to define: name = x0..x1,y0..y1 (plan window, mm) @ storey [@ x,y of the result point, mm]. EtabsOpenCheck
//       draws each as a horizontal quadrilateral 100 mm above the storey's level, so it cuts the members of the storey ABOVE
//       that level (a cut at P1/GF reads the shear the building above P1/GF hands down - its base shear).
//   UserLoads(e2k text)
//       the lateral load each WIND / SEISMIC pattern of USER LOADS states (USERLOAD FX FY lines), in kN, from the file's own
//       force unit - the applied side of the gate, read off the file ETABS opened, not off ETABS.
//   Gate(case, applied, reaction)
//       equilibrium: the reactions summed over EVERY restrained joint must balance the applied load within 1 % of its size.
//
// Why (run r25, 13 Nov): ETABS's BaseReact read WX as 4,319 kN with a 434 kN FY against 3,763 kN applied, and EQX 14,446 kN
// against r20's 11,995 - while the joint reactions summed over every restraint balanced WX to 0.1 kN. v3 had added UX UY
// restraints at P2 and P1/GF above the pinned P3; BaseReact does not carry them (gravity: BaseReact FX 4,021 kN under
// KOR-Dead, 0.00 in r20). So base-shear.csv stopped being the base shear and nothing said so. This gate says so.
// COVERS: the cut spec grammar (refusing a malformed one), the USERLOAD sum and its unit, the 1 % verdict both ways.
// DOES NOT: read an auto-generated seismic load (ETABS computes it; EtabsOpenCheck takes its applied side from a cut named
// ALL when the spec has one, else says the gate was not run); judge whether a cut is drawn in the right place.
// SAME-CLASS FAULT IT WOULD MISS: a load applied below the cut storey is in the reactions but not in an ALL cut above it -
// the gate then reads that load as a lack of equilibrium (a false FAIL, never a false PASS).
// THIS IS A COPY, for EtabsOpenCheck: the TESTED original is KOR.Drafter Kor.Operations.EngineeringTools.Core/Csi/LateralEquilibrium.cs
using System.Globalization;
using System.Text.RegularExpressions;

namespace Kor.EtabsOpenCheck;

public static class LateralEquilibrium
{
    private static readonly CultureInfo Ic = CultureInfo.InvariantCulture;

    public sealed record Cut(string Name, double X0, double X1, double Y0, double Y1, string Storey, double? Rx, double? Ry);

    /// <summary>"name=x0..x1,y0..y1@storey[@x,y]" entries split by ';' (mm). Throws FormatException naming the bad entry.</summary>
    public static List<Cut> ParseCuts(string spec)
    {
        var cuts = new List<Cut>();
        foreach (var raw in spec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var m = Regex.Match(raw, @"^([A-Za-z0-9_\-]+)\s*=\s*(-?[\d.eE+]+)\.\.(-?[\d.eE+]+)\s*,\s*(-?[\d.eE+]+)\.\.(-?[\d.eE+]+)\s*@\s*([^@]+?)\s*(?:@\s*(-?[\d.eE+]+)\s*,\s*(-?[\d.eE+]+))?$");
            if (!m.Success) throw new FormatException($"cut '{raw}' is not name=x0..x1,y0..y1@storey[@x,y] (mm)");
            double P(int g) => double.Parse(m.Groups[g].Value, NumberStyles.Float, Ic);
            var c = new Cut(m.Groups[1].Value, P(2), P(3), P(4), P(5), m.Groups[6].Value, m.Groups[7].Success ? P(7) : null, m.Groups[8].Success ? P(8) : null);
            if (!(c.X1 > c.X0 && c.Y1 > c.Y0)) throw new FormatException($"cut '{raw}': the window is empty (x0 < x1 and y0 < y1 wanted)");
            if (cuts.Any(x => x.Name.Equals(c.Name, StringComparison.OrdinalIgnoreCase))) throw new FormatException($"cut '{c.Name}' is named twice");
            cuts.Add(c);
        }
        if (cuts.Count == 0) throw new FormatException("no cut in the spec");
        return cuts;
    }

    /// <summary>kN per one unit of the e2k's force unit (UNITS "force" "length" "temp").</summary>
    public static double KnPer(string forceUnit) => forceUnit.ToUpperInvariant() switch
    {
        "LB" => 0.0044482216152605, "KIP" => 4.4482216152605, "N" => 0.001, "KN" => 1, "KGF" => 0.00980665, "TONF" => 9.80665, "KIPS" => 4.4482216152605,
        _ => throw new FormatException($"force unit '{forceUnit}' not known")
    };

    /// <summary>Each WIND/SEISMIC pattern's USERLOAD lines summed: pattern -> (FX, FY) in kN.</summary>
    public static Dictionary<string, (double Fx, double Fy)> UserLoads(string e2kText)
    {
        var unit = Regex.Match(e2kText, @"^\s*UNITS\s+""([^""]+)""", RegexOptions.Multiline);
        if (!unit.Success) throw new FormatException("the e2k has no UNITS line");
        double k = KnPer(unit.Groups[1].Value);
        var sum = new Dictionary<string, (double Fx, double Fy)>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(e2kText, @"^\s*(?:WIND|SEISMIC)\s+""([^""]+)""\s+""USERLOAD""(.*)$", RegexOptions.Multiline))
        {
            double V(string key) { var v = Regex.Match(m.Groups[2].Value, @"\b" + key + @"\s+(-?[\d.eE+]+)"); return v.Success ? double.Parse(v.Groups[1].Value, NumberStyles.Float, Ic) * k : 0; }
            var cur = sum.GetValueOrDefault(m.Groups[1].Value);
            sum[m.Groups[1].Value] = (cur.Fx + V("FX"), cur.Fy + V("FY"));
        }
        return sum;
    }

    /// <summary>Equilibrium: reaction + applied = 0 within tol x |applied|. signFree compares sizes per component (an applied
    /// side read off a section cut, whose sign depends on the side ETABS reports).</summary>
    public static (bool Ok, string Line) Gate(string caseName, (double Fx, double Fy) applied, (double Fx, double Fy) reaction, string appliedFrom, bool signFree = false, double tol = 0.01)
    {
        double size = Math.Sqrt(applied.Fx * applied.Fx + applied.Fy * applied.Fy);
        if (size < 1) return (true, $"GATE equilibrium lateral {caseName}: applied {size:N1} kN ({appliedFrom}) - no lateral load, not judged: PASS");
        double ex = signFree ? Math.Abs(reaction.Fx) - Math.Abs(applied.Fx) : reaction.Fx + applied.Fx;
        double ey = signFree ? Math.Abs(reaction.Fy) - Math.Abs(applied.Fy) : reaction.Fy + applied.Fy;
        double err = Math.Sqrt(ex * ex + ey * ey) / size;
        bool ok = err <= tol;
        return (ok, string.Format(Ic, "GATE equilibrium lateral {0}: reactions FX {1:N1} FY {2:N1} kN vs applied FX {3:N1} FY {4:N1} kN ({5}): out by {6:P2}: {7}",
            caseName, reaction.Fx, reaction.Fy, applied.Fx, applied.Fy, appliedFrom, err, ok ? "PASS" : "FAIL"));
    }
}

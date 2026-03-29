using System.Collections.Generic;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    /// <summary>
    /// Static database of concrete material properties and factored load combinations.
    /// Values: E and G in MPa; Fc in MPa.
    /// </summary>
    internal static class StructuralMaterialDatabase
    {
        // Eurocode Ecm, Gc, fck per grade (MPa)
        private static readonly IReadOnlyDictionary<string, (double E, double G, double Fc)> _grades =
            new Dictionary<string, (double, double, double)>
            {
                { "C20", (29962, 12804, 20) },
                { "C25", (31476, 13451, 25) },
                { "C28", (32308, 13806, 28) },
                { "C30", (32837, 14033, 30) },
                { "C32", (33346, 14251, 32) },
                { "C35", (34077, 14563, 35) },
                { "C40", (35220, 15051, 40) },
                { "C50", (37278, 15930, 50) },
            };

        public static IReadOnlyCollection<string> SupportedGrades => ((Dictionary<string, (double, double, double)>)_grades).Keys;

        /// <summary>Returns (E_MPa, G_MPa, Fc_MPa) for the grade, falling back to C30.</summary>
        public static (double E, double G, double Fc) GetGrade(string gradeCode)
            => _grades.TryGetValue(gradeCode ?? "C30", out var p) ? p : _grades["C30"];

        /// <summary>
        /// Returns factored load combinations for the requested code.
        /// Each combo: (Name, list of (PatternName, ScaleFactor)).
        /// Patterns containing "SDL"/"LIVE" are included only when hasSdl/hasLive is true.
        /// </summary>
        public static List<(string Name, List<(string Pat, double SF)> Cases)> BuildLoadCombinations(
            string code, bool hasSdl, bool hasLive)
        {
            var result = new List<(string Name, List<(string Pat, double SF)> Cases)>();

            void Add(string name, params (string Pat, double SF)[] cases)
            {
                var filtered = new List<(string Pat, double SF)>();
                foreach (var c in cases)
                {
                    if (c.Pat == "DEAD") { filtered.Add(c); continue; }
                    if (c.Pat == "SDL" && hasSdl) { filtered.Add(c); continue; }
                    if (c.Pat == "LIVE" && hasLive) { filtered.Add(c); continue; }
                }
                if (filtered.Count > 0)
                    result.Add((name, filtered));
            }

            switch (code)
            {
                case "AS/NZS":
                    Add("1.35G", ("DEAD", 1.35));
                    Add("1.2G+1.5Q", ("DEAD", 1.2), ("SDL", 1.2), ("LIVE", 1.5));
                    Add("1.2G+0.4Q", ("DEAD", 1.2), ("SDL", 1.2), ("LIVE", 0.4));
                    Add("G+Q", ("DEAD", 1.0), ("SDL", 1.0), ("LIVE", 1.0));
                    break;
                case "ASCE7":
                    Add("1.4D", ("DEAD", 1.4));
                    Add("1.2D+1.6L", ("DEAD", 1.2), ("SDL", 1.2), ("LIVE", 1.6));
                    Add("1.2D+1.0L", ("DEAD", 1.2), ("SDL", 1.2), ("LIVE", 1.0));
                    Add("D+L", ("DEAD", 1.0), ("SDL", 1.0), ("LIVE", 1.0));
                    break;
                case "EC0":
                    Add("1.35G+1.5Q", ("DEAD", 1.35), ("SDL", 1.35), ("LIVE", 1.5));
                    Add("1.35G+1.05Q", ("DEAD", 1.35), ("SDL", 1.35), ("LIVE", 1.05));
                    Add("G+1.5Q", ("DEAD", 1.0), ("SDL", 1.0), ("LIVE", 1.5));
                    Add("G+Q", ("DEAD", 1.0), ("SDL", 1.0), ("LIVE", 1.0));
                    break;
            }

            return result;
        }
    }
}

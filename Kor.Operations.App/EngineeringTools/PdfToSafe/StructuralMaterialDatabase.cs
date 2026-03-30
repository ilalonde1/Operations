#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    /// <summary>A single reinforcing bar size with dimensions in mm.</summary>
    internal sealed record RebarSize(string Name, double DiameterMm, double AreaMm2);

    /// <summary>
    /// Static database of concrete material properties, reinforcing bar sizes,
    /// and factored load combinations.
    /// Material values: E and G in MPa; Fc in MPa.
    /// </summary>
    internal static class StructuralMaterialDatabase
    {
        private static readonly IReadOnlyDictionary<string, double> _grades =
            new Dictionary<string, double>
            {
                { "C20", 20 },
                { "C25", 25 },
                { "C28", 28 },
                { "C30", 30 },
                { "C32", 32 },
                { "C35", 35 },
                { "C40", 40 },
                { "C50", 50 },
            };

        public static IReadOnlyCollection<string> SupportedGrades
            => ((Dictionary<string, double>)_grades).Keys;

        /// <summary>Returns (E_MPa, G_MPa, Fc_MPa) for the grade, falling back to C30.</summary>
        public static (double E, double G, double Fc) GetGrade(string gradeCode,
                                                                DesignCodeOption code = DesignCodeOption.None)
        {
            double fc = _grades.TryGetValue(gradeCode ?? "C30", out var value) ? value : _grades["C30"];
            double e = code switch
            {
                DesignCodeOption.CSA_A23_3_19 => 4500.0 * Math.Sqrt(fc),
                DesignCodeOption.ACI_318_19 => 4700.0 * Math.Sqrt(fc),
                DesignCodeOption.AS_3600_09 or DesignCodeOption.NZS_3101_06
                    => Math.Pow(2400.0, 1.5) * 0.043 * Math.Sqrt(fc),
                _ => 22000.0 * Math.Pow((fc + 8.0) / 10.0, 0.3),
            };
            double g = e / (2.0 * (1.0 + 0.20));
            return (e, g, fc);
        }

        // Reinforcing bar sizes

        private static readonly IReadOnlyList<RebarSize> _csaRebar = new[]
        {
            new RebarSize("10M",  11.3,  100),
            new RebarSize("15M",  16.0,  200),
            new RebarSize("20M",  19.5,  300),
            new RebarSize("25M",  25.2,  500),
            new RebarSize("30M",  29.9,  700),
            new RebarSize("35M",  35.7, 1000),
            new RebarSize("45M",  43.7, 1500),
            new RebarSize("55M",  56.4, 2500),
        };

        private static readonly IReadOnlyList<RebarSize> _aciRebar = new[]
        {
            new RebarSize("#3",   9.525,   71),
            new RebarSize("#4",  12.700,  129),
            new RebarSize("#5",  15.875,  200),
            new RebarSize("#6",  19.050,  284),
            new RebarSize("#7",  22.225,  387),
            new RebarSize("#8",  25.400,  510),
            new RebarSize("#9",  28.650,  645),
            new RebarSize("#10", 32.260,  819),
            new RebarSize("#11", 35.810, 1006),
            new RebarSize("#14", 43.000, 1452),
            new RebarSize("#18", 57.330, 2581),
        };

        private static readonly IReadOnlyList<RebarSize> _asNzsRebar = new[]
        {
            new RebarSize("N10",  10.0,   78.5),
            new RebarSize("N12",  12.0,  113.0),
            new RebarSize("N16",  16.0,  201.0),
            new RebarSize("N20",  20.0,  314.0),
            new RebarSize("N24",  24.0,  452.0),
            new RebarSize("N28",  28.0,  616.0),
            new RebarSize("N32",  32.0,  804.0),
            new RebarSize("N36",  36.0, 1020.0),
            new RebarSize("N40",  40.0, 1257.0),
        };

        private static readonly IReadOnlyList<RebarSize> _ec2Rebar = new[]
        {
            new RebarSize("B8",   8.0,   50.3),
            new RebarSize("B10", 10.0,   78.5),
            new RebarSize("B12", 12.0,  113.0),
            new RebarSize("B16", 16.0,  201.0),
            new RebarSize("B20", 20.0,  314.0),
            new RebarSize("B25", 25.0,  491.0),
            new RebarSize("B32", 32.0,  804.0),
            new RebarSize("B40", 40.0, 1257.0),
        };

        /// <summary>
        /// Returns the reinforcing bar size list appropriate for the given design code.
        /// Returns an empty list for <see cref="DesignCodeOption.None"/>.
        /// </summary>
        public static IReadOnlyList<RebarSize> GetRebarSizes(DesignCodeOption code) => code switch
        {
            DesignCodeOption.CSA_A23_3_19 => _csaRebar,
            DesignCodeOption.ACI_318_19   => _aciRebar,
            DesignCodeOption.AS_3600_09   => _asNzsRebar,
            DesignCodeOption.NZS_3101_06  => _asNzsRebar,
            DesignCodeOption.EC2_2004     => _ec2Rebar,
            _                             => System.Array.Empty<RebarSize>(),
        };

        /// <summary>
        /// Returns the SAFE F2K design code string for the given option,
        /// or null when <see cref="DesignCodeOption.None"/> is selected.
        /// </summary>
        public static string? GetDesignCodeString(DesignCodeOption code) => code switch
        {
            DesignCodeOption.CSA_A23_3_19 => "CSA A23.3-19",
            DesignCodeOption.ACI_318_19   => "ACI 318-19",
            DesignCodeOption.AS_3600_09   => "AS 3600-09",
            DesignCodeOption.EC2_2004     => "Eurocode 2-2004",
            DesignCodeOption.NZS_3101_06  => "NZS 3101-06",
            _                             => null,
        };

        // Load combinations

        /// <summary>
        /// Returns factored load combinations for the requested code.
        /// Each combo: (Name, list of (PatternName, ScaleFactor)).
        /// Patterns containing "SDL"/"LIVE"/"LBALC" are included only when
        /// hasSdl/hasLive/hasPt is true respectively.
        /// </summary>
        public static List<(string Name, List<(string Pat, double SF)> Cases)> BuildLoadCombinations(
            string code, bool hasSdl, bool hasLive, bool hasPt = false)
        {
            var result = new List<(string Name, List<(string Pat, double SF)> Cases)>();

            void Add(string name, params (string Pat, double SF)[] cases)
            {
                var filtered = new List<(string Pat, double SF)>();
                foreach (var c in cases)
                {
                    if (c.Pat == "DEAD")             { filtered.Add(c); continue; }
                    if (c.Pat == "SDL"   && hasSdl)  { filtered.Add(c); continue; }
                    if (c.Pat == "LIVE"  && hasLive) { filtered.Add(c); continue; }
                    if (c.Pat == "LBALC" && hasPt)   { filtered.Add(c); continue; }
                }
                if (filtered.Count > 0)
                    result.Add((name, filtered));
            }

            switch (code)
            {
                case "AS/NZS":
                    Add("1.35G",             ("DEAD", 1.35));
                    Add("1.2G+1.5Q",         ("DEAD", 1.2), ("SDL", 1.2), ("LIVE", 1.5));
                    Add("1.2G+0.4Q",         ("DEAD", 1.2), ("SDL", 1.2), ("LIVE", 0.4));
                    Add("G+Q",               ("DEAD", 1.0), ("SDL", 1.0), ("LIVE", 1.0));
                    if (hasPt)
                    {
                        Add("1.2G+1.5Q+1.5PT", ("DEAD", 1.2), ("SDL", 1.2), ("LIVE", 1.5), ("LBALC", 1.5));
                        Add("Immediate",       ("DEAD", 1.0), ("SDL", 1.0), ("LIVE", 1.0), ("LBALC", 1.0));
                        Add("Sustained",       ("DEAD", 1.0), ("SDL", 1.0), ("LIVE", 0.25), ("LBALC", 0.25));
                    }
                    break;

                case "ASCE7":
                    Add("1.4D",              ("DEAD", 1.4));
                    Add("1.2D+1.6L",         ("DEAD", 1.2), ("SDL", 1.2), ("LIVE", 1.6));
                    Add("1.2D+1.0L",         ("DEAD", 1.2), ("SDL", 1.2), ("LIVE", 1.0));
                    Add("D+L",               ("DEAD", 1.0), ("SDL", 1.0), ("LIVE", 1.0));
                    if (hasPt)
                    {
                        Add("1.2D+1.6L+1.0PT", ("DEAD", 1.2), ("SDL", 1.2), ("LIVE", 1.6), ("LBALC", 1.0));
                        Add("Immediate",       ("DEAD", 1.0), ("SDL", 1.0), ("LIVE", 1.0), ("LBALC", 1.0));
                        Add("Sustained",       ("DEAD", 1.0), ("SDL", 1.0), ("LIVE", 0.25), ("LBALC", 0.25));
                    }
                    break;

                case "EC0":
                    Add("1.35G+1.5Q",        ("DEAD", 1.35), ("SDL", 1.35), ("LIVE", 1.5));
                    Add("1.35G+1.05Q",       ("DEAD", 1.35), ("SDL", 1.35), ("LIVE", 1.05));
                    Add("G+1.5Q",            ("DEAD", 1.0),  ("SDL", 1.0),  ("LIVE", 1.5));
                    Add("G+Q",               ("DEAD", 1.0),  ("SDL", 1.0),  ("LIVE", 1.0));
                    if (hasPt)
                    {
                        Add("1.35G+1.5Q+1.5PT", ("DEAD", 1.35), ("SDL", 1.35), ("LIVE", 1.5),  ("LBALC", 1.5));
                        Add("Immediate",        ("DEAD", 1.0),  ("SDL", 1.0),  ("LIVE", 1.0),  ("LBALC", 1.0));
                        Add("Sustained",        ("DEAD", 1.0),  ("SDL", 1.0),  ("LIVE", 0.25), ("LBALC", 0.25));
                    }
                    break;

                case "NBC":
                    // NBC 2020 / CSA A23.3-19 factored combinations
                    Add("1.4D",                ("DEAD", 1.4));
                    Add("1.25D+1.5L",          ("DEAD", 1.25), ("SDL", 1.25), ("LIVE", 1.5));
                    Add("1.0D+0.5L",           ("DEAD", 1.0),  ("SDL", 1.0),  ("LIVE", 0.5));
                    Add("D+L",                 ("DEAD", 1.0),  ("SDL", 1.0),  ("LIVE", 1.0));
                    if (hasPt)
                    {
                        Add("1.25D+1.5L+1.5PT", ("DEAD", 1.25), ("SDL", 1.25), ("LIVE", 1.5),  ("LBALC", 1.5));
                        Add("1.0D+0.5L+1.0PT",  ("DEAD", 1.0),  ("SDL", 1.0),  ("LIVE", 0.5),  ("LBALC", 1.0));
                        Add("Immediate",        ("DEAD", 1.0),  ("SDL", 1.0),  ("LIVE", 1.0),  ("LBALC", 1.0));
                        Add("Sustained",        ("DEAD", 1.0),  ("SDL", 1.0),  ("LIVE", 0.25), ("LBALC", 0.25));
                    }
                    break;
            }

            return result;
        }
    }
}

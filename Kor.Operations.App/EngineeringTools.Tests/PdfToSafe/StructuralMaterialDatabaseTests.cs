#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe
{
    /// <summary>
    /// Locks the material database against silent edits. The database drives
    /// every export's material properties, rebar list, and load-combination
    /// set — getting any of these wrong ships structurally incorrect models.
    /// These tests catch: grade table drift, E-formula mistakes, rebar list
    /// reorder/rename, load combo factor typos.
    /// </summary>
    public class StructuralMaterialDatabaseCoverageTests
    {
        // ─── SupportedGrades ──────────────────────────────────────────────

        [Fact]
        public void SupportedGrades_ContainsCoreSet()
        {
            var grades = StructuralMaterialDatabase.SupportedGrades;
            // These are the canonical set engineers rely on; removing any is
            // a breaking change to saved projects.
            foreach (var g in new[] { "C20", "C25", "C28", "C30", "C32", "C35", "C40", "C50" })
                Assert.Contains(g, grades);
        }

        // ─── GetGrade: fc values ──────────────────────────────────────────

        [Theory]
        [InlineData("C20", 20)]
        [InlineData("C25", 25)]
        [InlineData("C28", 28)]
        [InlineData("C30", 30)]
        [InlineData("C32", 32)]
        [InlineData("C35", 35)]
        [InlineData("C40", 40)]
        [InlineData("C50", 50)]
        public void GetGrade_ReturnsCorrectFc(string code, double expectedFc)
        {
            var (_, _, fc) = StructuralMaterialDatabase.GetGrade(code);
            Assert.Equal(expectedFc, fc);
        }

        [Fact]
        public void GetGrade_UnknownCode_FallsBackToC30()
        {
            var (_, _, fc) = StructuralMaterialDatabase.GetGrade("C999");
            Assert.Equal(30.0, fc);
        }

        [Fact]
        public void GetGrade_NullCode_FallsBackToC30()
        {
            var (_, _, fc) = StructuralMaterialDatabase.GetGrade(null!);
            Assert.Equal(30.0, fc);
        }

        // ─── GetGrade: E formulas per code ────────────────────────────────

        [Fact]
        public void GetGrade_Csa_UsesCsaFormula_4500RootFc()
        {
            // CSA A23.3-19 cl. 8.6.2.3: Ec = 4500·√fc (normal-density concrete).
            var (e, _, _) = StructuralMaterialDatabase.GetGrade("C30", DesignCodeOption.CSA_A23_3_19);
            Assert.Equal(4500.0 * Math.Sqrt(30.0), e, 3);
        }

        [Fact]
        public void GetGrade_Aci_UsesAciFormula_4700RootFc()
        {
            // ACI 318-19 Eq. 19.2.2.1b: Ec = 4700·√fc (normal-weight).
            var (e, _, _) = StructuralMaterialDatabase.GetGrade("C30", DesignCodeOption.ACI_318_19);
            Assert.Equal(4700.0 * Math.Sqrt(30.0), e, 3);
        }

        [Fact]
        public void GetGrade_Ec2_UsesPowerFormula()
        {
            // Eurocode 2: 22000·((fc+8)/10)^0.3.
            var (e, _, _) = StructuralMaterialDatabase.GetGrade("C30", DesignCodeOption.EC2_2004);
            Assert.Equal(22000.0 * Math.Pow((30.0 + 8.0) / 10.0, 0.3), e, 3);
        }

        [Theory]
        [InlineData(DesignCodeOption.AS_3600_09)]
        [InlineData(DesignCodeOption.NZS_3101_06)]
        public void GetGrade_AsNzs_UsesDensityBasedFormula(DesignCodeOption code)
        {
            // AS/NZS: ρ^1.5 · 0.043 · √fc with ρ = 2400 kg/m³.
            var (e, _, _) = StructuralMaterialDatabase.GetGrade("C30", code);
            Assert.Equal(Math.Pow(2400.0, 1.5) * 0.043 * Math.Sqrt(30.0), e, 3);
        }

        [Fact]
        public void GetGrade_GModulus_DerivedFromPoissonRatio02()
        {
            // G = E / (2·(1+ν)); ν=0.20 fixed for concrete.
            var (e, g, _) = StructuralMaterialDatabase.GetGrade("C30", DesignCodeOption.CSA_A23_3_19);
            Assert.Equal(e / (2.0 * (1.0 + 0.20)), g, 6);
        }

        // ─── GetRebarSizes ─────────────────────────────────────────────────

        [Theory]
        [InlineData(DesignCodeOption.CSA_A23_3_19, "10M", "55M")]
        [InlineData(DesignCodeOption.ACI_318_19,   "#3",  "#18")]
        [InlineData(DesignCodeOption.AS_3600_09,   "N10", "N40")]
        [InlineData(DesignCodeOption.NZS_3101_06,  "N10", "N40")]
        [InlineData(DesignCodeOption.EC2_2004,     "B8",  "B40")]
        public void GetRebarSizes_ReturnsCorrectFamily(DesignCodeOption code, string first, string last)
        {
            var sizes = StructuralMaterialDatabase.GetRebarSizes(code);
            Assert.Equal(first, sizes.First().Name);
            Assert.Equal(last,  sizes.Last().Name);
        }

        [Fact]
        public void GetRebarSizes_None_ReturnsEmpty()
        {
            Assert.Empty(StructuralMaterialDatabase.GetRebarSizes(DesignCodeOption.None));
        }

        [Fact]
        public void GetRebarSizes_CsaBarsInAscendingDiameterOrder()
        {
            var sizes = StructuralMaterialDatabase.GetRebarSizes(DesignCodeOption.CSA_A23_3_19);
            for (int i = 1; i < sizes.Count; i++)
                Assert.True(sizes[i].DiameterMm > sizes[i - 1].DiameterMm,
                    $"Rebar order broken at index {i}");
        }

        [Fact]
        public void GetRebarSizes_AreaMatchesCircleOfDiameterApproximately()
        {
            // Sanity: stored area ≈ π·d²/4 within 2 % (CSA 15M nominal = 200 mm² but
            // AUTO-generated variants can round to published values).
            foreach (var r in StructuralMaterialDatabase.GetRebarSizes(DesignCodeOption.CSA_A23_3_19))
            {
                double geometric = Math.PI * r.DiameterMm * r.DiameterMm / 4.0;
                double error = Math.Abs(geometric - r.AreaMm2) / r.AreaMm2;
                Assert.True(error < 0.02,
                    $"{r.Name} area mismatch: stored {r.AreaMm2} vs geometric {geometric:F1}");
            }
        }

        // ─── GetDesignCodeString ──────────────────────────────────────────

        [Theory]
        [InlineData(DesignCodeOption.CSA_A23_3_19, "CSA A23.3-19")]
        [InlineData(DesignCodeOption.ACI_318_19,   "ACI 318-19")]
        [InlineData(DesignCodeOption.AS_3600_09,   "AS 3600-09")]
        [InlineData(DesignCodeOption.EC2_2004,     "Eurocode 2-2004")]
        [InlineData(DesignCodeOption.NZS_3101_06,  "NZS 3101-06")]
        public void GetDesignCodeString_ReturnsExactSafeLiteral(DesignCodeOption code, string expected)
        {
            Assert.Equal(expected, StructuralMaterialDatabase.GetDesignCodeString(code));
        }

        [Fact]
        public void GetDesignCodeString_None_ReturnsNull()
        {
            Assert.Null(StructuralMaterialDatabase.GetDesignCodeString(DesignCodeOption.None));
        }

        // ─── BuildLoadCombinations: presence/absence by flag ──────────────

        [Fact]
        public void BuildLoadCombinations_UnknownCode_ReturnsEmpty()
        {
            var combos = StructuralMaterialDatabase.BuildLoadCombinations("XYZ", true, true);
            Assert.Empty(combos);
        }

        [Fact]
        public void BuildLoadCombinations_DeadOnly_YieldsCoreDeadCombo()
        {
            // hasSdl=false, hasLive=false → only DEAD combos survive the filter.
            var combos = StructuralMaterialDatabase.BuildLoadCombinations("NBC", false, false);
            Assert.Contains(combos, c => c.Name == "1.4D");
            // And LIVE-bearing combos collapse away:
            Assert.DoesNotContain(combos, c =>
                c.Cases.Any(p => p.Pat == "LIVE" || p.Pat == "SDL"));
        }

        [Fact]
        public void BuildLoadCombinations_Nbc_SdlAndLive_UsesFactoredCases()
        {
            var combos = StructuralMaterialDatabase.BuildLoadCombinations("NBC", true, true);
            var names = combos.Select(c => c.Name).ToList();
            Assert.Contains("1.4D",          names);
            Assert.Contains("1.25D+1.5L",    names);
            Assert.Contains("1.0D+0.5L",     names);
            Assert.Contains("D+L",           names);
            // 0.9D+1.5L removed — non-standard for gravity-only floor models (NBC reversal case)
            Assert.DoesNotContain("0.9D+1.5L", names);

            // Sanity-check ONE row's factor values — catches swapped / dropped scalars.
            var c1 = combos.First(c => c.Name == "1.25D+1.5L").Cases.ToDictionary(x => x.Pat, x => x.SF);
            Assert.Equal(1.25, c1["DEAD"]);
            Assert.Equal(1.25, c1["SDL"]);
            Assert.Equal(1.5,  c1["LIVE"]);
        }

        [Fact]
        public void BuildLoadCombinations_PtFlag_AddsPtCombos()
        {
            var withoutPt = StructuralMaterialDatabase.BuildLoadCombinations("NBC", true, true, hasPt: false);
            var withPt    = StructuralMaterialDatabase.BuildLoadCombinations("NBC", true, true, hasPt: true);

            Assert.True(withPt.Count > withoutPt.Count);
            Assert.Contains(withPt, c => c.Cases.Any(p => p.Pat == "LBALC"));
            Assert.DoesNotContain(withoutPt, c => c.Cases.Any(p => p.Pat == "LBALC"));
        }

        [Theory]
        [InlineData("NBC")]
        [InlineData("ASCE7")]
        [InlineData("EC0")]
        [InlineData("AS/NZS")]
        public void BuildLoadCombinations_EveryFamily_ProducesDeadCombo(string family)
        {
            var combos = StructuralMaterialDatabase.BuildLoadCombinations(family, false, false);
            Assert.NotEmpty(combos);
            Assert.All(combos, c => Assert.Contains(c.Cases, p => p.Pat == "DEAD"));
        }
    }
}

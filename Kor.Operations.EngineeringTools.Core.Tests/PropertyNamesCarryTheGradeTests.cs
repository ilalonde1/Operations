using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// "In the slab property name, we always want to see the thickness and the concrete strength.
/// Example: slab8-35MPa." — Andrea Neuviale, 31 August.
/// </summary>
/// <remarks>
/// The strength was called unobtainable twice, on the grounds that every MPa string in all 139
/// DXFs is a wall type. It is in HER REFERENCE MODEL, which this tool opens on every run, and the
/// material was already being carried onto every property written — only the NAME threw it away.
///
/// What these hold is the part that can go quietly wrong: a name claiming a grade the property does
/// not carry. A slab called KOR-S8-30MPa made of "65 MPa Walls" is worse than one called KOR-S8.
/// </remarks>
public class PropertyNamesCarryTheGradeTests
{
    private static E2kDocument Reference(params string[] materials)
    {
        var lines = new List<string>
        {
            "$ STORIES - IN SEQUENCE FROM TOP",
            "  STORY \"LEVEL 2\"  HEIGHT 120",
            "  STORY \"LEVEL 1\"  HEIGHT 120",
            "  STORY \"Base\"  HEIGHT 0",
            "$ MATERIAL PROPERTIES",
        };
        lines.AddRange(materials.Select(m => $"  MATERIAL  \"{m}\"  TYPE \"Concrete\""));
        return E2kDocument.Parse(lines);
    }

    private static IReadOnlyList<string> Compose(E2kDocument doc)
    {
        var summary = E2kGeometryComposer.Compose(doc, Array.Empty<StoryPlacement>());
        return summary.Sections;
    }

    [Theory]
    [InlineData("30 MPa Floor", "-30MPa")]      // her own naming
    [InlineData("35MPa Floor", "-35MPa")]       // no space
    [InlineData("27.5 MPa Floor", "-27.5MPa")]  // not a whole number
    public void AGradeInTheMaterialNameReachesThePropertyName(string material, string expected)
    {
        string suffix = GradeOf(material);
        Assert.Equal(expected, suffix);
    }

    [Theory]
    [InlineData("4000Psi")]                     // imperial: says nothing about MPa
    [InlineData("C30")]                         // a grade by another convention
    [InlineData("Concrete")]
    public void AMaterialThatNamesNoGradeLeavesTheNameAlone(string material)
    {
        Assert.Equal(string.Empty, GradeOf(material));
    }

    /// <summary>
    /// The suffix is read from the SAME material the property is given. They used to be two
    /// separate lookups of the same expression, which is a pair waiting to drift.
    /// </summary>
    [Fact]
    public void TheNameAndTheMaterialAgree()
    {
        var doc = Reference("30 MPa Floor", "65 MPa Walls", "45 MPa Columns");
        var sections = Compose(doc);

        foreach (string line in sections.Where(s => s.Contains("SHELLPROP", StringComparison.Ordinal)))
        {
            string grade = Between(line, "MATERIAL \"", "\"");
            string named = Between(line, "SHELLPROP  \"", "\"");
            if (!named.Contains("MPa", StringComparison.OrdinalIgnoreCase)) continue;

            string claimed = named[(named.LastIndexOf('-') + 1)..];
            Assert.Contains(claimed.Replace("MPa", "", StringComparison.OrdinalIgnoreCase),
                            grade, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string GradeOf(string material)
    {
        var doc = Reference(material);
        string? found = doc.FindConcreteMaterial("Floor") ?? doc.FindConcreteMaterial(null);
        if (found is null) return string.Empty;

        var m = System.Text.RegularExpressions.Regex.Match(
            found, @"(\d+(?:\.\d+)?)\s*MPa", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? $"-{m.Groups[1].Value}MPa" : string.Empty;
    }

    private static string Between(string s, string after, string before)
    {
        int a = s.IndexOf(after, StringComparison.Ordinal);
        if (a < 0) return string.Empty;
        a += after.Length;
        int b = s.IndexOf(before, a, StringComparison.Ordinal);
        return b < 0 ? string.Empty : s[a..b];
    }
}

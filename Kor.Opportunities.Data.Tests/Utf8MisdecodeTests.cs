using Kor.Opportunities.Data.Intel;
using Xunit;

namespace Kor.Opportunities.Data.Tests;

/// <summary>
/// Utf8Misdecode against strings taken from the live store on 2026-10-15: the garbled shapes that
/// migration 355 found, and the genuine French and Cree accents it correctly left alone.
/// </summary>
public sealed class Utf8MisdecodeTests
{
    // "—" (E2 80 94) read as Windows-1252 is "â" "€" and U+201D.
    private const string GarbledEmDash = "â€”";

    [Fact]
    public void RepairsAGarbledEmDash()
    {
        Assert.True(Utf8Misdecode.TryRepair("Principal " + GarbledEmDash + " Land Development departure", out var fixedText));
        Assert.Equal("Principal — Land Development departure", fixedText);
    }

    [Fact]
    public void RepairsAGarbledAccent()
    {
        Assert.True(Utf8Misdecode.TryRepair("MontrÃ©al", out var fixedText));
        Assert.Equal("Montréal", fixedText);
    }

    [Theory]
    [InlineData("Matthew Derkach: Mâmawatoskêwak Advisory Circle")]
    [InlineData("Bernard Serge Gagné credited as project architect for L'Embâcle")]
    [InlineData("École primaire des Grands-Bâtisseurs in Beauport")]
    [InlineData("12-unit Metis seniors housing (Otâ Wîkitâhk) completed")]
    [InlineData("São Paulo, Zürich, Bogotá, naïve, ça va")]
    public void LeavesGenuineAccentsAlone(string genuine)
    {
        Assert.False(Utf8Misdecode.TryRepair(genuine, out var unchanged));
        Assert.Equal(genuine, unchanged);
    }

    [Fact]
    public void RepairsOnlyTheGarbledPartOfMixedText()
    {
        Assert.True(Utf8Misdecode.TryRepair("A — B " + GarbledEmDash + " C, Gagné", out var fixedText));
        Assert.Equal("A — B — C, Gagné", fixedText);
    }

    [Fact]
    public void KeepsCharactersOutsideWindows1252()
    {
        Assert.True(Utf8Misdecode.TryRepair("東京 " + GarbledEmDash + " office", out var fixedText));
        Assert.Equal("東京 — office", fixedText);
    }

    [Theory]
    [InlineData("plain ascii, nothing to do")]
    [InlineData("")]
    public void ReportsNothingToRepair(string input)
    {
        Assert.False(Utf8Misdecode.TryRepair(input, out var unchanged));
        Assert.Equal(input, unchanged);
    }

    [Fact]
    public void ANullStaysEmptyAndUnrepaired()
    {
        Assert.False(Utf8Misdecode.TryRepair(null, out var unchanged));
        Assert.Equal(string.Empty, unchanged);
    }

    [Theory]
    [InlineData("em dash — here")]
    [InlineData("it’s “quoted”")]
    [InlineData("café über École")]
    [InlineData("emoji \U0001F600 four bytes")]
    [InlineData("euro € and ellipsis …")]
    public void GarbleThenRepairIsTheIdentity(string clean)
    {
        var garbled = Utf8Misdecode.Garble(clean);
        Assert.NotNull(garbled);
        Assert.NotEqual(clean, garbled);
        Assert.True(Utf8Misdecode.TryRepair(garbled, out var back));
        Assert.Equal(clean, back);
    }

    [Fact]
    public void TheEmDashGarblesToTheShapeFoundInTheStore()
    {
        Assert.Equal(GarbledEmDash, Utf8Misdecode.Garble("—"));
    }

    [Fact]
    public void ARightDoubleQuoteRoundTripsOrIsReportedUnprovable()
    {
        // U+201D is E2 80 9D, and 0x9D has no Windows-1252 character. Garble must either reproduce
        // what PowerShell 5.1 produced (U+009D) and repair back, or say it cannot, never guess.
        var garbled = Utf8Misdecode.Garble("”");
        if (garbled is null) return;
        Assert.True(Utf8Misdecode.TryRepair(garbled, out var back));
        Assert.Equal("”", back);
    }
}

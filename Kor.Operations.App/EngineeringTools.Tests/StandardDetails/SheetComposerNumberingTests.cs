using Kor.Operations.StandardDetails;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.StandardDetails;

/// <summary>
/// The rule that hands a composed sheet its number: the next free number in the S1.NN series the
/// standards sheets already use, read from the live sheet list at save time. The name is the user's;
/// the number is never typed.
///
/// Covers: the pure numbering rule (empty list, gaps, case, other series present, wide numbers).
/// Does NOT cover: the bridge query that supplies the list, the race between two simultaneous saves
/// (Revit refuses the duplicate; the second save fails loudly), or the window wiring.
/// A same-class fault it would NOT catch: a sheet whose number carries trailing whitespace from Revit.
/// </summary>
public sealed class SheetComposerNumberingTests
{
    [Fact]
    public void An_empty_model_starts_the_series()
    {
        Assert.Equal("S1.01", StandardDetailsSheetComposer.NextSheetNumber(new string[0]));
    }

    [Fact]
    public void The_next_number_follows_the_highest_in_the_series_not_the_first_gap()
    {
        // The template has S1.01..S1.19 with S1.09 and S1.10 carrying no details; a gap is not free.
        var existing = new[] { "S1.01", "S1.02", "S1.08", "S1.11", "S1.19" };
        Assert.Equal("S1.20", StandardDetailsSheetComposer.NextSheetNumber(existing));
    }

    [Fact]
    public void Other_series_and_sheet_names_are_ignored()
    {
        var existing = new[] { "S0.01", "S305", "S5.05", "A1.01", "s1.03", "S1.7", "General Notes" };
        Assert.Equal("S1.04", StandardDetailsSheetComposer.NextSheetNumber(existing));
    }

    [Fact]
    public void Numbers_past_two_digits_keep_counting()
    {
        Assert.Equal("S1.100", StandardDetailsSheetComposer.NextSheetNumber(new[] { "S1.99" }));
        Assert.Equal("S1.101", StandardDetailsSheetComposer.NextSheetNumber(new[] { "S1.100" }));
    }
}

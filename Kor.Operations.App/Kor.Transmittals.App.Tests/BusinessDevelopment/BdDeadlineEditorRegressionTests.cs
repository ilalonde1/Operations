#nullable enable
using System.IO;
using Kor.Operations.App.Opportunities;
using Xunit;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

/// <summary>
/// Brief 5 / BD-A10: EXPECTED a date edit preserves stored time/offset; ACTUAL is
/// the editor's production conversion helper and load/save wiring. Covers offset
/// date boundaries, DST date changes, fractional seconds, clearing and new values.
/// Does not instantiate WPF or persist a row. A binding/commit failure that supplies
/// the old SelectedDate, or an incorrect source offset, would escape these gates.
/// Authored only; no tests/builds/DB access were run for this brief.
/// </summary>
public sealed class BdDeadlineEditorRegressionTests
{
    [Theory]
    [InlineData("2026-10-13T14:30:12.1234567-06:00", "2026-11-03")]
    [InlineData("2026-10-13T00:15:00+14:00", "2026-10-14")]
    [InlineData("2026-03-07T14:00:00-08:00", "2026-03-09")]
    public void ChangedDate_PreservesTimeAndOffsetRegardlessOfWorkstationZone(string stored, string date)
    {
        var original = DateTimeOffset.Parse(stored);
        var selected = DateTime.Parse(date);
        var actual = OpportunityEntryDialog.ApplyDeadlineDate(original, selected, TimeZoneInfo.Utc)!.Value;
        Assert.True(actual.Date == selected.Date && actual.TimeOfDay == original.TimeOfDay && actual.Offset == original.Offset,
            $"EXPECTED {selected:yyyy-MM-dd} with {original.TimeOfDay} {original.Offset}; ACTUAL {actual:O}");
        var untouched = OpportunityEntryDialog.ApplyDeadlineDate(original, original.Date, TimeZoneInfo.Utc)!.Value;
        Assert.True(original.EqualsExact(untouched), $"EXPECTED unchanged instant/offset; ACTUAL {untouched:O}");
    }

    [Fact]
    public void ClearAndNewDate_HaveExplicitSemantics()
    {
        var date = new DateTime(2026, 10, 13);
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test offset", TimeSpan.FromHours(-7), "Test", "Test");
        Assert.Null(OpportunityEntryDialog.ApplyDeadlineDate(DateTimeOffset.UtcNow, null, zone));
        Assert.Equal(new DateTimeOffset(date, TimeSpan.FromHours(-7)),
            OpportunityEntryDialog.ApplyDeadlineDate(null, date, zone));
    }

    [Fact]
    public void Dialog_LoadsStoredCalendarDate_AndSavesThroughPreservingHelper()
    {
        var source = File.ReadAllText(Path.Combine(XamlStaticResourceOrderTests.GetRepoRoot(),
            "Kor.Operations.App", "Opportunities", "OpportunityEntryDialog.xaml.cs"));
        Assert.True(source.Contains("DeadlineBox.SelectedDate = _existing.SubmissionDeadlineUtc?.Date;", StringComparison.Ordinal),
            "EXPECTED stored-offset calendar date in picker; ACTUAL loading differs");
        Assert.Contains("ApplyDeadlineDate(_existing?.SubmissionDeadlineUtc, DeadlineBox.SelectedDate, TimeZoneInfo.Local)", source);
        Assert.DoesNotContain("SubmissionDeadlineUtc?.LocalDateTime", source);
    }
}

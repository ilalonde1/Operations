#nullable enable
using Kor.Operations.FileSync.Service.Jobs.Shared;
using Xunit;

namespace Kor.Operations.FileSync.Service.Tests;

// The field-review round trip's one rule, pinned at the code both runners call:
//
//     a report leaves SharePoint only after a NAMED engineer acknowledged it,
//     and acknowledgement is a note we dropped that they deleted -- never the
//     absence of a note.
//
// The four folder states below are the real ones from 2026-09-17, when ~70
// un-initialled reports a month had been going to the server since June.
//
// WHAT THIS COVERS: the CSV cell cleaning (the tab), surname -> folder
// resolution (single- and multi-word), and the sweep decision for every
// combination of {CatchAll, note recorded or not, note present or gone}.
//
// WHAT IT DOES NOT COVER: the Graph plumbing (that the note actually landed,
// that the listing is complete), the SQL round trip of FileSync.EorControlFiles,
// and -- the same-class fault it cannot see -- a WRONG row in EOR.csv: a report
// routed to the wrong engineer, who acknowledges it, is swept legitimately.
// Only a person reading the 1st-of-month CatchAll report, or the engineer
// noticing a stranger's project in their folder, catches that one.
public sealed class EorRoutingTests
{
    private static readonly string[] Folders =
    {
        "CatchAll", "Conor Murtagh", "Ian Lalonde", "Jim DesRoches", "John Markulin",
        "Kevin Wurmlinger", "Omar Alcazar Pastrana", "Rory Beirne",
    };

    // --- EOR.csv ----------------------------------------------------------------

    [Theory]
    [InlineData("\"\tWurmlinger\"", "Wurmlinger")]   // the defect: a tab INSIDE the quotes, 60 of 238 rows
    [InlineData("\" Beirne \"", "Beirne")]
    [InlineData("Markulin", "Markulin")]
    [InlineData("\"Alcazar Pastrana\"", "Alcazar Pastrana")]
    [InlineData("﻿ProjectNumber", "ProjectNumber")]  // a BOM on the header must not blank the whole map
    public void Csv_cell_is_cleaned_inside_the_quotes_too(string raw, string expected)
        => Assert.Equal(expected, EorRouting.CleanCsvField(raw));

    [Fact]
    public void Real_csv_shape_parses_with_tabs_removed()
    {
        var csv = "ProjectNumber,EOR\r\n01668-01,\"\tWurmlinger\"\r\n01657-01,Markulin\r\n31039-01,\"\tAlcazar Pastrana\"\r\n\r\n";
        var map = EorRouting.ParseEorCsv(csv);
        Assert.Equal(3, map.Count);
        Assert.Equal("Wurmlinger", map["01668-01"]);
        Assert.Equal("Markulin", map["01657-01"]);
        Assert.Equal("Alcazar Pastrana", map["31039-01"]);
    }

    // --- surname -> folder ------------------------------------------------------

    [Theory]
    [InlineData("Wurmlinger", "Kevin Wurmlinger")]
    [InlineData("markulin", "John Markulin")]
    [InlineData("Alcazar Pastrana", "Omar Alcazar Pastrana")]   // multi-word: never matched before
    [InlineData("Omar Alcazar Pastrana", "Omar Alcazar Pastrana")]
    [InlineData("DesRoches", "Jim DesRoches")]
    public void A_surname_in_the_csv_finds_its_folder(string csvName, string expectedFolder)
    {
        var map = new Dictionary<string, string> { ["31000-01"] = csvName };
        var r = EorRouting.ResolveEor("31000-01", map, Folders, "CatchAll");
        Assert.Equal(expectedFolder, r.Folder);
        Assert.Equal(EorRouting.EorReason.Matched, r.Reason);
        Assert.False(r.IsCatchAll);
    }

    [Fact]
    public void A_name_with_no_folder_goes_to_catch_all_and_says_why()
    {
        var map = new Dictionary<string, string> { ["30010-18"] = "Zickmantel", ["60058-05"] = "  " };
        var z = EorRouting.ResolveEor("30010-18", map, Folders, "CatchAll");
        Assert.Equal("CatchAll", z.Folder);
        Assert.Equal(EorRouting.EorReason.NoFolderForName, z.Reason);
        Assert.Equal("Zickmantel", z.CsvName);

        var blank = EorRouting.ResolveEor("60058-05", map, Folders, "CatchAll");
        Assert.Equal(EorRouting.EorReason.EmptyName, blank.Reason);

        var missing = EorRouting.ResolveEor("31199-01", map, Folders, "CatchAll");
        Assert.Equal("CatchAll", missing.Folder);
        Assert.Equal(EorRouting.EorReason.NotInCsv, missing.Reason);
        Assert.Null(missing.CsvName);
    }

    [Fact]
    public void The_csv_can_never_route_to_catch_all_by_name()
    {
        // Someone typing "CatchAll" as an EOR is not an acknowledgement path.
        var map = new Dictionary<string, string> { ["31000-01"] = "CatchAll" };
        var r = EorRouting.ResolveEor("31000-01", map, Folders, "CatchAll");
        Assert.Equal(EorRouting.EorReason.NoFolderForName, r.Reason);
    }

    // --- the 5th: sweep or not ----------------------------------------------------

    private const string SeptNote = "Acknowledge and Move To Server September.txt";
    private static readonly IReadOnlyDictionary<string, string> SeptPending =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["John Markulin"] = SeptNote };

    [Fact]
    public void Catch_all_is_never_swept_even_with_no_note()
    {
        // June-Sept 2026: 69 / 73 / 84 / 68 files a month left this way.
        var d = EorRouting.DecideSweep("CatchAll", "CatchAll", SeptPending, new[] { "31198-01 CRM 2026-08-20 Report 01.pdf" });
        Assert.Equal(EorRouting.SweepAction.SkipCatchAll, d.Action);
    }

    [Fact]
    public void A_folder_that_was_not_given_a_note_this_period_is_not_swept()
    {
        // Kevin, 2026-09-17: four reports he re-filed himself, no note ever dropped (tab in EOR.csv).
        var d = EorRouting.DecideSweep("Kevin Wurmlinger", "CatchAll", SeptPending, new[] { "31083-01 CRM 2026-08-26 Report 01.pdf" });
        Assert.Equal(EorRouting.SweepAction.SkipNoBatch, d.Action);
    }

    [Fact]
    public void A_stale_note_from_another_month_does_not_count_either_way()
    {
        // Rory, 2026-09-17: 12 reports and "…May.txt" still there. No September
        // record -> nothing was asked of her this period -> leave it alone.
        var d = EorRouting.DecideSweep("Rory Beirne", "CatchAll", SeptPending,
            new[] { "Acknowledge and Move To Server May.txt", "31065-01 CRM 2026-08-05 Report 01.pdf" });
        Assert.Equal(EorRouting.SweepAction.SkipNoBatch, d.Action);
    }

    [Fact]
    public void A_note_still_in_the_folder_means_not_acknowledged()
    {
        var d = EorRouting.DecideSweep("John Markulin", "CatchAll", SeptPending,
            new[] { SeptNote, "31130-01 CRM 2026-08-30 Report 03.pdf" });
        Assert.Equal(EorRouting.SweepAction.SkipNotAcked, d.Action);
        Assert.Equal(SeptNote, d.ControlFileName);
    }

    [Fact]
    public void A_recorded_note_that_is_gone_is_the_only_thing_that_sweeps()
    {
        var d = EorRouting.DecideSweep("john markulin", "CatchAll", SeptPending,
            new[] { "31130-01 CRM 2026-08-30 Report 03.pdf" });
        Assert.Equal(EorRouting.SweepAction.Sweep, d.Action);
        Assert.Equal(SeptNote, d.ControlFileName);
    }

    [Fact]
    public void With_no_records_for_the_period_nothing_sweeps_anywhere()
    {
        // The state on any day before the fix's first 1st-of-month run, and
        // the state if the SQL insert failed: every folder must be left alone.
        var none = new Dictionary<string, string>();
        foreach (var folder in Folders)
        {
            var d = EorRouting.DecideSweep(folder, "CatchAll", none, new[] { "31000-01 CRM 2026-09-01 Report 01.pdf" });
            Assert.NotEqual(EorRouting.SweepAction.Sweep, d.Action);
        }
    }

    [Fact]
    public void Period_key_sorts_and_is_not_the_audit_tag()
        => Assert.Equal("2026-09", EorRouting.PeriodKey(new DateTimeOffset(2026, 9, 5, 8, 0, 0, TimeSpan.FromHours(-7))));
}

#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.Intake;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A live set's wall elevations against the engineer's own model, both resolved on the share by
/// NAME (job number, "05 Stickfile", the reference's file name) — never a path in a test. When the
/// share is unreachable it returns without asserting, which xUnit reports as a pass that proved
/// nothing (the convention every live class here follows); on the network it fails when a reader
/// starts disagreeing with the model, so that is caught here and not in ETABS.
/// </summary>
/// <remarks>
/// SLOW: one PDF and one .e2k read off the share through DrawingMirror. Banked 2026-09-08: 31168
/// states 22 storeys on its 4 section/elevation sheets, 20 match the model by both level names and
/// 20 of those are within 5 mm; the two unmatched are the drawings' LEVEL 1 against A-LEVEL 1 /
/// B-LEVEL 1. Re-banked 2026-09-09 (intake step 25, every ladder column read): 67 storeys stated,
/// 61 match the model by both names, 60 of those within 25 mm, and ONE disagrees — C-L9 over C-L8,
/// the drawing 3,202 mm and her model 3,502 — tower C's top storey, read for the first time. That
/// is a question for the engineer, not a fault to silence: the floor is 60 matched and no
/// disagreement but that one, named.
/// WHAT IT DOES NOT COVER: a second live set (31130's reference is not on the share under a
/// name this test knows), the unmatched pairs' correctness, and which of the two is right about
/// C-LEVEL 9.
/// </remarks>
[Trait("Speed", "Slow")]
public sealed class TheLiveSetsStoreysAgreeWithTheirModelTests
{
    [Fact]
    public void Langara31168StatesItsStoreysAsItsModelHasThem()
    {
        if (!LiveProjects.ShareReachable) return;   // off the network: nothing to compare, nothing to claim
        string stickFolder = LiveProjects.Folder("31168", "05 Stickfile");
        // the office names a stick file "<job> - <yyyy-MM-dd>- <project> - Stickfile….pdf"; the newest date is the current set
        var dated = Directory.EnumerateFiles(stickFolder, "31168-01 - *.pdf", SearchOption.TopDirectoryOnly)
            .Select(f => (File: f, Date: System.Text.RegularExpressions.Regex.Match(Path.GetFileName(f), @"^31168-01 - (\d{4}-\d{2}-\d{2})").Groups[1].Value))
            .Where(t => t.Date.Length > 0)
            .OrderByDescending(t => t.Date, StringComparer.Ordinal).ThenBy(t => t.File, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Assert.True(dated.Count > 0, $"no dated stick file PDF under {stickFolder}");
        string pdf = DrawingMirror.SingleFile(dated[0].File);
        string reference = DrawingMirror.SingleFile(LiveProjects.File("31168", "31168-reference.e2k"));

        var table = SetStoreys.Read(pdf);
        var doc = E2kDocument.Load(reference);
        var result = StoreyAgreement.Compare(table, doc.ReadStories(), doc.LengthUnitInInches() ?? 1.0);

        Assert.True(result.Matched >= 60, result.Summary());
        // the one disagreement the drawings and the model have, tower C's top storey (step 25);
        // any other is new and fails here
        Assert.True(result.Off.Count <= 1, result.Summary());
        Assert.True(result.Off.Count == 0 || result.Summary().Contains("C-L9->C-L8", StringComparison.Ordinal), result.Summary());
        Assert.True(table.SheetsWithStoreys >= 3, result.Summary());
    }
}

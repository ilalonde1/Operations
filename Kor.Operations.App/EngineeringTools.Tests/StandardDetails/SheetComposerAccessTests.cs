using System;
using Kor.Operations.StandardDetails;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.StandardDetails;

/// <summary>
/// Covers member/publisher output permissions, busy-state restoration, the sheet-name requirement on
/// both outputs, and a personal PDF with no sheet number. Does not cover role lookup, WPF events,
/// AUTHORING occupancy, or sheet numbering.
/// A same-class fault this would not catch: Save_Click invoking ComposeAsync without its permission guard.
/// </summary>
public sealed class SheetComposerAccessTests
{
    [Theory]
    //          canPublish busy   hasName  save   openPdf personal
    [InlineData(false,     false, true,    false, false,  true)]
    [InlineData(true,      false, true,    true,  true,   true)]
    [InlineData(false,     true,  true,    false, false,  false)]
    [InlineData(true,      true,  true,    false, false,  false)]
    // No name: neither output, for anyone. Open PDF opens an already-saved sheet, so it does not need one.
    [InlineData(false,     false, false,   false, false,  false)]
    [InlineData(true,      false, false,   false, true,   false)]
    public void Outputs_need_a_name_and_governed_outputs_need_publish_permission(
        bool canPublish, bool busy, bool hasSheetName, bool saveEnabled, bool openPdfEnabled, bool personalEnabled)
    {
        var actions = SheetComposerWindow.GetActionStates(canPublish, busy, hasSheetName);

        Assert.Equal(saveEnabled, actions.SaveToMaster);
        Assert.Equal(openPdfEnabled, actions.OpenPdf);
        Assert.Equal(personalEnabled, actions.CreatePdfSheet);
    }

    [Fact]
    public void Personal_pdf_accepts_an_empty_sheet_number()
    {
        var spec = new CustomSheetSpec(914.4, 609.6, "", "Personal details", "member",
            new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc),
            new[] { new CustomSheetPlacementSpec("KOR-D-00001", null, 20, 20, 155, 100) });

        Assert.NotEmpty(CustomPdfSheetComposer.Build(spec));
    }
}

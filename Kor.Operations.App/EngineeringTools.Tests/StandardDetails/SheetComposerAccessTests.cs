using System;
using Kor.Operations.StandardDetails;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.StandardDetails;

/// <summary>
/// Covers member/publisher output permissions, busy-state restoration, and a personal PDF with no
/// sheet number. Does not cover role lookup, WPF events, AUTHORING occupancy, or sheet numbering.
/// A same-class fault this would not catch: Save_Click invoking ComposeAsync without its permission guard.
/// </summary>
public sealed class SheetComposerAccessTests
{
    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(false, true, false, false)]
    [InlineData(true, true, false, false)]
    public void Personal_pdf_is_available_to_members_but_governed_outputs_require_publish_permission(
        bool canPublish, bool busy, bool governedEnabled, bool personalEnabled)
    {
        var actions = SheetComposerWindow.GetActionStates(canPublish, busy);

        Assert.Equal(governedEnabled, actions.SaveToMaster);
        Assert.Equal(governedEnabled, actions.OpenPdf);
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

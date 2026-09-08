using System.Text.Json;
using Kor.Operations.StandardDetails;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.StandardDetails;

/// <summary>
/// The publish-capture matcher: which exported PDF belongs to which detail.
///
/// Covers: identity matching by elementId / id / key / detailNumber; refusal of another view's lone
/// export (the 2026-09-07 ship-blocker); the identity-less lone item on a single-view request; the
/// top-level pdf fallback; the failure reason surfaced from the bridge's <c>failures</c> list.
///
/// Does NOT cover: the bridge itself, file existence on disk, SetRenderedPdf, batching, or what the
/// caller does with a failure. A same-class fault it would NOT catch: two success items that both name
/// the same detail (a duplicate key), where the first one wins silently.
/// </summary>
public sealed class MasterPublisherPdfMatchingTests
{
    private static JsonElement Reply(string json) => JsonDocument.Parse(json).RootElement.Clone();

    // The bridge's reply shape (BridgeExec.ExportViews) for a three-view batch where only KOR-D-00010 exported.
    private const string OneSuccessTwoFailures = """
        {
          "views": [
            { "elementId": 10, "viewName": "A", "viewType": "DraftingView", "key": "KOR-D-00010", "pdf": "X:\\exports\\KOR-D-00010.pdf", "exists": true }
          ],
          "failures": [
            { "index": 1, "elementId": 20, "key": "KOR-D-00020", "reason": "id 20 (B) is not printable/exportable." },
            { "index": 2, "elementId": 30, "key": "KOR-D-00030", "reason": "id 30 does not exist in this document." }
          ],
          "exported": 1,
          "failed": 2
        }
        """;

    [Fact]
    public void The_one_success_resolves_for_its_own_detail()
    {
        var reply = Reply(OneSuccessTwoFailures);

        Assert.True(MasterPublisher.TryResolveExportedViewPdf(reply, 10, "KOR-D-00010", singleViewRequest: false, out var pdf, out _));
        Assert.EndsWith("KOR-D-00010.pdf", pdf);
    }

    [Fact]
    public void A_failed_detail_never_receives_another_details_pdf()
    {
        // The ship-blocker: before the fix the lone success was accepted for 00020 and 00030 as well,
        // and SetRenderedPdf stored A's drawing under three detail numbers.
        var reply = Reply(OneSuccessTwoFailures);

        Assert.False(MasterPublisher.TryResolveExportedViewPdf(reply, 20, "KOR-D-00020", singleViewRequest: false, out var pdf20, out var error20));
        Assert.Empty(pdf20);
        Assert.Contains("not printable", error20);

        Assert.False(MasterPublisher.TryResolveExportedViewPdf(reply, 30, "KOR-D-00030", singleViewRequest: false, out var pdf30, out var error30));
        Assert.Empty(pdf30);
        Assert.Contains("does not exist", error30);
    }

    [Fact]
    public void A_lone_item_naming_another_view_is_refused_even_on_a_single_view_request()
    {
        var reply = Reply(OneSuccessTwoFailures);

        Assert.False(MasterPublisher.TryResolveExportedViewPdf(reply, 20, "KOR-D-00020", singleViewRequest: true, out var pdf, out _));
        Assert.Empty(pdf);
    }

    [Fact]
    public void A_lone_item_with_no_identity_is_accepted_only_for_a_single_view_request()
    {
        var reply = Reply("""{ "views": [ { "pdf": "X:\\exports\\only.pdf", "exists": true } ] }""");

        Assert.True(MasterPublisher.TryResolveExportedViewPdf(reply, 10, "KOR-D-00010", singleViewRequest: true, out var pdf, out _));
        Assert.EndsWith("only.pdf", pdf);

        Assert.False(MasterPublisher.TryResolveExportedViewPdf(reply, 10, "KOR-D-00010", singleViewRequest: false, out _, out _));
    }

    [Fact]
    public void Matches_by_the_bridges_elementId_when_no_key_is_echoed()
    {
        var reply = Reply("""{ "views": [ { "elementId": 10, "pdf": "X:\\exports\\10.pdf", "exists": true }, { "elementId": 20, "pdf": "X:\\exports\\20.pdf", "exists": true } ] }""");

        Assert.True(MasterPublisher.TryResolveExportedViewPdf(reply, 20, "KOR-D-00020", singleViewRequest: false, out var pdf, out _));
        Assert.EndsWith("20.pdf", pdf);
    }

    [Fact]
    public void A_matched_item_whose_file_is_missing_is_not_a_pdf()
    {
        var reply = Reply("""{ "views": [ { "elementId": 10, "key": "KOR-D-00010", "pdf": "X:\\exports\\KOR-D-00010.pdf", "exists": false } ] }""");

        Assert.False(MasterPublisher.TryResolveExportedViewPdf(reply, 10, "KOR-D-00010", singleViewRequest: true, out _, out var error));
        Assert.Contains("did not export", error);
    }

    [Fact]
    public void Top_level_pdf_is_only_trusted_for_a_single_view_request()
    {
        var reply = Reply("""{ "pdf": "X:\\exports\\whole.pdf" }""");

        Assert.True(MasterPublisher.TryResolveExportedViewPdf(reply, 10, "KOR-D-00010", singleViewRequest: true, out var pdf, out _));
        Assert.EndsWith("whole.pdf", pdf);

        Assert.False(MasterPublisher.TryResolveExportedViewPdf(reply, 10, "KOR-D-00010", singleViewRequest: false, out _, out _));
    }
}

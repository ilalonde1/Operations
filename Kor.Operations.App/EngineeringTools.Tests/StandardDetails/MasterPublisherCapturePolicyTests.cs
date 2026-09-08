using System;
using System.Linq;
using Kor.Operations.StandardDetails;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.StandardDetails;

/// <summary>
/// Covers capture refusal by default, explicit override, incomplete results, and all failure details
/// in the UI summary. Does not cover the bridge, PDF writes, or the live confirmation dialog.
/// A same-class fault this would not catch: PublishAsync calling the policy after replacing MASTER.
/// </summary>
public sealed class MasterPublisherCapturePolicyTests
{
    [Fact]
    public void Capture_failure_refuses_publish_and_names_the_detail_and_reason()
    {
        var capture = new MasterPublishPdfCaptureResult(2, 1, 0, 1,
            new[] { "KOR-D-00020: view is not printable" });

        var error = Assert.Throws<MasterPublishPdfCaptureException>(() => capture.EnsurePublishAllowed());

        Assert.Contains("KOR-D-00020: view is not printable", error.Message);
        Assert.Contains("MASTER was not replaced", error.Message);
    }

    [Fact]
    public void Override_keeps_result_incomplete_and_summary_includes_failures_beyond_twenty()
    {
        var failures = Enumerable.Range(1, 25).Select(i => $"KOR-D-{i:D5}: export failed").ToArray();
        var capture = new MasterPublishPdfCaptureResult(25, 0, 0, 25, failures);

        capture.EnsurePublishAllowed(allowPdfCaptureFailures: true);
        var result = Result(capture);

        Assert.False(result.PdfCaptureComplete);
        Assert.Same(failures, result.PdfCapture.Failures);
        var summary = StandardDetailsWindow.BuildMasterPublishSummary(result);
        Assert.Contains("PDF capture complete: False", summary);
        Assert.All(failures, failure => Assert.Contains(failure, summary));
    }

    [Fact]
    public void Successful_capture_needs_no_override()
    {
        var capture = new MasterPublishPdfCaptureResult(2, 1, 1, 0, Array.Empty<string>());

        capture.EnsurePublishAllowed();

        Assert.True(Result(capture).PdfCaptureComplete);
    }

    private static MasterPublishResult Result(MasterPublishPdfCaptureResult capture)
        => new(2, 2, 2, Array.Empty<MasterPublishRemovedView>(), Array.Empty<string>(), capture, Verified: true);
}

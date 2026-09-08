#nullable enable
using Kor.Operations.FileSync.Service.Jobs.Watcher;
using Xunit;

namespace Kor.Operations.FileSync.Service.Tests;

// Unit tests for the pure name sanitizer. This is the exact rule SharePoint
// enforces and NTFS does not, so it is where the ' 31056-01 ... .pdf' failure
// of 2026-09-08 (Graph "Invalid request" on a leading-space name) is pinned.
public sealed class SharePointNameTests
{
    [Theory]
    // The real defect: a leading space.
    [InlineData(" 31056-01 2026-08-21 10th & Highbury Issued for Draft IFC.pdf",
                "31056-01 2026-08-21 10th & Highbury Issued for Draft IFC.pdf")]
    [InlineData(" leading.pdf", "leading.pdf")]
    [InlineData("trailing.pdf ", "trailing.pdf")]
    [InlineData("\tboth sides \t", "both sides")]
    [InlineData("trailing-dot.", "trailing-dot")]
    [InlineData("dots and space. . ", "dots and space")]
    public void Illegal_names_are_trimmed_to_what_SharePoint_stores(string input, string expected)
    {
        Assert.Equal(expected, SharePointName.Sanitize(input));
        Assert.False(SharePointName.IsLegal(input));
    }

    [Theory]
    [InlineData("clean.pdf")]
    [InlineData("31056-01 2026-09-08 10th & Highbury Issued for Draft IFC.pdf")]
    [InlineData("has spaces inside.pdf")]     // interior spaces are fine
    [InlineData(".gitignore")]                 // a leading dot is legal in SharePoint
    [InlineData("v1.2.3 report.pdf")]          // interior dots are fine
    public void Legal_names_are_returned_unchanged(string input)
    {
        Assert.Equal(input, SharePointName.Sanitize(input));
        Assert.True(SharePointName.IsLegal(input));
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("...")]
    [InlineData(". . .")]
    public void Names_that_are_only_whitespace_or_dots_reduce_to_empty(string input)
    {
        Assert.Equal(string.Empty, SharePointName.Sanitize(input));
    }
}

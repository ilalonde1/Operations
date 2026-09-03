#nullable enable
using Kor.Opportunities.Worker.Services.Research;
using Xunit;

namespace Kor.Operations.App.Tests.Research;

/// <summary>
/// The Continuum regression. On 2026-09-03 canonical 74300 held both a Denver
/// developer and a Victoria BC architecture practice; a refresh researched the
/// Denver firm and replaced the Victoria record in place, with no error and no
/// history to recover from. These assert the gate that now stands in the way.
/// </summary>
public sealed class ResearchIdentityGateTests
{
    private static string Result(string? website, bool? matches)
    {
        var w = website is null ? "null" : $"\"{website}\"";
        var m = matches is null ? "null" : (matches.Value ? "true" : "false");
        return $"{{\"displayName\":\"x\",\"entityWebsite\":{w},\"entityMatchesRecord\":{m}}}";
    }

    [Theory]
    [InlineData("https://www.perkinswill.com/", "perkinswill.com")]
    [InlineData("perkinswill.com", "perkinswill.com")]
    [InlineData("WWW.PerkinsWill.COM", "perkinswill.com")]
    [InlineData("https://continuumarchitecture.ca/legacy", "continuumarchitecture.ca")]
    [InlineData("  https://continuumpartners.com  ", "continuumpartners.com")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    public void NormalizeHost_HandlesUrlsDomainsCasingAndBlanks(string? input, string? expected)
    {
        Assert.Equal(expected, ResearchIdentityGate.NormalizeHost(input));
    }

    [Fact]
    public void Blocks_the_Continuum_case_researched_a_different_company()
    {
        // Anchored to the Victoria architects; researcher came back with Denver.
        var d = ResearchIdentityGate.Evaluate(
            "continuumarchitecture.ca",
            Result("https://continuumpartners.com/", matches: null));

        Assert.False(d.Allow);
        Assert.Contains("does not match", d.Reason);
        Assert.Equal("continuumpartners.com", d.ResearchedHost);
    }

    [Fact]
    public void Blocks_when_the_researcher_says_it_could_not_confirm_the_entity()
    {
        var d = ResearchIdentityGate.Evaluate(
            "continuumarchitecture.ca",
            Result("https://continuumarchitecture.ca/", matches: false));

        Assert.False(d.Allow);
        Assert.Contains("could not confirm", d.Reason);
    }

    [Fact]
    public void Allows_when_the_researched_host_matches_the_anchor()
    {
        var d = ResearchIdentityGate.Evaluate(
            "perkinswill.com",
            Result("https://www.perkinswill.com/", matches: true));

        Assert.True(d.Allow);
        Assert.Null(d.Reason);
        Assert.Equal("perkinswill.com", d.ResearchedHost);
    }

    [Fact]
    public void Anchorless_org_is_allowed_and_reports_a_host_to_backfill()
    {
        // 7,200 of 9,695 active orgs had no anchor. Blocking them all would stop
        // the platform working; instead the discovered host is handed back so the
        // org acquires an anchor and the NEXT refresh is comparable.
        var d = ResearchIdentityGate.Evaluate(
            anchorHost: null,
            Result("https://continuumarchitecture.ca/", matches: true));

        Assert.True(d.Allow);
        Assert.Equal("continuumarchitecture.ca", d.ResearchedHost);
    }

    [Fact]
    public void Allows_when_the_researcher_returned_no_website_at_all()
    {
        // No claim made means nothing to contradict -- the gate must not invent a
        // failure, or every provider that omits the field would stop working.
        var d = ResearchIdentityGate.Evaluate("perkinswill.com", Result(null, matches: null));

        Assert.True(d.Allow);
        Assert.Null(d.ResearchedHost);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("")]
    [InlineData(null)]
    public void Malformed_or_missing_output_is_not_the_gates_failure_mode(string? json)
    {
        // Unparseable output fails downstream on its own terms; the gate must not
        // add a second failure mode for it.
        var d = ResearchIdentityGate.Evaluate("perkinswill.com", json);

        Assert.True(d.Allow);
    }

    [Fact]
    public void Subdomain_is_treated_as_a_different_host()
    {
        // Deliberate: "careers.example.com" vs "example.com" is a weak signal that
        // the researcher landed somewhere adjacent. Flag it rather than assume.
        var d = ResearchIdentityGate.Evaluate("example.com", Result("https://careers.example.com/", matches: null));

        Assert.False(d.Allow);
    }
}

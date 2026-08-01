#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Kor.Opportunities.Data.BdReports.Generators;

public static class AwardProgramsReportGenerator
{
    public static BdReportDocument Build(IReadOnlyList<AwardProgramReportRow> awards, DateTimeOffset generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(awards);

        var b = new BdReportDocumentBuilder("KOR Structural - Industry Awards Finder");
        b.Italic(
            "Read-only catalog of AEC industry award programs KOR could enter for recognition. " +
            "This is separate from contract-award/bid-result intelligence and does not draft or submit applications. " +
            $"Generated {generatedAtUtc:yyyy-MM-dd HH:mm} UTC from opportunities.AwardProgram.");

        b.H2("Upcoming award programs");
        if (awards.Count == 0)
        {
            b.P("No upcoming award programs are currently cataloged. The Worker discovery job will populate this list after its next successful run.");
            return b.Build();
        }

        b.Table(
            new[] { "Deadline", "Awarding body", "Program", "Region", "Discipline", "Category", "Light KOR-project match" },
            awards.Select(a => (IReadOnlyList<string>)new[]
            {
                a.SubmissionDeadline?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "not posted",
                Safe(a.AwardingBody, 34) ?? string.Empty,
                Safe(a.ProgramName, 42) ?? string.Empty,
                a.Region ?? string.Empty,
                a.Discipline ?? string.Empty,
                Safe(a.Category, 28) ?? string.Empty,
                a.MatchProjectName is null ? string.Empty : Safe(a.MatchProjectName, 46) ?? string.Empty,
            }).ToList(),
            null,
            awards.Select(a => (IReadOnlyList<string?>)new[]
            {
                null,
                null,
                null,
                null,
                null,
                null,
                a.MatchMpiId is { } id ? KorReportLinks.Mpi(id) : null,
            }).ToList());

        b.H2("Eligibility notes");
        foreach (var award in awards.Take(12))
        {
            b.H3($"{award.AwardingBody} - {award.ProgramName}");
            if (!string.IsNullOrWhiteSpace(award.EligibilitySummary))
            {
                b.P(award.EligibilitySummary!);
            }

            if (!string.IsNullOrWhiteSpace(award.EntryFee))
            {
                b.B("Entry fee: ", award.EntryFee!);
            }

            if (!string.IsNullOrWhiteSpace(award.Url))
            {
                b.B("Source: ", award.Url!);
            }
        }

        b.H2("Methodology");
        b.P("Award programs are discovered by the AwardProgramFinderJob using the BD research executor web-search path, then upserted by NaturalKey (awarding body + program + cycle year). Rows are ordered by upcoming submission deadline; null deadlines are retained for recurring programs whose current cycle has not posted.");
        b.Italic("Light KOR-project match is only a region/discipline hint from active MajorProjectsInventory, not an application recommendation.");
        return b.Build();
    }

    private static string? Safe(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= max ? value : value[..max];
    }
}

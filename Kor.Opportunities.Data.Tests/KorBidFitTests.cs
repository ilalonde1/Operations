using System.IO;
using Kor.Opportunities.Data.Ingestion;
using Xunit;

namespace Kor.Opportunities.Data.Tests;

// Measures the tender rule against all 581 open tenders of 2026-10-20, each labelled by hand as KOR work or not.
// Compares the rule's verdict on the title with the label; it does not see the description, the buyer or the documents,
// and because the rule was tuned on this same set it says nothing about titles unlike these. Floors are the measured
// figures (precision 64/67, recall 64/68): a change that lets in road work or drops consultant work fails here.
public sealed class KorBidFitTests
{
    private static IEnumerable<(long Id, bool Ours, string Title)> Labels()
    {
        using var stream = typeof(KorBidFitTests).Assembly.GetManifestResourceStream("Kor.Opportunities.Data.Tests.Fixtures.KorBidFitLabels-2026-10-20.tsv")!;
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith('#') || line.Length == 0) continue;
            var p = line.Split('\t');
            yield return (long.Parse(p[0]), p[1] == "Y", p[2]);
        }
    }

    [Fact]
    public void Shows_KOR_work_and_keeps_road_utility_and_supply_tenders_out()
    {
        var rows = Labels().ToList();
        Assert.Equal(581, rows.Count);
        var shown = rows.Where(r => KorBidFit.IsKorWork(r.Title)).ToList();
        var right = shown.Count(r => r.Ours);
        var wrong = shown.Where(r => !r.Ours).Select(r => $"{r.Id} {r.Title}").ToList();
        var missed = rows.Where(r => r.Ours && !KorBidFit.IsKorWork(r.Title)).Select(r => $"{r.Id} {r.Title}").ToList();
        Assert.True(wrong.Count <= 3, "Shown but not KOR work:\n" + string.Join("\n", wrong));
        Assert.True(missed.Count <= 4, "KOR work the rule misses:\n" + string.Join("\n", missed));
        Assert.Equal(64, right);
    }

    [Theory]
    [InlineData("26-75-INF Kalamalka Lake Road Construction (Vernon (City))", false)]
    [InlineData("12875 Construction - Watermain Upgrade - Jaud Rd", false)]
    [InlineData("Civil Engineering Consulting Services - Mountain Highway Improvements", false)]
    [InlineData("RFPQ-Mechanical Consulting Services - Standing Opportunity", false)]
    [InlineData("Construction Management - Ecole North Oyster Elementary - Seismic Upgrade and Addition", false)]
    [InlineData("Structural and Civil Engineering – Professional Services Agreement", true)]
    [InlineData("Multi-disciplinary Architectural and Engineering Consultant Services", true)]
    [InlineData("Building Condition Assessment and Feasibility Study – Building B", true)]
    [InlineData("DESIGN BUILD | RFQ # R-27016 - 107TH STREET ELEMENTARY | SEISMIC MODERNIZATION PROJECT", true)]
    public void Reads_the_titles_Ian_and_the_leads_will_judge_it_by(string title, bool ours)
        => Assert.Equal(ours, KorBidFit.IsKorWork(title));
}

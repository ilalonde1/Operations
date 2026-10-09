#nullable enable
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Worker.Services;

internal sealed partial class DataHealthAuditJob
{
    // Section K: how much of the footprint has an early-signal feed at all.
    //
    // WHY IT IS IN THE WEEKLY REPORT AND NOT IN A SCRIPT. It WAS in a script --
    // `tools/BdSourceCoverage/coverage_gap.py`, written 2026-09-10, which measured this
    // properly: 56 in-footprint municipalities, 13 held, Metro Vancouver 4 of 21, Fraser
    // Valley 0 of 6, 2.58M people with no feed. It was promoted into `tools/`, and then
    // nobody ran it, so on 2026-10-08 the same gap was measured again from scratch and the
    // document was only found because Ian said "I'm POSITIVE we looked at this".
    // A gap in a document gets rediscovered. A gap in the report gets closed.
    //
    // ⛔ "HELD" IS DERIVED, NEVER STORED. coverage_gap.py hardcoded its held set as a
    //    python literal copied out of a live query, which is precisely why its number went
    //    stale while two more municipalities were wired. This joins the roster
    //    (migration 333) to OpportunitySources at report time.
    //
    // WHAT IT COMPARES: for each of the 56 footprint municipalities, whether an ENABLED
    // source exists whose name prefix matches -- SourceType 2, 20, 21 or 22, i.e. the
    // early-signal adapters plus the one GenericJson municipal feed (Nanaimo). Tender
    // sources are deliberately not coverage: `BidsTenders_PortMoody` existing is exactly
    // the illusion this section is meant to dispel.
    //
    // WHAT IT DOES NOT COVER, so nobody reads the heading and stops looking:
    //   * Whether a held source is PRODUCING. A municipality can be held and silent and
    //     still count here; §I and §J are what notice silence. "Held" means configured
    //     and enabled, nothing more.
    //   * Whether the feed is any good. Langford prices its permits and names nobody;
    //     Nanaimo's is a "What's Building" page, not an application feed. Both count as 1.
    //   * Quality of the match. It is a name-prefix join, so a source named for something
    //     other than its municipality is invisible to it.
    //   * Regional districts. The RDN feed is real coverage of electoral areas that have
    //     no municipality, and this section credits it to nobody.
    //   * Population is 2021 census and ranks the gap list. It is not market size.
    //   * ⚠ A same-class fault it would NOT catch: a municipality wired to the WRONG
    //     layer -- a "Development Permit AREA" zoning overlay ingests cleanly as nonsense
    //     and counts as held here. Only `tools/ArcGisProbe` catches that, before wiring.
    internal const string FootprintCoverageSql = """
        IF OBJECT_ID('opportunities.FootprintMunicipalities', 'U') IS NULL
            SELECT CAST(N'' AS nvarchar(40))  AS Region,
                   CAST(N'' AS nvarchar(80))  AS Name,
                   CAST(0 AS int)             AS Population2021,
                   CAST(NULL AS nvarchar(200)) AS HeldBy
            WHERE 1 = 0;
        ELSE
            SELECT m.Region, m.Name, m.Population2021, h.HeldBy
            FROM opportunities.FootprintMunicipalities m
            OUTER APPLY (
                SELECT TOP 1 s.Name AS HeldBy
                FROM opportunities.OpportunitySources s
                WHERE s.IsEnabled = 1
                  AND s.IsHistorical = 0
                  AND s.SourceType IN (2, 20, 21, 22)
                  AND LEFT(s.Name, CHARINDEX(N'_', s.Name + N'_') - 1) = m.SourceNamePrefix
                ORDER BY s.Name
            ) h
            ORDER BY m.Region, m.Population2021 DESC;
        """;

    private static FootprintCoverageRow ReadFootprintCoverageRow(SqlDataReader r) =>
        new(GetString(r, 0), GetString(r, 1), r.GetInt32(2),
            r.IsDBNull(3) ? null : r.GetString(3));

    internal static void AppendFootprintCoverage(StringBuilder sb, IReadOnlyList<FootprintCoverageRow> rows)
    {
        sb.AppendLine();
        sb.AppendLine("## K. Early-signal footprint coverage");

        if (rows.Count == 0)
        {
            sb.AppendLine("No footprint roster found — migration 333 has not run on this database. "
                          + "This section asserts nothing.");
            return;
        }

        var held = rows.Count(r => r.HeldBy is not null);
        long popNoFeed = rows.Where(r => r.HeldBy is null).Sum(r => (long)r.Population2021);
        long popAll = rows.Sum(r => (long)r.Population2021);

        sb.Append("**").Append(held.ToString(CultureInfo.InvariantCulture))
            .Append(" of ").Append(rows.Count.ToString(CultureInfo.InvariantCulture))
            .AppendLine("** in-footprint municipalities have an early-signal feed.");
        sb.Append(popNoFeed.ToString("N0", CultureInfo.InvariantCulture))
            .Append(" of ").Append(popAll.ToString("N0", CultureInfo.InvariantCulture))
            .AppendLine(" people live where we hold no application feed.");
        sb.AppendLine();
        sb.AppendLine("Held = an ENABLED source whose name prefix matches the municipality (SourceType 2/20/21/22). "
                      + "It does NOT mean the source is producing — that is §I and §J. Tender feeds are not coverage.");
        sb.AppendLine();

        sb.AppendLine("| Region | Held | Of | Population with no feed |");
        sb.AppendLine("|---|---:|---:|---:|");
        foreach (var g in rows.GroupBy(r => r.Region)
                              .OrderByDescending(g => g.Where(r => r.HeldBy is null)
                                                       .Sum(r => (long)r.Population2021)))
        {
            sb.Append("| ").Append(Md(g.Key))
                .Append(" | ").Append(g.Count(r => r.HeldBy is not null).ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(g.Count().ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(g.Where(r => r.HeldBy is null)
                                        .Sum(r => (long)r.Population2021)
                                        .ToString("N0", CultureInfo.InvariantCulture))
                .AppendLine(" |");
        }

        var gaps = rows.Where(r => r.HeldBy is null)
                       .OrderByDescending(r => r.Population2021)
                       .Take(12)
                       .ToList();
        if (gaps.Count > 0)
        {
            sb.AppendLine();
            sb.Append("Largest gaps (").Append(gaps.Count.ToString(CultureInfo.InvariantCulture))
                .Append(" of ").Append(rows.Count(r => r.HeldBy is null).ToString(CultureInfo.InvariantCulture))
                .AppendLine(" unheld):");
            sb.AppendLine();
            sb.AppendLine("| Municipality | Region | Population |");
            sb.AppendLine("|---|---|---:|");
            foreach (var r in gaps)
            {
                sb.Append("| ").Append(Md(r.Name))
                    .Append(" | ").Append(Md(r.Region))
                    .Append(" | ").Append(r.Population2021.ToString("N0", CultureInfo.InvariantCulture))
                    .AppendLine(" |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Coverage limits: counts configuration, not production, not feed quality, and not "
                      + "whether a municipality was wired to the right layer — a zoning-overlay layer "
                      + "ingests cleanly as nonsense and counts as held. Regional-district feeds are "
                      + "credited to no municipality. Population is 2021 census, for ranking only. "
                      + "The roster is 56 of BC's 160 municipalities, by footprint, from migration 333.");
    }

    internal sealed record FootprintCoverageRow(string Region, string Name, int Population2021, string? HeldBy);
}

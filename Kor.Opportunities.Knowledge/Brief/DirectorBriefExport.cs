using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Knowledge.Search;
using Kor.Opportunities.Knowledge.Store;

namespace Kor.Opportunities.Knowledge.Brief;

public sealed record DirectorBriefExportFile(DateOnly WeekOf, DateTimeOffset ExportedAtUtc, IReadOnlyList<ExportSection> Sections);
public sealed record ExportSection(string Section, string Question, string ClaimsQuery, string Facts,
    IReadOnlyList<OfferedClaim> Claims, IReadOnlyList<PriorCall> PriorCalls);
public sealed record OfferedClaim(long ClaimId, long PassageId, long SourceId, string SourceTitle, string Kind, string Statement, float Score);

/// <summary>Exports evidence only. The five answers are written outside the application.</summary>
public sealed class DirectorBriefExport
{
    private readonly Func<DirectorSection, CancellationToken, Task<string>> _facts;
    private readonly Func<string, int, CancellationToken, Task<IReadOnlyList<KnowledgeClaimHit>>> _search;
    private readonly Func<string, DateOnly, CancellationToken, Task<IReadOnlyList<PriorCall>>> _priorCalls;

    public DirectorBriefExport(DirectorFacts facts, KnowledgeSearch search, IDirectorBriefStore briefs)
        : this(facts.GetAsync, (query, take, ct) => search.SearchClaimsAsync(query, take, ct: ct), briefs.ListPriorCallsAsync) { }

    public DirectorBriefExport(Func<DirectorSection, CancellationToken, Task<string>> facts,
        Func<string, int, CancellationToken, Task<IReadOnlyList<KnowledgeClaimHit>>> search,
        Func<string, DateOnly, CancellationToken, Task<IReadOnlyList<PriorCall>>> priorCalls)
    { _facts = facts; _search = search; _priorCalls = priorCalls; }

    public async Task<DirectorBriefExportFile> ExportAsync(DateOnly weekOf, CancellationToken ct = default)
    {
        var exportedAt = DateTimeOffset.UtcNow;
        var sections = new List<ExportSection>();
        foreach (var section in Enum.GetValues<DirectorSection>())
        {
            var query = DirectorQuestions.ClaimsQuery(section);
            var facts = await _facts(section, ct);
            var claims = await _search(query, 12, ct);
            var priorCalls = await _priorCalls(section.ToString(), weekOf.AddDays(-28), ct);
            sections.Add(new(section.ToString(), DirectorQuestions.Question(section), query, facts,
                claims.Take(12).Select(c => new OfferedClaim(c.ClaimId, c.PassageId, c.SourceId, c.SourceTitle, c.Kind, c.Statement, c.Score)).ToArray(),
                priorCalls));
        }
        return new(weekOf, exportedAt, sections);
    }

    // Strictly next Sunday, even when the current Pacific day is Sunday.
    public static DateOnly NextSunday(DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time")).DateTime);
        var days = (7 - (int)today.DayOfWeek) % 7;
        return today.AddDays(days == 0 ? 7 : days);
    }
}

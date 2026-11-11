using System.Text;
using System.Text.RegularExpressions;
using Kor.Operations.Framework.Capture;

namespace Kor.Operations.Framework.Recall;

/// <summary>
/// Board row 2031 (RESET-1b, trap c): a JOB'S FIX LIST reads the open board rows that carry Ian's asks for that job, on EVERY owner's board.
/// Record #131883: row 1647 (Ian 4 Nov, cover 3D pictures from the HTML viewer) sat open 6 days because lanes work from the last handover's
/// fix list and neither it nor the blind look read open board rows; the brief to the 31218 lane shared no word with it, so word ranking
/// (<see cref="BrainRecall.Rank"/>) cannot bring it (BrainRecallTests.TrapC). This brings it by JOB, not by words:
///   1. A job is a 5-digit number in the moment's text that the open board treats as a job: some open row names it as NNNNN-NN (31218-01),
///      or two or more open rows name it (<see cref="JobsIn"/>; kor recall --job). THE HOOK (row 2042) takes only a job the moment WORKS
///      (<see cref="WorkedJobs"/>) and says only the rows naming it, with Ian's standing asks as one counted line (<see cref="HookText"/>).
///   2. NAMING THE JOB: every open row of ANY owner that names the job and quotes Ian (the word Ian in its What or DoneWhen).
///   3. IAN'S STANDING ASKS ON ITS BOARDS: the job's boards are the owners of the open rows naming it, less Framework and Ian (the
///      coordinator's rows are about how we work, and recall carries them by words); every open row of those owners that quotes Ian and does
///      NOT name the job and whose title names no other job (<see cref="TitleNamesAJob"/>) - an ask about the product (a cover, a sheet, a look) holds for every job it builds. Row 1647 is one: it names the
///      hotel, Halifax and 31180-01, never 31218.
/// Read from the recall <see cref="BrainRecall.Index"/> (the hook's local file, up to ~10 minutes old; the CLI's fresh from the database),
/// so it needs the postings of the job number and of "ian" loaded (<see cref="TermsFor"/>).
/// WHAT IT DOES NOT DO: an ask that does not say "Ian" (an engineer's ask relayed without the name) is not listed; an ask on a board that
/// has no open row naming the job is not listed (a new product with no job row yet); a 5-digit number that is not a job but sits in two
/// open rows (a budget, a port) is taken as one. Same-class miss it would NOT catch: Ian's ask recorded only as a brain.Memory decision,
/// never as a board row (records are recall's, by words).
/// </summary>
public static class JobAsks
{
    public const string IanTerm = "ian";
    public const int LeadChars = 160;
    private static readonly Regex FiveDigits = new(@"(?<![\p{L}\p{N}])(\d{5})(?![\p{L}\p{N}])", RegexOptions.CultureInvariant);

    /// <summary>The 5-digit numbers in a text, in order, once each (candidates; <see cref="JobsIn"/> decides).</summary>
    public static List<string> Candidates(string text) =>
        FiveDigits.Matches(text ?? "").Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>The terms the index must hold postings for: each job and "ian" (none when there is no job).</summary>
    public static HashSet<string> TermsFor(IEnumerable<string> jobs)
    {
        var t = jobs.ToHashSet(StringComparer.Ordinal);
        if (t.Count > 0) t.Add(IanTerm);
        return t;
    }

    private static List<int> BoardDocsWith(BrainRecall.Index ix, string term) =>
        ix.Postings.TryGetValue(term, out var list) ? list.Select(p => p.Doc).Where(d => ix.Docs[d].Board).Distinct().ToList() : new List<int>();

    /// <summary>The candidates the open board treats as jobs (rule 1).</summary>
    public static List<string> JobsIn(string text, BrainRecall.Index ix) =>
        Candidates(text).Where(n =>
        {
            var docs = BoardDocsWith(ix, n);
            return docs.Count >= 2 || docs.Any(d => Regex.IsMatch(ix.Docs[d].Lead, $@"(?<!\d){n}-\d{{2}}(?!\d)"));
        }).ToList();

    public sealed record FixList(string Job, IReadOnlyList<BrainRecall.Doc> Naming, IReadOnlyList<string> Boards, IReadOnlyList<BrainRecall.Doc> Standing);

    /// <summary>Rules 2 and 3 for one job.</summary>
    public static FixList For(string job, BrainRecall.Index ix)
    {
        var naming = BoardDocsWith(ix, job).ToHashSet();
        var ian = BoardDocsWith(ix, IanTerm).ToHashSet();
        var boards = naming.Select(d => ix.Docs[d].Where).Where(o => o is not ("Framework" or "Ian" or "")).Distinct(StringComparer.Ordinal)
                           .OrderBy(o => o, StringComparer.Ordinal).ToList();
        var onBoards = boards.ToHashSet(StringComparer.Ordinal);
        IEnumerable<BrainRecall.Doc> Order(IEnumerable<int> d) => d.Select(i => ix.Docs[i]).OrderBy(x => x.Where, StringComparer.Ordinal).ThenBy(x => x.Id);
        return new FixList(job,
            Order(naming.Where(ian.Contains)).ToList(),
            boards,
            Order(ian.Where(d => !naming.Contains(d) && onBoards.Contains(ix.Docs[d].Where) && !TitleNamesAJob(ix.Docs[d].Lead))).ToList());
    }

    /// <summary>A row whose title - its [tag], or its first <see cref="TitleChars"/> characters when it has none - names a 5-digit job is that
    /// job's own row, not a standing ask (row 48 "31170 (Griffin): ...", row 2013 "[RERUN 01819 incremental]"). Row 1647's title is
    /// "[cover 3D pictures]": the jobs it names later (31180-01) are examples, so it stays.</summary>
    public static bool TitleNamesAJob(string lead)
    {
        var t = (lead ?? "").TrimStart();
        var title = t.StartsWith('[') && t.IndexOf(']') is > 0 and var close ? t[..close] : t[..Math.Min(t.Length, TitleChars)];
        return FiveDigits.IsMatch(title);
    }

    public const int TitleChars = 40;

    private static string Line(BrainRecall.Doc d) =>
        $"- {d.Ref} [{d.Kind}, owner {d.Where}] {SecretGuard.Mask(BrainRecall.FirstSentence(d.Lead, LeadChars))}";

    /// <summary>Row 2042: a job the moment WORKS, not one it mentions. A 5-digit number counts only as (1) a path segment under jobs/ or one
    /// that opens NNNNN-NN (jobs/31218-01-137th-street/job.json, work/31218-01/...), (2) <c>--job NNNNN</c>, or (3) the words "job NNNNN" /
    /// "NNNNN job". So "Heather Lands 31097" in a census note (10 Nov, OverLORD) and "31218-01-137th-street" as a bare argument are not
    /// jobs. A number the board does not know still gives an empty list (<see cref="For"/>), so nothing is said.
    /// WHAT IT DOES NOT CATCH: a session working a job only by its name ("the Halifax run") or by a project share path "\\...\31218 Name\".</summary>
    private static readonly Regex Worked = new(
        @"(?i)(?:jobs?[\\/](\d{5})(?!\d)|(?<=[\\/])(\d{5})-\d{2}(?!\d)|--job[\s=]+(\d{5})(?!\d)|\bjob\s*[:#]?\s*(?:no\.?\s*|number\s*)?(\d{5})(?!\d)|(?<![\p{L}\p{N}])(\d{5})\s+job\b)",
        RegexOptions.CultureInvariant);

    public static List<string> WorkedJobs(string text) =>
        Worked.Matches(text ?? "").Select(m => m.Groups.Values.Skip(1).First(g => g.Success).Value).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>The hook's text (row 2042): the header and the rows NAMING the job; Ian's standing asks as ONE line with their count and the
    /// command that lists them, only when <paramref name="withStanding"/> (once per session). Null when there is nothing to say.</summary>
    public static string? HookText(FixList f, bool withStanding)
    {
        var sb = new StringBuilder();
        if (f.Naming.Count > 0)
        {
            sb.Append($"JOB FIX LIST (kor recall, row 2031) - job {f.Job}: {f.Naming.Count} open row(s) naming it that quote Ian; a run or brief for {f.Job} answers each or names it not done (#131883):\n");
            foreach (var d in f.Naming) sb.Append(Line(d)).Append('\n');
        }
        if (withStanding && f.Standing.Count > 0)
            sb.Append($"IAN'S STANDING ASKS: {f.Standing.Count} open row(s) on {string.Join(", ", f.Boards)} quote Ian and name no job, so they hold for every job (row 1647 is one); list them: kor recall --job {f.Job}\n");
        return sb.Length == 0 ? null : sb.ToString().TrimEnd('\n');
    }

    public static string Text(FixList f)
    {
        var sb = new StringBuilder();
        sb.Append($"JOB FIX LIST (kor recall, row 2031) - job {f.Job}: the open rows on EVERY owner's board that quote Ian. Each is an ask of Ian's this job's run must answer or name as not done; a brief or handover that drops one repeats row 1647 (#131883).\n");
        sb.Append($"NAMING {f.Job} ({f.Naming.Count}):\n");
        foreach (var d in f.Naming) sb.Append(Line(d)).Append('\n');
        sb.Append($"IAN'S STANDING ASKS on its boards ({string.Join(", ", f.Boards)}; {f.Standing.Count}) - asks about the product, so they hold for {f.Job} too:\n");
        foreach (var d in f.Standing) sb.Append(Line(d)).Append('\n');
        return sb.ToString().TrimEnd('\n');
    }
}

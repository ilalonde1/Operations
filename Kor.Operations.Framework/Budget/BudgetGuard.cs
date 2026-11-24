using System.Globalization;
using System.Text.RegularExpressions;

namespace Kor.Operations.Framework.Budget;

public sealed record BudgetLimit(int Runs, TimeSpan? Duration, string? Error);
public sealed record BudgetSpend(int Row, string Step, string? Ref, string Session, string Outcome, DateTimeOffset AtUtc);
public sealed record BudgetDecision(string Outcome, string Message, int Spent, BudgetLimit Limit)
{
    public bool Allowed => Outcome is "allowed" or "lifted";
}

/// <summary>
/// Counts permissions to start, across sessions, not successful completions. A lifted permission is also a spend.
/// Elapsed wall time begins with the first permission, never with board creation. Missing/ambiguous limits fail closed.
/// Covers calls to budget check only; the KOR-210 launcher is not wired here. A direct run or a dishonest row number
/// still evades the stop. No Codex, Revit, token or money limit is enforced. See the response for the exact grammar.
/// </summary>
public static class BudgetGuard
{
    public const string Step = "kor210-run";
    private const RegexOptions RxOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly Regex Runs = new(
        @"(?:^|[;\n])\s*(?:at most\s+)?(?<n>one|a|\d+)\s+(?:KOR-210\s+(?:(?:SAFE|ETABS)\s+)?runs?|(?:(?:SAFE|ETABS)\s+)?runs?\s+on\s+KOR-210)\b",
        RxOptions, TimeSpan.FromSeconds(1));
    private static readonly Regex Time = new(
        @"(?:^\s*|~\s*)(?:(?<half>half a day)\b|(?<n>\d+(?:\.\d+)?)\s*(?<unit>min(?:ute)?s?|h(?:ours?|rs?)?|days?)\b)",
        RxOptions, TimeSpan.FromSeconds(1));

    public static BudgetLimit Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new(0, null, "state the run count in the row's Budget");
        var matches = Runs.Matches(text);
        string? error = null;
        var runs = 0;
        if (matches.Count != 1)
            error = matches.Count == 0 && !text.Contains("KOR-210", StringComparison.OrdinalIgnoreCase)
                ? "state the run count in the row's Budget (no KOR-210 count means 0 runs)"
                : "unparseable or ambiguous KOR-210 run count; state one explicit run count in the row's Budget";
        else
        {
            var count = matches[0].Groups["n"].Value;
            if (!int.TryParse(count, NumberStyles.None, CultureInfo.InvariantCulture, out runs))
            {
                if (count.Equals("one", StringComparison.OrdinalIgnoreCase) || count.Equals("a", StringComparison.OrdinalIgnoreCase)) runs = 1;
                else error = "KOR-210 run count is outside the supported integer range";
            }
            var rest = text.Remove(matches[0].Index, matches[0].Length);
            if (rest.Contains("KOR-210", StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(text[(matches[0].Index + matches[0].Length)..], @"^\s*(?:or\b|to\b|[-–])", RxOptions, TimeSpan.FromSeconds(1)))
                error = "ambiguous additional KOR-210 allowance";
        }
        var times = Time.Matches(text);
        if (times.Count > 1) return new(runs, null, "ambiguous time allowance; state one duration");
        TimeSpan? duration = null;
        if (times.Count == 1)
        {
            var m = times[0];
            if (Regex.IsMatch(text[(m.Index + m.Length)..], @"^\s*(?:or\b|to\b|[-–])", RxOptions, TimeSpan.FromSeconds(1)))
                return new(runs, null, "ambiguous time allowance; state one duration");
            try
            {
                var unit = m.Groups["unit"].Value.ToLowerInvariant();
                var seconds = m.Groups["half"].Success ? 43200m
                    : decimal.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture)
                      * (unit.StartsWith("min", StringComparison.Ordinal) ? 60m : unit.StartsWith("day", StringComparison.Ordinal) ? 86400m : 3600m);
                duration = TimeSpan.FromTicks(checked((long)(seconds * TimeSpan.TicksPerSecond)));
            }
            catch (Exception e) when (e is OverflowException or FormatException)
            { return new(runs, null, "time allowance is outside the supported range"); }
            text = text.Remove(m.Index, m.Length);
        }
        if (text.Contains('~') || Regex.IsMatch(text, @"\d\s*(?:min(?:ute)?s?|h(?:ours?|rs?)?|days?)\b", RxOptions, TimeSpan.FromSeconds(1)))
            return new(runs, null, "unparseable or ambiguous time allowance; use a leading duration or ~N h");
        return new(runs, duration, error);
    }

    public static BudgetDecision Decide(int row, string? budget, IReadOnlyList<BudgetSpend> spends, string step,
                                        DateTimeOffset now, DateTimeOffset? approval)
    {
        var limit = Parse(budget);
        var allowed = spends.Where(s => s.Row == row && s.Step == Step && s.Outcome is "allowed" or "lifted").ToList();
        var spent = allowed.Count;
        var first = allowed.Count == 0 ? (DateTimeOffset?)null : allowed.Min(s => s.AtUtc);
        var why = row <= 0 ? "row must be a positive board row number"
            : step != Step ? "unsupported step (only kor210-run is counted)"
            : limit.Error ?? (spent >= limit.Runs ? "run allowance spent"
            : limit.Duration is { } duration && (duration <= TimeSpan.Zero || first is { } at && now - at >= duration)
                ? "elapsed time allowance spent" : null);
        var lifted = why is not null && row > 0 && step == Step && approval is { } typed && typed <= now && now - typed < TimeSpan.FromHours(12);
        var outcome = why is null ? "allowed" : lifted ? "lifted" : "refused";
        var message = $"{outcome}: row {row}, Budget \"{budget?.ReplaceLineEndings(" ") ?? "(missing)"}\", spent {spent} of {limit.Runs} KOR-210 runs. " +
            (why is null ? "This check records one spend." : why + ". ") +
            (lifted ? $"Ian's typed IAN-APPROVED-OVER-BUDGET {row} at {approval:O} lifts the stop for 12 hours from that prompt; this check records one spend."
             : why is null ? "" : $"Ian can lift it by typing IAN-APPROVED-OVER-BUDGET {row} in this session (12 hours from that prompt).");
        return new(outcome, message.Trim(), spent, limit);
    }
}

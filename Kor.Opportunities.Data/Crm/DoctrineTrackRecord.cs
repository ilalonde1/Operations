#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Kor.Opportunities.Data.Crm;

public sealed record DoctrineCardRecord(long CardId, int Suggested, int Done, int NotToday, int NotDoing, int Undecided,
    int PursuitsAdvanced, int PursuitsWon, int PursuitsLost, DateOnly? LastSuggested, IReadOnlyList<string> Flags)
{
    // Kept outside the positional contract: archived is a separate outcome, never an advance or loss.
    public int PursuitsArchived { get; init; }
    /// <summary>Ian's notes on Not today / Not doing moves citing this card, newest first: "2026-10-19 not doing: client is Jim's".</summary>
    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
    public string Summary => Suggested == 0 ? "Not suggested yet"
        : $"Suggested {Suggested} · done {Done} · not today {NotToday} · not doing {NotDoing} · {PursuitsAdvanced} pursuits advanced";
    public string FlagText => string.Join(" · ", Flags.Select(DoctrineTrackRecord.FlagText));
    public static DoctrineCardRecord Empty(long id) => new(id, 0, 0, 0, 0, 0, 0, 0, 0, null, Array.Empty<string>());
}

public sealed record ClosedPursuit(long EngagementId, string Company, int Stage, string? Reason, DateTimeOffset ClosedAtUtc);

/// <summary>
/// Pure observational record, not causal attribution. A suggestion is one move citing a card. Outcomes count distinct
/// linked pursuits per card, regardless of the move result. Advanced means entering a later open stage than at the hour
/// date, or Won, during the next 30 UTC calendar dates (day 30 included). Lost and Archived count separately. The latest
/// history entry on or before the hour date supplies the starting stage; absent history defaults to Pending (1).
/// NoEffect considers only Done moves with pursuit links and whether any of those moves has an advance in its window.
/// Suggestions are ordered by date, hour id, then move rank; no clock, database, or automatic retirement is involved.
/// </summary>
public static class DoctrineTrackRecord
{
    public const string Rule = "Advanced: a linked pursuit entered a later open stage than at the hour date, or was won, "
        + "within the next 30 UTC calendar dates (day 30 included). Starting stage is the latest history on or before the hour date, "
        + "or Pending if missing. Lost and archived count separately. Outcomes are distinct pursuits per card, "
        + "including undecided or skipped moves; this is association, not proof of cause. NoEffect uses only Done pursuit moves.";

    public static string FlagText(string flag) => flag switch
    {
        "Skipped" => "Skipped the last 3 times it came up",
        "Idle" => "Suggested at least 5 times and never done",
        "NoEffect" => "Done on at least 4 pursuit moves with none advanced",
        _ => flag,
    };

    public const string WeightRule = "Ranking weight: 1 + 0.3 × (acted − ½) + 0.2 × (advanced − ½), where acted = (done + 1) / (done + not doing + 2) "
        + "and advanced = (pursuits advanced + 1) / (advanced + lost + 2); a card skipped the last 3 times is capped at 0.8. "
        + "A card with no record weighs exactly 1, and no record moves a card by more than a quarter, so relevance still decides.";

    /// <summary>
    /// How much a card's own record moves its ranking: a bounded multiplier on relevance, 0.75 to 1.25. Not today is neutral.
    /// Smoothed (Laplace) so one answer cannot swing a card, and observational: association, not proof of cause.
    /// </summary>
    public static double RankWeight(DoctrineCardRecord? record)
    {
        if (record is null) return 1.0;
        var acted = (record.Done + 1.0) / (record.Done + record.NotDoing + 2.0);
        var advanced = (record.PursuitsAdvanced + 1.0) / (record.PursuitsAdvanced + record.PursuitsLost + 2.0);
        var weight = 1.0 + 0.3 * (acted - 0.5) + 0.2 * (advanced - 0.5);
        return record.Flags.Contains("Skipped") ? Math.Min(weight, 0.8) : weight;
    }

    public static IReadOnlyList<DoctrineCardRecord> Compute(
        IReadOnlyList<(long HourId, DateOnly ForDate, DirectorHourContent Content)> hours,
        IReadOnlyList<(long HourId, int MoveRank, string Result)> results,
        IReadOnlyList<(long EngagementId, int Stage, DateTimeOffset EnteredAtUtc)> history,
        IReadOnlyList<long> cardIds,
        IReadOnlyDictionary<(long HourId, int MoveRank), string>? notes = null)
    {
        var answers = results.ToDictionary(r => (r.HourId, r.MoveRank), r => r.Result);
        var stages = history.ToLookup(h => h.EngagementId);
        var suggestions = hours.SelectMany(h => DirectorHourFile.AllMoves(h.Content).Select(m => (h.HourId, h.ForDate, Move: m)))
            .OrderByDescending(s => s.ForDate).ThenByDescending(s => s.HourId).ThenByDescending(s => s.Move.Rank)
            .SelectMany(s => s.Move.Cards.Distinct().Select(id => (CardId: id, s.HourId, s.ForDate, s.Move)))
            .ToLookup(s => s.CardId);
        var records = new List<DoctrineCardRecord>();
        foreach (var id in cardIds.Distinct())
        {
            var moves = suggestions[id].ToArray();
            var done = 0;
            var notToday = 0;
            var notDoing = 0;
            var donePursuitMoves = 0;
            var doneAdvanced = false;
            var advanced = new HashSet<long>();
            var won = new HashSet<long>();
            var lost = new HashSet<long>();
            var archived = new HashSet<long>();
            foreach (var move in moves)
            {
                answers.TryGetValue((move.HourId, move.Move.Rank), out var result);
                if (result == "Done") done++;
                if (result == "NotToday") notToday++;
                if (result == "NotDoing") notDoing++;
                var pursuits = move.Move.Links.Where(l => l.Kind == "pursuit").Select(l => l.Id).Distinct().ToArray();
                if (result == "Done" && pursuits.Length > 0) donePursuitMoves++;
                foreach (var pursuit in pursuits)
                {
                    var timeline = stages[pursuit].OrderBy(h => h.EnteredAtUtc).ToArray();
                    var starting = timeline.LastOrDefault(h => DateOnly.FromDateTime(h.EnteredAtUtc.UtcDateTime) <= move.ForDate).Stage;
                    if (starting == 0) starting = 1;
                    foreach (var entry in timeline)
                    {
                        var date = DateOnly.FromDateTime(entry.EnteredAtUtc.UtcDateTime);
                        if (date <= move.ForDate || date > move.ForDate.AddDays(30)) continue;
                        if (entry.Stage == 6 || (entry.Stage is > 1 and < 6 && entry.Stage > starting))
                        {
                            advanced.Add(pursuit);
                            if (result == "Done") doneAdvanced = true;
                        }
                        if (entry.Stage == 6) won.Add(pursuit);
                        if (entry.Stage == 7) lost.Add(pursuit);
                        if (entry.Stage == 8) archived.Add(pursuit);
                    }
                }
            }

            var flags = new List<string>();
            if (moves.Length >= 3 && moves.Take(3).All(m => answers.TryGetValue((m.HourId, m.Move.Rank), out var r) && r == "NotDoing"))
                flags.Add("Skipped");
            if (moves.Length >= 5 && done == 0) flags.Add("Idle");
            if (donePursuitMoves >= 4 && !doneAdvanced) flags.Add("NoEffect");
            // moves are newest first, so the reasons are too.
            var reasons = moves
                .Where(m => answers.TryGetValue((m.HourId, m.Move.Rank), out var r) && r is ("NotDoing" or "NotToday")
                    && notes is not null && notes.TryGetValue((m.HourId, m.Move.Rank), out var n) && !string.IsNullOrWhiteSpace(n))
                .Select(m => $"{m.ForDate:yyyy-MM-dd} {(answers[(m.HourId, m.Move.Rank)] == "NotDoing" ? "not doing" : "not today")}: {notes![(m.HourId, m.Move.Rank)].Trim()}")
                .ToArray();
            records.Add(new DoctrineCardRecord(id, moves.Length, done, notToday, notDoing, moves.Length - done - notToday - notDoing,
                advanced.Count, won.Count, lost.Count, moves.Length == 0 ? null : moves[0].ForDate, flags)
                { PursuitsArchived = archived.Count, Reasons = reasons });
        }
        return records;
    }
}

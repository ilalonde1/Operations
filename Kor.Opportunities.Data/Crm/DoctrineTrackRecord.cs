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

    public static IReadOnlyList<DoctrineCardRecord> Compute(
        IReadOnlyList<(long HourId, DateOnly ForDate, DirectorHourContent Content)> hours,
        IReadOnlyList<(long HourId, int MoveRank, string Result)> results,
        IReadOnlyList<(long EngagementId, int Stage, DateTimeOffset EnteredAtUtc)> history,
        IReadOnlyList<long> cardIds)
    {
        var answers = results.ToDictionary(r => (r.HourId, r.MoveRank), r => r.Result);
        var stages = history.ToLookup(h => h.EngagementId);
        var suggestions = hours.SelectMany(h => h.Content.Moves.Select(m => (h.HourId, h.ForDate, Move: m)))
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
            records.Add(new DoctrineCardRecord(id, moves.Length, done, notToday, notDoing, moves.Length - done - notToday - notDoing,
                advanced.Count, won.Count, lost.Count, moves.Length == 0 ? null : moves[0].ForDate, flags) { PursuitsArchived = archived.Count });
        }
        return records;
    }
}

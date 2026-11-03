using System.Data;
using Microsoft.Data.SqlClient;

namespace Kor.Operations.Framework.Start;

/// <summary>One row of oversight.vw_Board as board_render.py selects it (DueLocal through CONVERT(nvarchar(30), .., 120)).</summary>
public sealed record BoardRow(int Id, string? Owner, string? State, string? Flag, string? What, string? DoneWhen, string? DueLocal,
                              string? BlockedOn, string? Budget);

/// <summary>One row of oversight.vw_MistakeOwed (HoursOpen decimal(9,1), read as Python's float of its JSON).</summary>
public sealed record OwedRow(int Id, string? What, string? State, double? HoursOpen, string? Flag);

/// <summary>One row of oversight.vw_GuardWeek with a wrong refusal.</summary>
public sealed record GuardRow(string? Guard, int? Refusals, int? JudgedWrong);

/// <summary>What the board part renders. A null owed/guards status means "not read" (the lead is not Framework).</summary>
public sealed record BoardState(IReadOnlyList<string> Owners, IReadOnlyList<BoardRow> Rows, bool OwedApplies,
                                IReadOnlyList<OwedRow> Owed, string OwedStatus, IReadOnlyList<GuardRow> Guards, string GuardsStatus)
{
    public const string OwedLead = "Framework";

    /// <summary>
    /// F6: each owner also brings its '&lt;owner&gt; helper' rows, added after the owners, once each; an owner already
    /// named '... helper' gets none. board_render.with_helpers.
    /// </summary>
    public static IReadOnlyList<string> WithHelpers(IEnumerable<string> owners)
    {
        var list = Distinct(owners);
        return Distinct(list.Concat(list.Where(o => !o.EndsWith(" helper", StringComparison.Ordinal)).Select(o => $"{o} helper")));
    }

    public static List<string> Distinct(IEnumerable<string> owners)
    {
        var out_ = new List<string>();
        foreach (var o in owners) if (!out_.Contains(o, StringComparer.Ordinal)) out_.Add(o);
        return out_;
    }

    /// <summary>
    /// ONE opportunities_app unit of work (the caller's, never committed - it only reads): the board rows for every owner
    /// (one read per owner, each ORDER BY What, as board_render), then - when Framework is one of the GIVEN owners - the
    /// owed mistakes and the guards' week, each its own read so one failing is said loudly without losing the board.
    /// </summary>
    public static BoardState Load(BrainDb db, IReadOnlyList<string> givenOwners, bool withHelpers)
    {
        var given = Distinct(givenOwners);
        var owedApplies = given.Contains(OwedLead, StringComparer.Ordinal);
        var owners = withHelpers ? WithHelpers(given) : given;
        var rows = new List<BoardRow>();
        foreach (var o in owners)
        {
            using var c = db.Command(
                "SELECT Id, Owner, State, Flag, What, DoneWhen, CONVERT(nvarchar(30), DueLocal, 120) AS DueLocal, BlockedOn, Budget " +
                "FROM oversight.vw_Board WHERE Owner = @o ORDER BY What", 120);
            c.Parameters.Add("@o", SqlDbType.NVarChar, 120).Value = o;
            using var r = c.ExecuteReader();
            while (r.Read())
                rows.Add(new BoardRow(r.GetInt32(0), S(r, 1), S(r, 2), S(r, 3), S(r, 4), S(r, 5), S(r, 6), S(r, 7), S(r, 8)));
        }
        IReadOnlyList<OwedRow> owed = Array.Empty<OwedRow>();
        IReadOnlyList<GuardRow> guards = Array.Empty<GuardRow>();
        string owedStatus = "live", guardsStatus = "live";
        if (owedApplies)
        {
            (owed, owedStatus) = Guarded(db, () =>
            {
                using var c = db.Command("SELECT Id, What, State, HoursOpen, Flag FROM oversight.vw_MistakeOwed ORDER BY HoursOpen DESC, Id", 120);
                using var r = c.ExecuteReader();
                var l = new List<OwedRow>();
                while (r.Read())
                    l.Add(new OwedRow(r.GetInt32(0), S(r, 1), S(r, 2), r.IsDBNull(3) ? null : Hours(r.GetDecimal(3)), S(r, 4)));
                return (IReadOnlyList<OwedRow>)l;
            });
            (guards, guardsStatus) = Guarded(db, () =>
            {
                using var c = db.Command("SELECT Guard, Refusals, JudgedWrong FROM oversight.vw_GuardWeek WHERE JudgedWrong > 0 " +
                                         "ORDER BY JudgedWrong DESC, Guard", 120);
                using var r = c.ExecuteReader();
                var l = new List<GuardRow>();
                while (r.Read()) l.Add(new GuardRow(S(r, 0), r.IsDBNull(1) ? null : r.GetInt32(1), r.IsDBNull(2) ? null : r.GetInt32(2)));
                return (IReadOnlyList<GuardRow>)l;
            });
        }
        return new BoardState(owners, rows, owedApplies, owed, owedStatus, guards, guardsStatus);
    }

    /// <summary>A read that may fail on its own: its rows, or none and "UNREADABLE: &lt;first line of the reason, 200&gt;".</summary>
    private static (IReadOnlyList<T>, string) Guarded<T>(BrainDb db, Func<IReadOnlyList<T>> read)
    {
        try { return (read(), "live"); }
        // as board_render.load_owed: ANY failure of this one read is said on its own line, never the whole board (review row 196)
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return (Array.Empty<T>(), "UNREADABLE: " + Reason(e));
        }
    }

    /// <summary>board_render's reason: the exception's first line, at most 200 characters.</summary>
    public static string Reason(Exception e)
    {
        var m = string.IsNullOrEmpty(e.Message) ? e.GetType().Name : e.Message;
        var first = m.Split('\n')[0].TrimEnd('\r');
        return PyText.Head(first, 200);
    }

    private static string? S(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    /// <summary>As Python reads it: FOR JSON writes the decimal's text and json.loads parses that text to a float.</summary>
    public static double Hours(decimal d) =>
        double.Parse(d.ToString(System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// board_render.py's render, owed_block and guards_line, ported line for line (Framework tools/board_render.py, 2 Nov):
/// the same text for the same rows. ONE deliberate difference: the COMPACT line names the kor command that prints the
/// full board (<see cref="FullBoardCommand"/>), not the Python script K6 retires. Everything else - wording, order, the
/// 9,900 budget, the width ladder, "None" for a missing value, hours in Python's <c>:g</c> - is board_render's.
/// </summary>
public static class BoardRender
{
    public const int OwedWhat = 160;
    public const int OwedMax = 3000;
    public const string Where = "KorStandards oversight.vw_Board";

    public static string FullBoardCommand(string module, IReadOnlyList<string> givenOwners, bool noHelpers) =>
        $"C:/KOR-Operations/kor/kor.exe start --module \"{module}\" --part board " +
        string.Join(" ", givenOwners.Select(o => $"--owner \"{o}\"")) + (noHelpers ? " --no-helpers" : "") + " --text";

    /// <summary>The whole board part: the rows, then (Framework) the owed block and the guards line, which keep their room.</summary>
    public static string Render(BoardState s, DateTime now, int? budget, string fullBoardCommand)
    {
        string? tail = null;
        if (s.OwedApplies)
        {
            tail = OwedBlock(s.Owed, s.OwedStatus);
            var g = GuardsLine(s.Guards, s.GuardsStatus);
            tail = g is not null ? tail + "\n" + g : tail;
        }
        return Rows(s.Owners, s.Rows, "live", budget, tail, now, fullBoardCommand);
    }

    public static string Rows(IReadOnlyList<string> owners, IReadOnlyList<BoardRow> rows, string status, int? budget, string? tail,
                              DateTime now, string fullBoardCommand)
    {
        if (!string.IsNullOrEmpty(tail))
        {
            var head = Rows(owners, rows, status, budget is null ? null : budget - PyText.Len(tail) - 1, null, now, fullBoardCommand);
            return head + "\n" + tail;
        }
        var stamp = now.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        var multi = owners.Count > 1;
        var headLine = multi
            ? $"THE BOARD (source of truth) - {Where}, Owners " + string.Join(", ", owners.Select(o => $"'{o}'")) +
              $" (every one of them this lead's), read {stamp}"
            : $"THE BOARD (source of truth) - {Where}, Owner '{owners[0]}', read {stamp}";
        if (status != "live")
            headLine = $"!! BOARD {status} !! Read the board by hand (oq.py) before any work.\n" + headLine;
        var lines = new List<string>
        {
            headLine,
            $"{rows.Count} open row(s). THE GOAL IS NONE (Ian, 2 Nov): this is YOUR board - add every new item as a row, " +
            "cross a row off (done, with evidence) the moment it is finished, and change a row on the board, never " +
            "only in chat. A new row needs What (goal), DoneWhen and Budget (time, runs, what it may read) - the " +
            "board refuses one without a Budget (F2, 2 Nov).",
        };
        if (multi)
            lines.Add("Per owner: " + string.Join("; ", owners.Select(o => $"'{o}' {rows.Count(r => r.Owner == o)}")));
        foreach (var r in rows)
        {
            var flag = PyText.Truthy(r.Flag) ? $" {r.Flag}" : "";
            var who = multi ? $" (Owner: {(PyText.Truthy(r.Owner) ? r.Owner : "?")})" : "";
            lines.Add($"- row {r.Id} [{PyText.Str(r.State)}{flag}]{who} {PyText.Str(r.What)}");
            lines.Add($"    done when: {PyText.Str(r.DoneWhen)}");
            if (PyText.Truthy(r.Budget)) lines.Add($"    budget: {r.Budget}");
            if (PyText.Truthy(r.BlockedOn)) lines.Add($"    blocked on: {r.BlockedOn}");
            if (PyText.Truthy(r.DueLocal)) lines.Add($"    due: {r.DueLocal}");
        }
        var full = string.Join("\n", lines);
        if (budget is null || PyText.Len(full) <= budget) return full;
        var text = full;
        foreach (var width in new[] { 160, 120, 90, 60, 40, 20, 0 })
        {
            var shortLines = lines.Take(multi ? 3 : 2).ToList();
            shortLines.Add($"COMPACT ({PyText.Len(full)} chars would not fit the session-start limit): done-when, blocked-on and due " +
                           $"are left out and titles cut to {width} chars. The full board: {fullBoardCommand}");
            foreach (var r in rows)
            {
                var flag = PyText.Truthy(r.Flag) ? $" {r.Flag}" : "";
                var who = multi ? $" (Owner: {(PyText.Truthy(r.Owner) ? r.Owner : "?")})" : "";
                var what = r.What ?? "";
                what = PyText.Len(what) <= width ? what : PyText.RStrip(PyText.Head(what, width)) + "...";
                shortLines.Add(PyText.RStrip($"- row {r.Id} [{PyText.Str(r.State)}{flag}]{who} {what}"));
            }
            text = string.Join("\n", shortLines);
            if (PyText.Len(text) <= budget) break;
        }
        return text;   // at width 0 every row is still listed, even if that alone is over the budget
    }

    public static string OwedBlock(IReadOnlyList<OwedRow> rows, string status)
    {
        if (status != "live")
            return $"!! IAN-CAUGHT MISTAKES OWED: {status} !! Read oversight.vw_MistakeOwed by hand (oq.py) - " +
                   "every Ian-caught mistake needs a mechanical mitigation within a day.";
        if (rows.Count == 0)
            return "IAN-CAUGHT MISTAKES OWED: 0 - every mistake Ian caught has a mitigation recorded done.";
        var hours = rows.Select(r => r.HoursOpen ?? 0.0).ToList();
        var lines = new List<string>
        {
            $"IAN-CAUGHT MISTAKES OWED: {rows.Count} (oldest {PyText.G(hours.Max())} hours) - each needs a mechanical mitigation " +
            "(hook, check, guard) with MitigationRef set and State 'done' within 24 h; Framework owns making it happen.",
        };
        var size = PyText.Len(lines[0]);
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var h = hours[i];
            var what = PyText.Squash(r.What ?? "");
            what = PyText.Len(what) <= OwedWhat ? what : PyText.RStrip(PyText.Head(what, OwedWhat)) + "...";
            var flag = h > 24 ? $"OVERDUE {PyText.G(h)} h" : $"{PyText.G(h)} h";
            var line = $"- mistake {r.Id} [{flag}, {PyText.Str(r.State)}] {what}";
            if (size + 1 + PyText.Len(line) + 120 > OwedMax)
            {
                lines.Add($"... +{rows.Count - i} more - SELECT Id, HoursOpen, What FROM oversight.vw_MistakeOwed (oq.py)");
                break;
            }
            lines.Add(line);
            size += 1 + PyText.Len(line);
        }
        return string.Join("\n", lines);
    }

    public static string? GuardsLine(IReadOnlyList<GuardRow> rows, string status)
    {
        if (status != "live") return $"!! GUARDS (7 days): {status} !! python -I tools/guard_log.py week";
        if (rows.Count == 0) return null;
        return "GUARDS (7 days): " + string.Join(", ", rows.Select(r => $"{PyText.Str(r.Guard)} {PyText.Str(r.Refusals)}/{PyText.Str(r.JudgedWrong)}"))
               + " (refusals/judged wrong; HOW-WE-BUILD 7: fix or trim a guard that costs more than it catches). "
               + "python -I tools/guard_log.py week | recent | judge <id> right|wrong";
    }
}

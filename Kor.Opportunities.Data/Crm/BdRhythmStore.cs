#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Crm;

/// <summary>An action on the register: a BdAsk row with the meeting it came from and its owner's name (migration 360).</summary>
public sealed record BdActionRow(long Id, string OwnerKey, string OwnerName, string Action, string? WhyNow, int Minutes,
    DateTimeOffset DueUtc, string Status, DateTimeOffset? ClosedAtUtc, string? ClosedBy, string? Result, long? MeetingId,
    string? MeetingTitle, DateTimeOffset? MeetingAtUtc, string? Region, string? SourceRef, long? EngagementId, long? CanonicalOrgId)
{
    public bool IsOpen => Status == "Open";
}

/// <summary>A new action for the register.</summary>
public sealed record BdActionDraft(string OwnerKey, string Action, string? WhyNow, int Minutes, DateTimeOffset DueUtc,
    string? Region, string? SourceRef, long? MeetingId);

/// <summary>A BD meeting: an all-hands, a one-to-one with a regional lead, or other.</summary>
public sealed record BdMeetingRow(long Id, string Kind, string Title, string? WithStaffKey, string? WithName,
    DateTimeOffset ScheduledForUtc, int DurationMinutes, string Status, string? Attendees, string? Agenda, string? Recap, string? RecordRef);

/// <summary>A reference contact KOR has given out, and when it was last checked.</summary>
public sealed record BdReferenceRow(long Id, string Application, string Project, string Role, string? Company, string? ContactName,
    string? Phone, string? Email, string? KorLead, string? KorLeadName, DateTimeOffset? LastCalledUtc, string? CalledBy,
    string CallOutcome, string? Notes)
{
    public bool NamesAPerson => !string.IsNullOrWhiteSpace(ContactName);
}

/// <summary>One imported prepared hour (the JSON is <see cref="DirectorHourContent"/>).</summary>
public sealed record DirectorHourRow(long Id, DateOnly ForDate, string Kind, string ContentJson, string WrittenBy,
    string ImportedBy, DateTimeOffset ImportedAtUtc, DateTimeOffset? EmailedAtUtc);

/// <summary>A person on the BD team (migration 361). Role is Manager, Lead or Partner; Region uses the CRM's names.</summary>
public sealed record BdLeadRow(string StaffKey, string Name, string Role, string? Region, int SortOrder);

/// <summary>What Ian did with one move of a prepared hour.</summary>
public sealed record HourMoveResult(int Rank, string Result, string? Note, DateTimeOffset RecordedAtUtc);

public static class BdRhythmValues
{
    public static readonly IReadOnlyList<string> MoveResults = new[] { "Done", "NotToday", "NotDoing" };
    public static readonly IReadOnlyList<string> ActionStatuses = new[] { "Open", "Done", "Declined", "Withdrawn" };
    public static readonly IReadOnlyList<string> MeetingKinds = new[] { "AllHands", "OneToOne", "Other" };
    public static readonly IReadOnlyList<string> MeetingStatuses = new[] { "Planned", "Held", "Cancelled" };
    public static readonly IReadOnlyList<string> CallOutcomes = new[] { "NotCalled", "Confirmed", "LeftMessage", "NoLongerThere", "Declined" };
    public static readonly IReadOnlyList<string> HourKinds = new[] { "BD", "Operations" };
}

/// <summary>
/// The operating rhythm Ian committed to on 9 October 2026: the action register (BdAsk with its meeting), meetings, the
/// references register and the prepared hour. Nothing here sends anything to anyone; the Worker's email goes to Ian only.
/// </summary>
public interface IBdRhythmStore
{
    Task<IReadOnlyList<BdActionRow>> ListActionsAsync(CancellationToken ct);
    Task<long> AddActionAsync(BdActionDraft draft, string by, CancellationToken ct);
    /// <summary>Closes an OPEN action as Done, Declined or Withdrawn with its result. Returns false when it was not open.</summary>
    Task<bool> CloseActionAsync(long id, string status, string? result, string by, CancellationToken ct);
    Task<IReadOnlyList<BdMeetingRow>> ListMeetingsAsync(CancellationToken ct);
    Task<long> PlanMeetingAsync(string kind, string title, string? withStaffKey, DateTimeOffset scheduledForUtc, int durationMinutes,
        string? attendees, string? agenda, string by, CancellationToken ct);
    /// <summary>Marks a meeting Held (with its recap) or Cancelled. Returns false when the meeting does not exist.</summary>
    Task<bool> RecordMeetingAsync(long id, string status, string? recap, string? recordRef, CancellationToken ct);
    Task<IReadOnlyList<BdReferenceRow>> ListReferencesAsync(CancellationToken ct);
    /// <summary>Records a call to a reference: its outcome, the person reached and notes.</summary>
    Task<bool> RecordReferenceCallAsync(long id, string outcome, string? contactName, string? notes, string by, CancellationToken ct);
    /// <summary>The newest import of the prepared hour for that date and kind, or null.</summary>
    Task<DirectorHourRow?> GetHourAsync(string kind, DateOnly forDate, CancellationToken ct);
    Task<long> ImportHourAsync(string kind, DateOnly forDate, string contentJson, string writtenBy, string importedBy, CancellationToken ct);
    Task MarkHourEmailedAsync(long id, CancellationToken ct);
    /// <summary>The BD team (migration 361): role and region per person, in the order the People screen shows them.</summary>
    Task<IReadOnlyList<BdLeadRow>> ListLeadsAsync(CancellationToken ct);
    /// <summary>What Ian did with each move of an hour.</summary>
    Task<IReadOnlyList<HourMoveResult>> ListMoveResultsAsync(long hourId, CancellationToken ct);
    /// <summary>Records Done, NotToday or NotDoing for one move; a second answer replaces the first.</summary>
    Task RecordMoveResultAsync(long hourId, int rank, string result, string? note, string by, CancellationToken ct);
}

public sealed class SqlBdRhythmStore : IBdRhythmStore
{
    private readonly string _connectionString;

    public SqlBdRhythmStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _connectionString = connectionString;
    }

    // BdStaff.StaffKey carries an explicit collation; every join to it states one.
    private const string ActionSql = @"
SELECT a.Id, a.ToStaffKey, COALESCE(s.DisplayName, a.ToStaffKey) AS OwnerName, a.Ask, a.WhyNow, a.Minutes, a.DueUtc, a.Status,
       a.ClosedAtUtc, a.ClosedBy, a.ClosedNote, a.MeetingId, m.Title, m.ScheduledForUtc, a.Region, a.SourceRef, a.EngagementId, a.CanonicalOrgId
FROM opportunities.BdAsk a
LEFT JOIN opportunities.BdStaff s ON s.StaffKey = a.ToStaffKey COLLATE SQL_Latin1_General_CP1_CI_AS
LEFT JOIN opportunities.BdMeeting m ON m.Id = a.MeetingId
ORDER BY CASE WHEN a.Status = N'Open' THEN 0 ELSE 1 END, CASE WHEN a.Status = N'Open' THEN a.DueUtc END, a.ClosedAtUtc DESC, a.Id;";

    public Task<IReadOnlyList<BdActionRow>> ListActionsAsync(CancellationToken ct) => ReadAsync(ActionSql, _ => { },
        r => new BdActionRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), Text(r, 4), r.GetInt32(5),
            r.GetDateTimeOffset(6), r.GetString(7), When(r, 8), Text(r, 9), Text(r, 10), Id(r, 11), Text(r, 12), When(r, 13),
            Text(r, 14), Text(r, 15), Id(r, 16), Id(r, 17)), ct);

    public Task<long> AddActionAsync(BdActionDraft draft, string by, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (string.IsNullOrWhiteSpace(draft.OwnerKey) || string.IsNullOrWhiteSpace(draft.Action) || draft.Action.Length > 500
            || draft.Minutes is < 5 or > 180 || string.IsNullOrWhiteSpace(by))
            throw new ArgumentException("The action needs an owner, a description of at most 500 characters and 5 to 180 minutes.", nameof(draft));
        return ScalarAsync(@"INSERT INTO opportunities.BdAsk (ToStaffKey, Ask, WhyNow, Minutes, DueUtc, AskedBy, MeetingId, Region, SourceRef)
OUTPUT INSERTED.Id VALUES (@owner, @ask, @why, @minutes, @due, @by, @meeting, @region, @source);", cmd =>
        {
            AddText(cmd, "@owner", 150, draft.OwnerKey.Trim()); AddText(cmd, "@ask", 500, draft.Action.Trim());
            AddText(cmd, "@why", 1000, draft.WhyNow); cmd.Parameters.Add("@minutes", SqlDbType.Int).Value = draft.Minutes;
            cmd.Parameters.Add("@due", SqlDbType.DateTimeOffset).Value = draft.DueUtc; AddText(cmd, "@by", 150, by);
            cmd.Parameters.Add("@meeting", SqlDbType.BigInt).Value = (object?)draft.MeetingId ?? DBNull.Value;
            AddText(cmd, "@region", 40, draft.Region); AddText(cmd, "@source", 200, draft.SourceRef);
        }, ct);
    }

    public async Task<bool> CloseActionAsync(long id, string status, string? result, string by, CancellationToken ct)
    {
        if (status is not ("Done" or "Declined" or "Withdrawn") || string.IsNullOrWhiteSpace(by) || result?.Length > 500)
            throw new ArgumentException("Close an action as Done, Declined or Withdrawn, with a result of at most 500 characters.", nameof(status));
        return await ExecuteAsync(@"UPDATE opportunities.BdAsk
SET Status = @status, ClosedAtUtc = sysdatetimeoffset(), ClosedBy = @by, ClosedNote = @note
WHERE Id = @id AND Status = N'Open';", cmd =>
        {
            cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id; AddText(cmd, "@status", 20, status);
            AddText(cmd, "@by", 150, by); AddText(cmd, "@note", 500, string.IsNullOrWhiteSpace(result) ? null : result.Trim());
        }, ct).ConfigureAwait(false) == 1;
    }

    public Task<IReadOnlyList<BdMeetingRow>> ListMeetingsAsync(CancellationToken ct) => ReadAsync(@"
SELECT m.Id, m.Kind, m.Title, m.WithStaffKey, s.DisplayName, m.ScheduledForUtc, m.DurationMinutes, m.Status, m.Attendees, m.Agenda, m.Recap, m.RecordRef
FROM opportunities.BdMeeting m
LEFT JOIN opportunities.BdStaff s ON s.StaffKey = m.WithStaffKey COLLATE SQL_Latin1_General_CP1_CI_AS
ORDER BY m.ScheduledForUtc DESC, m.Id DESC;", _ => { },
        r => new BdMeetingRow(r.GetInt64(0), r.GetString(1), r.GetString(2), Text(r, 3), Text(r, 4), r.GetDateTimeOffset(5),
            r.GetInt32(6), r.GetString(7), Text(r, 8), Text(r, 9), Text(r, 10), Text(r, 11)), ct);

    public Task<long> PlanMeetingAsync(string kind, string title, string? withStaffKey, DateTimeOffset scheduledForUtc, int durationMinutes,
        string? attendees, string? agenda, string by, CancellationToken ct)
    {
        if (!BdRhythmValues.MeetingKinds.Contains(kind) || string.IsNullOrWhiteSpace(title) || title.Length > 200
            || durationMinutes is < 5 or > 240 || string.IsNullOrWhiteSpace(by) || (kind == "OneToOne" && string.IsNullOrWhiteSpace(withStaffKey)))
            throw new ArgumentException("The meeting needs a kind, a title, 5 to 240 minutes and, for a one-to-one, who it is with.", nameof(kind));
        return ScalarAsync(@"INSERT INTO opportunities.BdMeeting (Kind, Title, WithStaffKey, ScheduledForUtc, DurationMinutes, Attendees, Agenda, CreatedBy)
OUTPUT INSERTED.Id VALUES (@kind, @title, @with, @at, @minutes, @attendees, @agenda, @by);", cmd =>
        {
            AddText(cmd, "@kind", 20, kind); AddText(cmd, "@title", 200, title.Trim()); AddText(cmd, "@with", 150, withStaffKey);
            cmd.Parameters.Add("@at", SqlDbType.DateTimeOffset).Value = scheduledForUtc;
            cmd.Parameters.Add("@minutes", SqlDbType.Int).Value = durationMinutes;
            AddText(cmd, "@attendees", 1000, attendees); AddMax(cmd, "@agenda", agenda); AddText(cmd, "@by", 150, by);
        }, ct);
    }

    public async Task<bool> RecordMeetingAsync(long id, string status, string? recap, string? recordRef, CancellationToken ct)
    {
        if (status is not ("Held" or "Cancelled")) throw new ArgumentException("Record a meeting as Held or Cancelled.", nameof(status));
        return await ExecuteAsync(@"UPDATE opportunities.BdMeeting
SET Status = @status, Recap = COALESCE(@recap, Recap), RecordRef = COALESCE(@record, RecordRef), UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = @id;", cmd =>
        {
            cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id; AddText(cmd, "@status", 20, status);
            AddMax(cmd, "@recap", recap); AddText(cmd, "@record", 500, recordRef);
        }, ct).ConfigureAwait(false) == 1;
    }

    public Task<IReadOnlyList<BdReferenceRow>> ListReferencesAsync(CancellationToken ct) => ReadAsync(@"
SELECT r.Id, r.Application, r.Project, r.Role, r.Company, r.ContactName, r.Phone, r.Email, r.KorLead, s.DisplayName,
       r.LastCalledUtc, r.CalledBy, r.CallOutcome, r.Notes
FROM opportunities.BdReference r
LEFT JOIN opportunities.BdStaff s ON s.StaffKey = r.KorLead COLLATE SQL_Latin1_General_CP1_CI_AS
ORDER BY r.Application, r.Project, r.Id;", _ => { },
        r => new BdReferenceRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), Text(r, 4), Text(r, 5), Text(r, 6),
            Text(r, 7), Text(r, 8), Text(r, 9), When(r, 10), Text(r, 11), r.GetString(12), Text(r, 13)), ct);

    public async Task<bool> RecordReferenceCallAsync(long id, string outcome, string? contactName, string? notes, string by, CancellationToken ct)
    {
        if (!BdRhythmValues.CallOutcomes.Contains(outcome) || outcome == "NotCalled" || string.IsNullOrWhiteSpace(by)
            || contactName?.Length > 200 || notes?.Length > 1000)
            throw new ArgumentException("Record the call as Confirmed, LeftMessage, NoLongerThere or Declined.", nameof(outcome));
        return await ExecuteAsync(@"UPDATE opportunities.BdReference
SET CallOutcome = @outcome, LastCalledUtc = sysdatetimeoffset(), CalledBy = @by,
    ContactName = COALESCE(@name, ContactName), Notes = COALESCE(@notes, Notes), UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = @id;", cmd =>
        {
            cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id; AddText(cmd, "@outcome", 30, outcome); AddText(cmd, "@by", 150, by);
            AddText(cmd, "@name", 200, string.IsNullOrWhiteSpace(contactName) ? null : contactName.Trim());
            AddText(cmd, "@notes", 1000, string.IsNullOrWhiteSpace(notes) ? null : notes.Trim());
        }, ct).ConfigureAwait(false) == 1;
    }

    public async Task<DirectorHourRow?> GetHourAsync(string kind, DateOnly forDate, CancellationToken ct)
    {
        var rows = await ReadAsync(@"
SELECT TOP (1) Id, ForDate, Kind, ContentJson, WrittenBy, ImportedBy, ImportedAtUtc, EmailedAtUtc
FROM opportunities.DirectorHour WHERE Kind = @kind AND ForDate = @date ORDER BY ImportedAtUtc DESC, Id DESC;", cmd =>
        {
            AddText(cmd, "@kind", 20, kind); cmd.Parameters.Add("@date", SqlDbType.Date).Value = forDate.ToDateTime(TimeOnly.MinValue);
        }, r => new DirectorHourRow(r.GetInt64(0), DateOnly.FromDateTime(r.GetDateTime(1)), r.GetString(2), r.GetString(3),
            r.GetString(4), r.GetString(5), r.GetDateTimeOffset(6), When(r, 7)), ct).ConfigureAwait(false);
        return rows.Count == 0 ? null : rows[0];
    }

    public Task<long> ImportHourAsync(string kind, DateOnly forDate, string contentJson, string writtenBy, string importedBy, CancellationToken ct)
    {
        if (!BdRhythmValues.HourKinds.Contains(kind) || string.IsNullOrWhiteSpace(contentJson) || string.IsNullOrWhiteSpace(writtenBy)
            || string.IsNullOrWhiteSpace(importedBy))
            throw new ArgumentException("The hour needs a kind, its content, who wrote it and who imported it.", nameof(kind));
        return ScalarAsync(@"INSERT INTO opportunities.DirectorHour (ForDate, Kind, ContentJson, WrittenBy, ImportedBy)
OUTPUT INSERTED.Id VALUES (@date, @kind, @json, @written, @imported);", cmd =>
        {
            cmd.Parameters.Add("@date", SqlDbType.Date).Value = forDate.ToDateTime(TimeOnly.MinValue);
            AddText(cmd, "@kind", 20, kind); AddMax(cmd, "@json", contentJson);
            AddText(cmd, "@written", 150, writtenBy); AddText(cmd, "@imported", 150, importedBy);
        }, ct);
    }

    public Task MarkHourEmailedAsync(long id, CancellationToken ct) => ExecuteAsync(
        "UPDATE opportunities.DirectorHour SET EmailedAtUtc = sysdatetimeoffset() WHERE Id = @id AND EmailedAtUtc IS NULL;",
        cmd => cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id, ct);

    public Task<IReadOnlyList<BdLeadRow>> ListLeadsAsync(CancellationToken ct) => ReadAsync(@"
SELECT l.StaffKey, COALESCE(s.DisplayName, l.StaffKey), l.Role, l.Region, l.SortOrder
FROM opportunities.BdLead l
LEFT JOIN opportunities.BdStaff s ON s.StaffKey = l.StaffKey COLLATE SQL_Latin1_General_CP1_CI_AS
WHERE l.IsActive = 1 ORDER BY l.SortOrder, l.StaffKey;", _ => { },
        r => new BdLeadRow(r.GetString(0), r.GetString(1), r.GetString(2), Text(r, 3), r.GetInt32(4)), ct);

    public Task<IReadOnlyList<HourMoveResult>> ListMoveResultsAsync(long hourId, CancellationToken ct) => ReadAsync(@"
SELECT MoveRank, Result, Note, RecordedAtUtc FROM opportunities.DirectorHourMove WHERE HourId = @hour ORDER BY MoveRank;",
        cmd => cmd.Parameters.Add("@hour", SqlDbType.BigInt).Value = hourId,
        r => new HourMoveResult(r.GetInt32(0), r.GetString(1), Text(r, 2), r.GetDateTimeOffset(3)), ct);

    public async Task RecordMoveResultAsync(long hourId, int rank, string result, string? note, string by, CancellationToken ct)
    {
        if (!BdRhythmValues.MoveResults.Contains(result) || rank is < 1 or > 7 || string.IsNullOrWhiteSpace(by) || note?.Length > 500)
            throw new ArgumentException("A move result is Done, NotToday or NotDoing, for moves 1 to 7, with who recorded it.", nameof(result));
        await ExecuteAsync(@"
UPDATE opportunities.DirectorHourMove SET Result = @result, Note = @note, RecordedBy = @by, RecordedAtUtc = sysdatetimeoffset()
WHERE HourId = @hour AND MoveRank = @rank;
IF @@ROWCOUNT = 0
    INSERT INTO opportunities.DirectorHourMove (HourId, MoveRank, Result, Note, RecordedBy) VALUES (@hour, @rank, @result, @note, @by);", cmd =>
        {
            cmd.Parameters.Add("@hour", SqlDbType.BigInt).Value = hourId; cmd.Parameters.Add("@rank", SqlDbType.Int).Value = rank;
            AddText(cmd, "@result", 20, result); AddText(cmd, "@note", 500, note); AddText(cmd, "@by", 150, by);
        }, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<T>> ReadAsync<T>(string sql, Action<SqlCommand> bind, Func<SqlDataReader, T> map, CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
        bind(cmd);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var rows = new List<T>();
        while (await r.ReadAsync(ct).ConfigureAwait(false)) rows.Add(map(r));
        return rows;
    }

    private async Task<long> ScalarAsync(string sql, Action<SqlCommand> bind, CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
        bind(cmd);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<int> ExecuteAsync(string sql, Action<SqlCommand> bind, CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
        bind(cmd);
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static void AddText(SqlCommand cmd, string name, int size, string? value) =>
        cmd.Parameters.Add(name, SqlDbType.NVarChar, size).Value = (object?)value ?? DBNull.Value;

    private static void AddMax(SqlCommand cmd, string name, string? value) =>
        cmd.Parameters.Add(name, SqlDbType.NVarChar, -1).Value = (object?)value ?? DBNull.Value;

    private static string? Text(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static long? Id(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt64(i);
    private static DateTimeOffset? When(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDateTimeOffset(i);
}

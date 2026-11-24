using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Kor.Operations.Framework.Capture;

namespace Kor.Operations.Framework.Enforce;

/// <summary>Append-only role history in brain.Event. No migration, LaneState column, or change to kor_handover's writer.
/// Explicit role events and handover opened events compete by AtUtc then Id. The handover's Session column belongs to
/// the outgoing session; its opened Detail has no session key. An unrelated/free-form role is not guessed into worker.
/// </summary>
public static class SessionRoles
{
    public sealed record Role(string Name, string Source, int? EventId = null, DateTimeOffset? AtUtc = null);
    public static bool ValidSession(string? s) => s is not null && Regex.IsMatch(s, @"\A[A-Za-z0-9_-]{1,64}\z", RegexOptions.CultureInvariant);
    public static string? DefaultRole(string? startFolder) => startFolder is not null
        && Regex.IsMatch(startFolder.Replace('\\', '/').TrimEnd('/'), @"/Framework(?:/.*)?\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
        ? "coordinator" : null;

    internal const string SelectSql = """
        SELECT TOP (1) Id, AtUtc, Ref, Outcome,
          JSON_VALUE(CASE WHEN ISJSON(Detail)=1 THEN Detail ELSE N'{}' END, '$.role') AS Role
        FROM brain.Event
        WHERE Kind=N'gate'
          AND (Session=@roleSession OR JSON_VALUE(CASE WHEN ISJSON(Detail)=1 THEN Detail ELSE N'{}' END, '$.session')=@roleSession)
          AND (Ref IN (N'role:worker', N'role:coordinator')
            OR (Outcome=N'opened' AND Ref LIKE N'handover %'
              AND COALESCE(JSON_VALUE(CASE WHEN ISJSON(Detail)=1 THEN Detail ELSE N'{}' END, '$.test'), N'false') <> N'true'
              AND LOWER(JSON_VALUE(CASE WHEN ISJSON(Detail)=1 THEN Detail ELSE N'{}' END, '$.role')) IN (N'worker',N'coordinator',N'framework coordinator')))
        ORDER BY AtUtc DESC, Id DESC;
        """;

    public static Role? Read(BrainDb db, string session)
    {
        if (!ValidSession(session)) throw new ArgumentException("role needs a session id of 1-64 letters, digits, underscores or hyphens");
        using var command = db.Command(SelectSql, 5);
        command.Parameters.Add("@roleSession", SqlDbType.NVarChar, 64).Value = session;
        using var reader = command.ExecuteReader();
        return ReadRow(reader);
    }

    public static IReadOnlyList<Role> History(BrainDb db, string session)
    {
        if (!ValidSession(session)) throw new ArgumentException("Invalid session id");
        using var command = db.Command(SelectSql.Replace("TOP (1) ", "", StringComparison.Ordinal)
            .Replace("ORDER BY AtUtc DESC, Id DESC", "ORDER BY AtUtc, Id", StringComparison.Ordinal), 5);
        command.Parameters.Add("@roleSession", SqlDbType.NVarChar, 64).Value = session;
        using var reader = command.ExecuteReader();
        var history = new List<Role>();
        while (ReadRow(reader) is { } role) history.Add(role);
        return history;
    }

    internal static Role? ReadRow(SqlDataReader reader)
    {
        if (!reader.Read()) return null;
        var reference = reader.GetString(2);
        var explicitRole = reference.StartsWith("role:", StringComparison.Ordinal);
        var role = explicitRole ? reference[5..] : reader.GetString(4).ToLowerInvariant();
        if (role == "framework coordinator") role = "coordinator";
        return new Role(role, $"brain.Event #{reader.GetInt32(0)} ({(explicitRole ? reference : "handover opened")})", reader.GetInt32(0), reader.GetDateTimeOffset(1));
    }

    public static int Set(BrainDb db, string session, string role, string? because)
    {
        if (!ValidSession(session)) throw new ArgumentException("role needs a valid --session id");
        if (role is not ("worker" or "coordinator")) throw new ArgumentException("role set takes worker or coordinator");
        return BrainStore.Insert(new BrainEvent("Framework", "gate", null, null, "role:" + role, "set",
            JsonSerializer.Serialize(new { session, role, because }), "kor role", session), db);
    }
}

using System.Data;
using Kor.Operations.Framework.Capture;

namespace Kor.Operations.Framework.Stall;

/// <summary>
/// G1's two database touches. The DIAGNOSIS read (brain login): a brain.Memory row of a diagnosis kind
/// (<see cref="StallGuard.DiagnosisKinds"/>) created after the second failure whose Name, Description or Body holds the
/// target's name - read only when a refusal is pending, never on an ordinary call. The REFUSAL row (Board login):
/// oversight.GuardRefusal, Guard 'kor-stall', Rule 'G1 &lt;class&gt; &lt;name&gt;', Matched through SecretGuard - as kor enforce logs
/// its own, so every refusal is counted and can be judged.
/// </summary>
public static class StallStore
{
    public const string Guard = "kor-stall";

    /// <summary>"brain.Memory #id (kind)" of the newest diagnosis naming <paramref name="name"/> made after
    /// <paramref name="after"/>, or null.</summary>
    public static string? Diagnosis(BrainDb db, string name, DateTimeOffset after)
    {
        using var c = db.Command(
            "SELECT TOP (1) Id, Kind FROM brain.Memory WHERE CreatedAtUtc > @after AND Kind IN (N'finding', N'dead-end', N'lesson', N'feedback') " +
            "AND (Name LIKE @p ESCAPE N'\\' OR Description LIKE @p ESCAPE N'\\' OR Body LIKE @p ESCAPE N'\\') ORDER BY Id DESC", 5);
        c.Parameters.Add("@after", SqlDbType.DateTimeOffset).Value = after;
        c.Parameters.Add("@p", SqlDbType.NVarChar, 400).Value = "%" + Like(name) + "%";
        using var r = c.ExecuteReader();
        return r.Read() ? $"brain.Memory #{r.GetInt32(0)} ({r.GetString(1)})" : null;
    }

    /// <summary>A LIKE pattern's text with its wildcards escaped (\ is the escape).</summary>
    public static string Like(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");

    public static int LogRefusal(BrainDb board, StallDecision d, string? session, string? repo, string matched)
    {
        using var c = board.Command(
            "DECLARE @id TABLE (Id int); INSERT oversight.GuardRefusal (Guard, [Rule], [Session], Repo, Matched) OUTPUT INSERTED.Id INTO @id " +
            "VALUES (@g, @r, @s, @repo, @m); SELECT Id FROM @id;", 3);
        c.Parameters.Add("@g", SqlDbType.NVarChar, 60).Value = Guard;
        c.Parameters.Add("@r", SqlDbType.NVarChar, 200).Value = BrainStore.Clip($"G1 {d.Second?.Class} {d.Target?.Name}", 200) ?? "G1";
        c.Parameters.Add("@s", SqlDbType.NVarChar, 80).Value = (object?)BrainStore.Clip(session, 80) ?? DBNull.Value;
        c.Parameters.Add("@repo", SqlDbType.NVarChar, 80).Value = (object?)BrainStore.Clip(repo, 80) ?? DBNull.Value;
        c.Parameters.Add("@m", SqlDbType.NVarChar, 400).Value = SecretGuard.ForGuardLog(matched);
        return Convert.ToInt32(c.ExecuteScalar());
    }
}

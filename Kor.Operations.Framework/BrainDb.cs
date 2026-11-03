using Microsoft.Data.SqlClient;

namespace Kor.Operations.Framework;

/// <summary>
/// THE one KorStandards unit of work for the Knowledge IDE (Framework docs/KNOWLEDGE-IDE-SPEC-K5-K6.md, shared plumbing;
/// the K4-K7 spec's OpenRolledBack is the same class): <see cref="Open"/> opens a connection WITH a transaction; every
/// command runs on it through <see cref="Command"/>; <see cref="Commit"/> keeps the work; Dispose without Commit ROLLS
/// BACK. The installed kor.exe opens one per verb and commits it. A test opens <see cref="OpenRolledBack"/>, whose
/// Commit throws: whatever a verb under test does, nothing it writes to the live KorStandards is kept.
/// A trigger or constraint error can doom the transaction: a test that expects a refusal uses a BrainDb of its own.
/// </summary>
public sealed class BrainDb : IDisposable
{
    public SqlConnection Cn { get; }
    public SqlTransaction Tx { get; }

    /// <summary>True for a test's unit of work: it can never be committed.</summary>
    public bool RolledBackOnly { get; }

    private bool _committed;

    private BrainDb(SqlConnection cn, SqlTransaction tx, bool rolledBackOnly)
    {
        Cn = cn;
        Tx = tx;
        RolledBackOnly = rolledBackOnly;
    }

    public static BrainDb Open(KorStandardsConnection.Login login = KorStandardsConnection.Login.Brain)
    {
        var cn = KorStandardsConnection.Open(login);
        try { return new BrainDb(cn, cn.BeginTransaction(), rolledBackOnly: false); }
        catch { cn.Dispose(); throw; }
    }

    /// <summary>For tests: a unit of work whose <see cref="Commit"/> throws, so Dispose always rolls it back.</summary>
    public static BrainDb OpenRolledBack(KorStandardsConnection.Login login = KorStandardsConnection.Login.Brain)
    {
        var cn = KorStandardsConnection.Open(login);
        try { return new BrainDb(cn, cn.BeginTransaction(), rolledBackOnly: true); }
        catch { cn.Dispose(); throw; }
    }

    public SqlCommand Command(string sql, int timeoutSeconds = 30) =>
        new(sql, Cn, Tx) { CommandTimeout = timeoutSeconds };

    public void Commit()
    {
        if (RolledBackOnly) throw new InvalidOperationException("this BrainDb is a test's: it is never committed");
        Tx.Commit();
        _committed = true;
    }

    public void Dispose()
    {
        if (!_committed)
        {
            try { Tx.Rollback(); } catch { /* the connection may already be gone; nothing was committed */ }
        }
        Tx.Dispose();
        Cn.Dispose();
    }

    /// <summary>
    /// Runs <paramref name="work"/> on the caller's unit of work, or on one of its own that it commits (the installed
    /// kor.exe). The caller's is never committed here.
    /// </summary>
    public static T Use<T>(BrainDb? db, Func<BrainDb, T> work)
    {
        if (db is not null) return work(db);   // the caller's transaction: a deadlock dooms it, the caller decides
        return RetryOnceOnDeadlock(() =>
        {
            using var own = Open();
            var result = work(own);
            own.Commit();
            return result;
        }, IsDeadlock, Thread.Sleep);
    }

    /// <summary>
    /// A kor hook write that is chosen as a deadlock victim (SQL 1205 - 3 Nov: migration 173's first APPLY deadlocked
    /// against a live hook writing brain.Event) is run ONCE more, whole, in a new unit of work, after a random 200-600 ms.
    /// A second failure is thrown: hook mode logs it and exits 0 (fresh prints its COULD NOT CHECK line).
    /// </summary>
    public static T RetryOnceOnDeadlock<T>(Func<T> attempt, Func<Exception, bool> isDeadlock, Action<TimeSpan> sleep)
    {
        try { return attempt(); }
        catch (Exception e) when (isDeadlock(e))
        {
            sleep(TimeSpan.FromMilliseconds(Random.Shared.Next(200, 601)));
            return attempt();
        }
    }

    /// <summary>SQL error 1205 (this transaction was the deadlock victim), anywhere in the exception's errors.</summary>
    public static bool IsDeadlock(Exception e) =>
        e is SqlException s && (s.Number == 1205 || s.Errors.Cast<SqlError>().Any(x => x.Number == 1205));
}

using System.Reflection;
using Kor.Operations.Framework.Cli;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// A kor hook write chosen as a deadlock victim (SQL 1205) is retried ONCE (BrainDb.RetryOnceOnDeadlock - the lead's
/// item after migration 173's first APPLY deadlocked against a live hook). Proved with an INJECTED 1205: a SqlException
/// built the way SqlClient builds one (it has no public constructor), never a real deadlock.
/// Covers: one 1205 then success -> the result, after one 200-600 ms pause; two 1205s -> thrown; any other error -> not
/// retried; the fresh hook's line when the retry also fails. Does NOT cover: a deadlock inside a CALLER's transaction (a
/// test's BrainDb) - that transaction is doomed and is never retried.
/// </summary>
public class DeadlockRetryTests
{
    /// <summary>A SqlException carrying one SqlError with the given number, made through SqlClient's own internal factories.</summary>
    internal static SqlException SqlError(int number, string message = "Transaction was deadlocked - injected by the test")
    {
        const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
        var error = typeof(SqlError).GetConstructors(Any)
            .OrderByDescending(c => c.GetParameters().Length)
            .Select(c => (c, p: c.GetParameters()))
            .Select(x => x.c.Invoke(x.p.Select(p => p.Name switch
            {
                "infoNumber" => (object)number,
                "errorState" or "errorClass" => p.ParameterType == typeof(byte) ? (object)(byte)13 : (object)13,
                "server" => "test",
                "errorMessage" => message,
                "procedure" => "",
                "lineNumber" => 0,
                "win32ErrorCode" => p.ParameterType == typeof(uint) ? (object)0u : (object)0,
                "exception" => null!,
                "batchIndex" => -1,
                _ => p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType)! : null!,
            }).ToArray()))
            .First();
        var errors = (SqlErrorCollection)typeof(SqlErrorCollection).GetConstructors(Any).First().Invoke(null);
        typeof(SqlErrorCollection).GetMethod("Add", Any)!.Invoke(errors, new[] { error });
        var create = typeof(SqlException).GetMethods(Any)
            .Where(m => m.Name == "CreateException" && m.GetParameters().Length == 2
                        && m.GetParameters()[0].ParameterType == typeof(SqlErrorCollection) && m.GetParameters()[1].ParameterType == typeof(string))
            .Single();
        return (SqlException)create.Invoke(null, new object[] { errors, "16.0" })!;
    }

    [Fact]
    public void TheInjectedExceptionIsReallyA1205()
    {
        var e = SqlError(1205);
        Assert.Equal(1205, e.Number);
        Assert.True(BrainDb.IsDeadlock(e));
        Assert.False(BrainDb.IsDeadlock(SqlError(547)));                       // a constraint refusal is not retried
        Assert.False(BrainDb.IsDeadlock(new InvalidOperationException("x")));
    }

    [Fact]
    public void OneDeadlockIsRetriedOnceAfterAShortRandomPause()
    {
        var calls = 0;
        var slept = new List<TimeSpan>();
        var r = BrainDb.RetryOnceOnDeadlock(() => ++calls == 1 ? throw SqlError(1205) : 42, BrainDb.IsDeadlock, slept.Add);
        Assert.Equal((42, 2), (r, calls));
        var pause = Assert.Single(slept);
        Assert.InRange(pause.TotalMilliseconds, 200, 600);
    }

    [Fact]
    public void ASecondDeadlockIsThrownAndAnyOtherErrorIsNeverRetried()
    {
        var calls = 0;
        var e = Assert.Throws<SqlException>(() => BrainDb.RetryOnceOnDeadlock<int>(() => { calls++; throw SqlError(1205); }, BrainDb.IsDeadlock, _ => { }));
        Assert.Equal((1205, 2), (e.Number, calls));
        calls = 0;
        Assert.Throws<SqlException>(() => BrainDb.RetryOnceOnDeadlock<int>(() => { calls++; throw SqlError(547); }, BrainDb.IsDeadlock, _ => { }));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void WhenTheRetryAlsoFailsTheFreshHookStillExitsZeroWithOneLine()
    {
        // The fresh hook's check, failing with 1205 on both attempts, reaches the hook's catch: one COULD NOT CHECK line.
        var calls = 0;
        var output = new StringWriter();
        var log = Path.Combine(Path.GetTempPath(), $"kor-1205-{Guid.NewGuid():N}.log");
        var code = Program.RunFreshHookForTests(() => BrainDb.RetryOnceOnDeadlock<Records.FreshnessReport>(
            () => { calls++; throw SqlError(1205); }, BrainDb.IsDeadlock, _ => { }), "Framework", output, log);
        Assert.Equal((0, 2), (code, calls));
        var line = Assert.Single(output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        Assert.StartsWith("kor fresh: COULD NOT CHECK - Transaction was deadlocked", line);
        Assert.EndsWith("(logged)", line);
        File.Delete(log);
    }
}

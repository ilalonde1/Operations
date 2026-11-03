using Kor.Operations.Framework;
using Kor.Operations.Framework.Cli;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>K2 - the kor.exe skeleton. The live test reads the REAL KorStandards (no stand-in DB).</summary>
public class SkeletonTests
{
    [Fact]
    public void VersionPrintsKor()
    {
        var output = new StringWriter();
        Assert.Equal(0, Program.Run(new[] { "--version" }, output, new StringWriter()));
        Assert.StartsWith("kor ", output.ToString());
    }

    [Fact]
    public void UnknownVerbIsUsageWithoutHook()
    {
        Assert.Equal(2, Program.Run(new[] { "no-such-verb" }, new StringWriter(), new StringWriter()));
    }

    [Fact]
    public void HookModeNeverFails()
    {
        // A hook must never fail the session: unknown verb and a verb that throws both exit 0 in --hook mode.
        Assert.Equal(0, Program.Run(new[] { "no-such-verb", "--hook" }, new StringWriter(), new StringWriter()));
    }

    [Fact]
    public void ParsesTheEmptyKeyPasswordQuirk()
    {
        // The opportunities connection string carries its password under an EMPTY key (oq.py, standards_db.py).
        var b = KorStandardsConnection.Parse("Server=KOR-APP01\\SQLEXPRESS;Database=X;User Id=u;=secret-value;");
        Assert.Equal("KOR-APP01\\SQLEXPRESS", b.DataSource);
        Assert.Equal("u", b.UserID);
        Assert.Equal("secret-value", b.Password);
        Assert.Equal(15, b.ConnectTimeout);
    }

    [Fact]
    public void ParseRefusesAMissingPart()
    {
        var e = Assert.Throws<InvalidOperationException>(() => KorStandardsConnection.Parse("Server=s;Database=d;User Id=u;"));
        Assert.DoesNotContain("secret", e.Message);
    }

    [Fact]
    public void MissingVariableNamesTheVariableNotThePassword()
    {
        Assert.Null(KorStandardsConnection.ReadVariable("KOR_NO_SUCH_VARIABLE_" + Guid.NewGuid().ToString("N")));
    }

    [Fact]
    public void PingReachesTheLiveKorStandardsWithBothLogins()
    {
        // Live: korbrain_app reads brain.vw_Memory, opportunities_app reads oversight.vw_Board.
        var results = Ping.Run();
        Assert.All(results, r => Assert.True(r.Ok, $"{r.Login}: {r.Detail}"));
    }
}

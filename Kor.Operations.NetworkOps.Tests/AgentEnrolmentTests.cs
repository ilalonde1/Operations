#nullable enable
using Kor.Operations.NetworkOps.Service.Agents;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// PCs outside the domain enrol themselves with a one-time code (Ian, 2026-10-02: the Boardroom PC, "It's not domain
// joined"; "do boardroom and non domain PCs -- autonomously").
//
// WHAT IT COVERS: a code works once, for the one PC name it was issued for, within the hour; a right code from the wrong
// name does not burn it; a burst of wrong guesses shuts the door for a minute; one live code per PC (re-issuing voids the
// old); the code reads without 0/O/1/I and is accepted with or without its dashes; PC names are Windows names; the
// command an administrator runs carries the pin, the package address and the code and nothing else from outside; only
// the two before-key routes skip the agent gate.
// WHAT IT DOES NOT: the agent's own install on the PC (Agent/Enrol.cs: sc.exe, folder ACLs) -- run for real on the
// Boardroom PC; the SQL that adds the device (AddManualPcAsync) -- the directory sync's own SQL retires only Source 'AD'.
// A SAME-CLASS FAULT IT WOULD NOT CATCH: a service restart between issue and use voids the code (in memory by design);
// the PC then says "wrong, used or expired" and a new one is issued.
public sealed class AgentEnrolmentTests
{
    private static readonly DateTime T0 = new(2026, 10, 3, 6, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_code_works_once_for_its_own_PC_within_the_hour()
    {
        var e = new AgentEnrolment();
        var code = e.Issue(77, "BOARDROOM", "ilalonde", T0).Code;
        Assert.Matches("^[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$", code);
        Assert.Null(e.Redeem("KOR-207", code, T0.AddMinutes(1)));                      // another PC: refused...
        Assert.Equal(77, e.Redeem("boardroom", code.Replace("-", "").ToLowerInvariant(), T0.AddMinutes(2))!.Value.DeviceId);   // ...and not burnt
        Assert.Null(e.Redeem("BOARDROOM", code, T0.AddMinutes(3)));                    // spent

        var late = e.Issue(77, "BOARDROOM", "ilalonde", T0).Code;
        Assert.Null(e.Redeem("BOARDROOM", late, T0 + AgentEnrolment.Lifetime + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Re_issuing_voids_the_old_code_and_guessing_is_shut_out()
    {
        var e = new AgentEnrolment();
        var first = e.Issue(5, "BOARDROOM", "a", T0).Code;
        var second = e.Issue(5, "BOARDROOM", "a", T0).Code;
        Assert.Null(e.Redeem("BOARDROOM", first, T0));
        for (var i = 0; i < 10; i++) e.Redeem("BOARDROOM", "AAAA-AAAA-AAA" + i, T0);
        Assert.Null(e.Redeem("BOARDROOM", second, T0));                                 // the right code, in a burst of wrong ones
        Assert.Equal(5, e.Redeem("BOARDROOM", second, T0.AddMinutes(2))!.Value.DeviceId);   // a minute later it works
    }

    [Theory]
    [InlineData("BOARDROOM", true)]
    [InlineData("KOR-PERFORM3", true)]
    [InlineData("A-NAME-TOO-LONG-1", false)]
    [InlineData("BOARD ROOM", false)]
    [InlineData("-X", false)]
    [InlineData("", false)]
    public void Only_a_Windows_computer_name_is_taken(string name, bool ok) => Assert.Equal(ok, AgentEnrolment.IsPcName(name));

    [Fact]
    public void The_command_trusts_only_the_pin_and_carries_the_code()
    {
        var pin = "EBAE9FE81D92623E1EED3C1F9C8F4ED7D5750A8BAE27522CE9F2CE1A3738EA6F";
        var cmd = AgentEnrolment.Command("https://KOR-APP01.int.korstructural.com:8445/", pin, "ABCD-EFGH-JKLM");
        Assert.Contains($"$pin='{pin}'", cmd);
        Assert.Contains("'https://KOR-APP01.int.korstructural.com:8445/agent/v1/package'", cmd);
        Assert.EndsWith("--enrol ABCD-EFGH-JKLM", cmd);
        Assert.DoesNotContain("password", cmd, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Only_the_two_before_key_routes_skip_the_agent_gate()
    {
        Assert.True(AgentApi.IsBeforeKey("/agent/v1/enrol"));
        Assert.True(AgentApi.IsBeforeKey("/agent/v1/package"));
        Assert.False(AgentApi.IsBeforeKey("/agent/v1/poll"));
        Assert.False(AgentApi.IsBeforeKey("/agent/v1/jobs/1/result"));
        Assert.False(AgentApi.IsBeforeKey("/agent/v1/enrol/x"));
    }
}

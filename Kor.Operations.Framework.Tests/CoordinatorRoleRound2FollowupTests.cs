using System.Text.Json;
using Kor.Operations.Framework.Enforce;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>Follow-up gates found while inspecting replay decisions. D remains frozen. Checks command data vs targets,
/// option placement, shared snapshots and approval rotation. Does not model arbitrary executable side effects.</summary>
public sealed class CoordinatorRoleRound2FollowupTests
{
    [Theory]
    [InlineData("Bash", "q='select 1'; for s in 1 2; do q=\"$q WHERE LEN(Body)>=$s;\"; done; python -c 'print(1)'", false)]
    [InlineData("Bash", "kor --session self role set worker", true)]
    [InlineData("Bash", "kor role --session self set worker", true)]
    [InlineData("Bash", "kor role set worker --session other --session self", true)]
    [InlineData("Bash", "kor role set worker --session self --session other", false)]
    [InlineData("Bash", "dotnet C:/tools/kor.dll role set worker --session self", true)]
    [InlineData("Bash", "dotnet exec C:/tools/kor.dll role set worker --session self", true)]
    [InlineData("Bash", "X=1 python -I C:/project/build_bundle.py", true)]
    [InlineData("Bash", "X=1 kor role set worker --session self", true)]
    [InlineData("Bash", "python -I 'C:/VIsual Studio Projects/Operations/work/obj/script.py'", true)]
    [InlineData("Bash", "X=/tmp; echo x > '$X/file'", true)]
    [InlineData("Bash", "X=/tmp; X=$X/probe; echo x > $X/file", false)]
    [InlineData("Bash", "X=/tmp; for i in 1; do X=C:/project; done; echo x > $X/file", true)]
    [InlineData("PowerShell", "'x' > ${env:TEMP}/probe.txt", false)]
    [InlineData("PowerShell", "$X = $(Get-Location); Set-Content $X/x x", true)]
    [InlineData("Bash", "echo x > /tmpish/file", true)]
    [InlineData("Bash", "echo x > C:/tmpish/file", true)]
    [InlineData("Bash", "rm /project", true)]
    [InlineData("Bash", "X=/tmp; echo x > $X/first; for i in 1; do X=C:/project; done", false)]
    [InlineData("Bash", "for i in 1; do X=C:/project; done; X=/tmp; echo x > $X/last", false)]
    public void Decisions(string tool, string command, bool refuse)
    {
        var d = CoordinatorRoleGuard.Evaluate(JsonSerializer.Serialize(new { tool_name = tool, tool_input = new { command },
            session_id = "self", cwd = "C:/Framework" }), null, null);
        Assert.True(d.Refuse == refuse, d.ToString());
    }

    [Fact]
    public void SharedChecksAreUsedAndNotCopiedToSessionFiles()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "r2-cache-" + Guid.NewGuid().ToString("N"));
        try
        {
            var now = DateTimeOffset.UtcNow;
            var checks = new List<LiveCheck> { new(123, 456, "lesson", "fixture", "Office", "Bash", "command", "bad",
                new[] { "bad" }, new[] { "good" }, "refuse bad") };
            CheckCache.Save(root, new(now, checks));
            var roles = 0;
            var first = CheckCache.GetSession(root, "fresh", "C:/Framework", () => throw new Exception("must reuse shared checks"),
                now, e => throw e, out _, () => { roles++; return new("coordinator", "test"); });
            Assert.Equal(123, Assert.Single(first.Checks!).Id);
            Assert.Null(CheckCache.LoadRole(root, "fresh")!.Checks);
            Assert.Equal(1, roles);
            var offline = CheckCache.GetSession(root, "new-offline", "C:/Framework", () => throw new IOException("offline"),
                now.AddMinutes(1), _ => { }, out _);
            Assert.Equal(123, Assert.Single(offline.Checks!).Id);
            var down = CheckCache.GetSession(root, "new-during-backoff", "C:/Framework", () => throw new Exception("must honor backoff"),
                now.AddSeconds(61), e => throw e, out _);
            Assert.Equal(123, Assert.Single(down.Checks!).Id);
            Assert.Null(down.Role);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}

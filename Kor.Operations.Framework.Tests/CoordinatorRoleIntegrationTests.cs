using System.Text.Json;
using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Cli;
using Kor.Operations.Framework.Enforce;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>Role storage, real enforce wiring, refusal/lift rows and rollback, origin/cache state, and typed approval
/// provenance. All SQL mutations use OpenRolledBack. Files stay under this test project's build directory. Does not test
/// Claude honoring hook JSON, cross-machine cache coherence, or arbitrary program effects.</summary>
[Collection(LiveDb.Name)]
public sealed class CoordinatorRoleIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "role-test-" + Guid.NewGuid().ToString("N"));
    private readonly string _session = "zz-role-" + Guid.NewGuid().ToString("N");
    public CoordinatorRoleIntegrationTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    private (int Code, string Text, string Errors) Run(BrainDb? db, BrainDb? board, string? input, params string[] args)
    {
        var output = new StringWriter(); var error = new StringWriter();
        var all = args.Concat(new[] { "--cache-dir", _root, "--prompts-dir", _root }).ToArray();
        var code = Program.Run(all, () => input, output, error, Path.Combine(_root, "hook-errors.log"), db, board);
        return (code, output.ToString(), error.ToString());
    }

    private string Hook(string? agent = null, string? transcript = null, string cwd = "C:/VIsual Studio Projects/Framework") =>
        JsonSerializer.Serialize(new { tool_name = "Edit", tool_input = new { file_path = "C:/VIsual Studio Projects/Operations/src/role-test.cs" },
            session_id = _session, cwd, agent_id = agent, transcript_path = transcript });

    [Fact]
    public void SetShowLatestHandoverAndEnforceRefusalUseRolledBackEvents()
    {
        using (var db = BrainDb.OpenRolledBack())
        using (var board = BrainDb.OpenRolledBack(KorStandardsConnection.Login.Board))
        {
            Assert.Null(SessionRoles.Read(db, _session));
            var denied = Run(db, board, Hook(), "enforce", "--module", "zz-role-test", "--hook");
            Assert.Contains("Coordinator role refuses", denied.Text);
            Assert.Contains(CoordinatorRoleGuard.Approval, denied.Text);
            Assert.Contains("\"permissionDecision\":\"deny\"", denied.Text);
            Assert.Equal(1, Count(board, "oversight.GuardRefusal"));
            var set = Run(db, board, null, "role", "set", "worker", "--session", _session, "--because", "test worker");
            Assert.True(set.Code == 0, set.Errors);
            var shown = Run(db, board, null, "role", "show", "--session", _session);
            Assert.Contains("worker; source: brain.Event #", shown.Text);
            Assert.Equal("worker", CheckStore.LiveForSession(db, _session).Role!.Name);
            Assert.DoesNotContain("Coordinator role refuses", Run(db, board, Hook(), "enforce", "--module", "zz-role-test", "--hook").Text);

            var handover = BrainStore.Insert(new BrainEvent("Framework", "gate", null, null, "handover #123 coordinator", "opened",
                JsonSerializer.Serialize(new { role = "Framework coordinator", from = "lead", test = false }), "kor_handover", _session), db);
            Assert.Equal(handover, SessionRoles.Read(db, _session)!.EventId);
            Assert.Equal("coordinator", SessionRoles.Read(db, _session)!.Name);
            Assert.Contains("Coordinator role refuses", Run(db, board, Hook(cwd: "C:/other"), "enforce", "--module", "zz-role-test", "--hook").Text);
            Assert.DoesNotContain("Coordinator role refuses", Run(db, board, Hook(agent: "helper"), "enforce", "--module", "zz-role-test", "--hook").Text);
            Assert.Equal(2, SessionRoles.History(db, _session).Count);
            Assert.Equal(2, Count(board, "oversight.GuardRefusal"));
            var later = SessionRoles.Set(db, _session, "worker", "latest wins");
            BrainStore.Insert(new BrainEvent("Framework", "gate", null, null, "handover #124 coordinator", "opened",
                JsonSerializer.Serialize(new { role = "coordinator", test = true }), "kor_handover", _session), db);
            Assert.Equal(later, SessionRoles.Read(db, _session)!.EventId);
            Assert.Equal("worker", SessionRoles.Read(db, _session)!.Name);
            Assert.Throws<InvalidOperationException>(db.Commit);
        }
        AssertNoRowsRemain();
    }

    [Fact]
    public void LiftIsLoggedAndOnlyTheCoordinatorBoundaryIsLifted()
    {
        var now = DateTimeOffset.UtcNow;
        var transcript = WriteApproval(now, CoordinatorRoleGuard.Approval);
        using (var db = BrainDb.OpenRolledBack())
        using (var board = BrainDb.OpenRolledBack(KorStandardsConnection.Login.Board))
        {
            var result = Run(db, board, Hook(transcript: transcript), "enforce", "--module", "zz-role-test", "--hook");
            Assert.Equal("", result.Text);
            Assert.Equal("", result.Errors);
            Assert.Equal(0, Count(board, "oversight.GuardRefusal"));
            Assert.Equal(1, Count(db, "brain.Event"));
            using var command = db.Command("SELECT Ref,Outcome,Detail FROM brain.Event WHERE Session=@s");
            command.Parameters.AddWithValue("@s", _session);
            using var reader = command.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal("role:lift", reader.GetString(0));
            Assert.Equal("lifted", reader.GetString(1));
            Assert.Contains("expiresAtUtc", reader.GetString(2));
        }
        AssertNoRowsRemain();
    }

    [Theory]
    [InlineData("typed", "cli", "human", 0, false, true)]
    [InlineData("queued", "cli", "human", 11.99, false, true)]
    [InlineData("typed", "cli", "human", 12, false, false)]
    [InlineData("typed", "cli", "human", -1, false, false)]
    [InlineData("typed", "sdk", "human", 0, false, false)]
    [InlineData("typed", "cli", "peer", 0, false, false)]
    [InlineData("typed", "cli", "human", 0, true, false)]
    public void ApprovalProvenanceAndWindow(string source, string entry, string origin, double hours, bool side, bool allowed)
    {
        var now = DateTimeOffset.UtcNow;
        var path = WriteApproval(now.AddHours(-hours), CoordinatorRoleGuard.Approval, source, entry, origin, side);
        Assert.Equal(allowed, CoordinatorApproval.Find(_session, _root, path, now) is not null);
        Assert.Null(CoordinatorApproval.Find("other-session", _root, path, now));
    }

    [Theory]
    [InlineData("> IAN-APPROVED-COORDINATOR-WORK")]
    [InlineData("\"IAN-APPROVED-COORDINATOR-WORK\"")]
    [InlineData("```\nIAN-APPROVED-COORDINATOR-WORK\n```")]
    [InlineData("The file says IAN-APPROVED-COORDINATOR-WORK")]
    public void QuotedTokenNeverLifts(string prompt)
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Null(CoordinatorApproval.Find(_session, _root, WriteApproval(now, prompt), now));
    }

    [Fact]
    public void RecorderAloneToolResultMalformedOrMismatchedPromptCannotLift()
    {
        var now = DateTimeOffset.UtcNow;
        var path = WriteApproval(now, CoordinatorRoleGuard.Approval);
        File.WriteAllText(path, JsonSerializer.Serialize(new { type = "user", sessionId = _session, timestamp = now,
            entrypoint = "cli", promptSource = "typed", message = new { role = "user", content = new[] { new { type = "tool_result", content = CoordinatorRoleGuard.Approval } } } }));
        Assert.Null(CoordinatorApproval.Find(_session, _root, path, now));
        path = WriteApproval(now, CoordinatorRoleGuard.Approval);
        File.WriteAllText(Path.Combine(_root, _session + ".jsonl"), "{broken");
        Assert.Null(CoordinatorApproval.Find(_session, _root, path, now));
        File.WriteAllText(Path.Combine(_root, _session + ".jsonl"), JsonSerializer.Serialize(new { prompt = "different\n" + CoordinatorRoleGuard.Approval }));
        Assert.Null(CoordinatorApproval.Find(_session, _root, path, now));
    }

    [Fact]
    public void CacheReadsOncePerWindowPersistsOriginAndBacksOffWithoutLosingTheRole()
    {
        var now = DateTimeOffset.UtcNow;
        var reads = 0;
        (List<LiveCheck>, SessionRoles.Role?) Read() { reads++; return (new(), new("coordinator", "test")); }
        var first = CheckCache.GetSession(_root, _session, "C:/Framework", Read, now, _ => { }, out _);
        Assert.Equal("coordinator", first.Role!.Name);
        for (var i = 0; i < 20; i++) CheckCache.GetSession(_root, _session, "C:/worker", Read, now.AddSeconds(29), _ => { }, out _);
        Assert.Equal(1, reads);
        var refreshed = CheckCache.GetSession(_root, _session, "C:/worker", Read, now.AddSeconds(30), _ => { }, out _);
        Assert.Equal(2, reads);
        Assert.Equal("C:/Framework", refreshed.StartFolder);
        var errors = 0;
        var stale = CheckCache.GetSession(_root, _session, "C:/worker", () => throw new IOException("offline"), now.AddSeconds(60), _ => errors++, out _);
        Assert.Equal(1, errors);
        Assert.Equal("coordinator", stale.Role!.Name);
        CheckCache.GetSession(_root, _session, null, Read, now.AddSeconds(119), _ => { }, out _);
        Assert.Equal(2, reads);
        CheckCache.InvalidateRole(_root, _session);
        CheckCache.GetSession(_root, _session, null, Read, now.AddSeconds(120), _ => { }, out _);
        Assert.Equal(3, reads);
    }

    [Fact]
    public void ReplaySkipsDuplicateAndSidechainCallsAndKeepsTheFirstCwd()
    {
        var path = Path.Combine(_root, "replay.jsonl");
        string Line(string cwd, string id, bool side) => JsonSerializer.Serialize(new { type = "assistant", sessionId = _session, cwd, isSidechain = side,
            message = new { content = new[] { new { type = "tool_use", id, name = "Edit", input = new { file_path = "C:/repo/src/a.cs" } } } } });
        File.WriteAllLines(path, new[] { Line("C:/Framework", "a", false), Line("C:/other", "a", false), Line("C:/other", "b", false), Line("C:/Framework", "c", true) });
        using var db = BrainDb.OpenRolledBack();
        var replay = Run(db, null, null, "role", "replay", path);
        Assert.Equal(0, replay.Code);
        Assert.Contains("L1 REFUSE", replay.Text);
        Assert.Contains("L3 REFUSE", replay.Text);
        Assert.DoesNotContain("L2", replay.Text);
        Assert.DoesNotContain("L4", replay.Text);
        Assert.Contains("2 main-thread calls", replay.Text);
    }

    [Theory]
    [InlineData("C:/repo/bin/Kor.Operations.EngineeringTools.TakeoffCli.exe model-audit bundle")]
    [InlineData("./build_bundle_v3.py finish")]
    public void DirectProductEntrypointsAreRunsToo(string command)
    {
        var raw = JsonSerializer.Serialize(new { tool_name = "Bash", tool_input = new { command }, cwd = "C:/Framework", session_id = _session });
        Assert.True(CoordinatorRoleGuard.Evaluate(raw, null, null).Refuse);
    }

    [Fact]
    public void ShellWrapperWritesAreInspectedButTheAllowedInstallerIsNotReclassified()
    {
        Directory.CreateDirectory(Path.Combine(_root, "tools"));
        var wrapper = Path.Combine(_root, "wrapper.ps1");
        var install = Path.Combine(_root, "tools", "install-kor.ps1");
        File.WriteAllText(wrapper, "Set-Content -Path C:/repo/src/a.cs -Value x");
        File.WriteAllText(install, "Set-Content -Path C:/repo/src/a.cs -Value x");
        bool Refused(string path) => CoordinatorRoleGuard.Evaluate(JsonSerializer.Serialize(new { tool_name = "PowerShell",
            tool_input = new { command = "& '" + path + "'" }, cwd = _root, session_id = _session }), "coordinator", _root).Refuse;
        Assert.True(Refused(wrapper));
        Assert.False(Refused(install));
    }

    [Fact]
    public void OwnScratchRootCanBeCreatedAndDriveRootDoesNotBecomeDriveRelative()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "claude", "hash", _session, "scratchpad");
        var raw = JsonSerializer.Serialize(new { tool_name = "PowerShell", tool_input = new { command = "New-Item -ItemType Directory -Path '" + scratch + "'" },
            cwd = "C:/Framework", session_id = _session });
        Assert.False(CoordinatorRoleGuard.Evaluate(raw, null, null).Refuse);
        Assert.Equal("C:/", CoordinatorRoleGuard.Resolve("C:/", "C:/Framework"));
    }

    private string WriteApproval(DateTimeOffset at, string prompt, string source = "typed", string entry = "cli", string origin = "human", bool side = false)
    {
        File.WriteAllText(Path.Combine(_root, _session + ".jsonl"), JsonSerializer.Serialize(new { prompt }));
        var transcript = Path.Combine(_root, "transcript.jsonl");
        File.WriteAllText(transcript, JsonSerializer.Serialize(new { type = "user", sessionId = _session, timestamp = at, entrypoint = entry,
            promptSource = source, isSidechain = side, origin = new { kind = origin }, message = new { role = "user", content = prompt } }));
        return transcript;
    }
    private int Count(BrainDb db, string table)
    {
        using var command = db.Command($"SELECT COUNT(*) FROM {table} WHERE Session=@s");
        command.Parameters.AddWithValue("@s", _session);
        return Convert.ToInt32(command.ExecuteScalar());
    }
    private void AssertNoRowsRemain()
    {
        using var brain = BrainDb.OpenRead(KorStandardsConnection.Login.Brain, true);
        using var board = BrainDb.OpenRead(KorStandardsConnection.Login.Board, true);
        Assert.Equal(0, Count(brain, "brain.Event"));
        Assert.Equal(0, Count(board, "oversight.GuardRefusal"));
    }
}

#nullable enable
using System.Text.RegularExpressions;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Learning;
using Kor.Operations.NetworkOps.Core.Prompts;
using Kor.Operations.NetworkOps.Service.Prompts;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// The Prompt Library: Claude prompts written from the live database when opened, and the report-back that brings the
// session's outcome into the system.
//
// WHAT IT COVERS: a finding prompt carries the evidence, KOR's diagnosis, what has cleared it on the fleet, accepted
// learnings from earlier sessions, where else it is open, and how to reach the machine; every prompt ends with the
// report-back command for its own run and token (or, before 007, says to leave a note); every tool in the catalog has
// its brief embedded and every brief on disk is in the catalog; the token is stored only as a hash; the library's
// source reads no credential option, so no prompt can carry one.
// WHAT IT DOES NOT: the SQL (PromptRuns insert/outcome/single-use), the HTTP routes, or the live-state lines of a tool
// prompt -- those need APP01's database and are proven by rendering and reporting one live. A SAME-CLASS FAULT IT
// WOULD NOT CATCH: an outcome route accidentally moved inside the Entra group would refuse every session's report;
// only the live round trip shows the route is reachable with the token alone.
public sealed class PromptLibraryTests
{
    private static readonly DateTime Now = new(2026, 9, 30, 23, 0, 0, DateTimeKind.Utc);

    private static FleetFinding Finding(long id, string device, string rule, string title = "Webroot is blocking a KOR tool", string evidence = "WRSA quarantined KorTools.dll")
        => new(id, device, rule, Severity.Warning, title, evidence, Now.AddDays(-2), Now.AddHours(-1), null, null, null, null);

    private static DevicePromptInput Input(FleetFinding? focus, IReadOnlyList<SessionLearning>? sessions = null) => new(
        "KOR-302N", "Workstation",
        new Dictionary<string, string> { ["os.build"] = "26100.6584", ["app.Revit 2026"] = "26.4" },
        focus is null ? [] : [focus, Finding(2, "KOR-302N", "disk-low:C", "C: is nearly full", "8 GB free")],
        focus, [], [], [], [new NoteRow("ilalonde", Now.AddDays(-1), "Monitors are DisplayPort: off = no display")],
        ["KOR-304"], [], [new LearnedFix("webroot", "Webroot override added", 3, 4)],
        focus is null ? null : Knowledge.For(focus.RuleKey), sessions ?? [],
        new PromptAccess(false, "kevinw · active", true, "1.0.2", true, "https://kor-mesh01.int.korstructural.com/?gotonode=abc&viewmode=11", null),
        Now.AddMinutes(-20), Now);

    [Fact]
    public void A_finding_prompt_carries_what_the_system_knows_about_it()
    {
        var f = Finding(1, "KOR-302N", "webroot:KorTools.dll");
        var md = PromptComposer.Device(Input(f, [new SessionLearning(Now.AddDays(-3), "KOR-304", "The override must be global, not per-site")]),
            new PromptReport(41, "tok_abcdefghijklmnopqrstuvwxyz", "https://KOR-APP01.int.korstructural.com:8445"));

        Assert.Contains("Solve \"Webroot is blocking a KOR tool\" on KOR-302N", md);
        Assert.Contains("WRSA quarantined KorTools.dll", md);
        Assert.Contains("Webroot override added (3 of 4 times)", md);
        Assert.Contains("accepted by Ian): The override must be global, not per-site", md);
        Assert.Contains("open on: KOR-304", md);
        Assert.Contains("C: is nearly full", md);   // the other open problem is still listed
        Assert.Contains("gotonode=abc", md);
        Assert.Contains("kevinw · active", md);
        Assert.Contains("Revit 2026 26.4", md);
        Assert.Contains("Monitors are DisplayPort", md);
    }

    [Fact]
    public void Every_prompt_ends_with_the_report_back_for_its_own_run()
    {
        var md = PromptComposer.Device(Input(null), new PromptReport(41, "tok_abcdefghijklmnopqrstuvwxyz", "https://KOR-APP01.int.korstructural.com:8445/"));
        Assert.Contains("https://KOR-APP01.int.korstructural.com:8445/api/prompt-runs/41/outcome", md);
        Assert.Contains("'X-Prompt-Token' = 'tok_abcdefghijklmnopqrstuvwxyz'", md);
        Assert.EndsWith("Prompt Library.", md.TrimEnd());

        var tool = PromptComposer.Tool("The endpoint agent", "## Brief\ntext", [("Agents", "29 of 39")], new PromptReport(7, "tok_zyxwvutsrqponmlkjihgfedcba", "https://x:8445"), Now);
        Assert.Contains("/api/prompt-runs/7/outcome", tool);
        Assert.Contains("- Agents: 29 of 39", tool);
    }

    // 2026-10-01: a session spent 10 of 14 minutes re-reading on the PC what NetworkOps already held, with an event-log
    // query that rendered every entry. The prompt now carries the last check and says how to read a PC quickly.
    [Fact]
    public void A_device_prompt_carries_its_last_check_and_how_to_read_the_machine_quickly()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "health", "KOR-104N-v8-2026-10-01.json"));
        var md = PromptComposer.Device(Input(null) with { Raw = new LastCheck("health", Now.AddMinutes(-25), json) }, null);
        Assert.Contains("**Read first:** its last full health check (25 min ago)", md);
        Assert.Contains("## Its last full check (health, 2026-09-30 22:35 UTC, as returned)", md);
        Assert.Contains("\"GpuHangStaleReports\"", md);   // the evidence the KOR-217 session went to the PC for
        Assert.Contains("--timeout 90", md);
        Assert.Contains("NEVER render `.Message`", md);
        Assert.Contains("ONE script", md);
    }

    [Fact]
    public void A_check_too_large_to_carry_is_pointed_to_not_pasted()
    {
        var md = PromptComposer.Device(Input(null) with { Raw = new LastCheck("health", Now, new string('x', PromptComposer.MaxRawChars + 1)) }, null);
        Assert.Contains("Too large to carry here", md);
        Assert.DoesNotContain("```json", md);
    }

    [Fact]
    public void Without_a_stored_check_the_prompt_says_so()
        => Assert.Contains("There is no stored check for it yet", PromptComposer.Device(Input(null), null));

    [Fact]
    public void Without_migration_007_the_prompt_says_to_leave_a_note_instead()
    {
        var md = PromptComposer.Device(Input(null), null);
        Assert.Contains("Reporting is not switched on yet (migration 007)", md);
        Assert.DoesNotContain("X-Prompt-Token", md);
    }

    [Fact]
    public void Every_tool_has_its_brief_embedded_and_every_brief_is_in_the_catalog()
    {
        foreach (var t in PromptLibrary.Tools)
            Assert.False(string.IsNullOrWhiteSpace(PromptLibrary.Brief(t.Id)), $"{t.Id}: no embedded brief");

        var onDisk = Directory.GetFiles(Path.Combine(Repo.Root, "Kor.Operations.NetworkOps.Service", "Prompts", "Tools"), "*.md")
            .Select(f => Path.GetFileNameWithoutExtension(f)!).Order().ToList();
        Assert.Equal(onDisk, PromptLibrary.Tools.Select(t => t.Id).Order().ToList());
    }

    [Fact]
    public void The_token_is_stored_only_as_a_hash()
    {
        var h = PromptLibrary.HashToken("tok_abcdefghijklmnopqrstuvwxyz");
        Assert.Equal(32, h.Length);
        Assert.Equal(h, PromptLibrary.HashToken("tok_abcdefghijklmnopqrstuvwxyz"));
        Assert.NotEqual(h, PromptLibrary.HashToken("tok_abcdefghijklmnopqrstuvwxyZ"));
    }

    [Fact]
    public void No_prompt_can_carry_a_credential_because_the_library_never_reads_one()
    {
        var src = File.ReadAllText(Path.Combine(Repo.Root, "Kor.Operations.NetworkOps.Service", "Prompts", "PromptLibrary.cs"))
                + File.ReadAllText(Path.Combine(Repo.Root, "Kor.Operations.NetworkOps.Core", "Prompts", "PromptComposer.cs"));
        var credentialOptions = typeof(Kor.Operations.NetworkOps.Service.NetworkOpsOptions).GetProperties()
            .Select(p => p.Name).Where(n => Regex.IsMatch(n, "Password|Secret|Key|Token|Community|Passphrase", RegexOptions.IgnoreCase)).ToList();
        Assert.NotEmpty(credentialOptions);   // the scan is looking at something
        foreach (var name in credentialOptions)
            Assert.DoesNotMatch($@"\.{name}\b", src);
    }

    private static class Repo
    {
        public static string Root
        {
            get
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Kor.Operations.NetworkOps.Agent"))) dir = dir.Parent;
                return dir?.FullName ?? throw new DirectoryNotFoundException("repo root not found above " + AppContext.BaseDirectory);
            }
        }
    }
}

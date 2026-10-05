#nullable enable
using System.Text.RegularExpressions;
using Kor.Operations.NetworkOps.Core.Learning;
using Kor.Operations.NetworkOps.Core.Prompts;
using Kor.Operations.NetworkOps.Service.Prompts;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// Asking Claude anything, and banking what a session learns as knowledge cards that reach every later prompt about a
// machine they apply to; and the rule under all of it: A SESSION NEVER REACHES A MACHINE FROM IAN'S PC -- every read
// goes through NetworkOps on APP01 (Ian 2026-10-01: "No trips from my PC to their PC. This should all be from App01").
//
// WHAT IT COVERS: which machines a card applies to (each term of the vocabulary, case and punctuation, no match, bad
// terms refused); an ask about a PC carries the question, the machine, the network brief, the network now, the cards
// that apply in full and the rest by title; a network-wide ask carries no machine; a device prompt carries its cards;
// the report-back offers a card only once 008 has run; every netops command any prompt hands a session is one of the
// verbs that go through APP01, and none says --direct; the CLI sends `run` through APP01 unless --direct is given;
// the CLI signs in to the same registration, scope and pinned certificate as the app (App.config).
// WHAT IT DOES NOT: the SQL (008, card insert in the outcome's transaction, decide), the HTTP routes, the agent or
// network route APP01 takes, or MSAL's sign-in -- those are proven by running `netops run` against a real PC and by a
// live ask. A SAME-CLASS FAULT IT WOULD NOT CATCH: a session that writes its OWN script to reach a machine directly
// (Invoke-Command, \\pc\c$, the old CLI built from an older commit) -- the prompt's rules forbid it; nothing enforces it.
public sealed class AskAndKnowledgeTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 21, 0, 0, DateTimeKind.Utc);

    private static readonly Dictionary<string, string> AndreasPc = new()
    {
        [Facts.Model] = "ThinkStation P340", [Facts.GpuName] = "NVIDIA Quadro P1000", ["app.etabs.22"] = "22.1.0", ["app.revit.2025"] = "25.4.60.9",
    };

    private static KnowledgeCard Card(long id, string title, string appliesTo) => new(id, title, appliesTo, "ETABS closes while opening a model", "cause", "check", "fix", "etabs", "KOR-214", 9, "accepted", Now.AddDays(-id));

    [Theory]
    [InlineData("app:etabs", true)]
    [InlineData("app:ETABS 22", true)]          // a card says it as a person would; the fact key is app.etabs.22
    [InlineData("app:safe", false)]
    [InlineData("model:p340", true)]
    [InlineData("gpu:quadro p1000", true)]
    [InlineData("kind:workstation", true)]
    [InlineData("kind:server", false)]
    [InlineData("device:kor-214", true)]
    [InlineData("device:KOR-217", false)]
    [InlineData("finding:crash-loop", true)]   // an open crash-loop:etabs.exe is the crash-loop family
    [InlineData("finding:gpu-hangs", false)]
    [InlineData("any", true)]
    [InlineData("app:tekla, model:p340", true)] // comma = or
    public void A_card_applies_to_the_machines_its_terms_name(string appliesTo, bool applies)
        => Assert.Equal(applies, KnowledgeCards.AppliesTo(appliesTo, "KOR-214", "Workstation", AndreasPc, ["crash-loop:etabs.exe", "memory-layout"]));

    [Theory]
    [InlineData("")]
    [InlineData("app:")]
    [InlineData("software:etabs")]
    public void A_card_no_prompt_could_ever_carry_is_refused(string appliesTo)
    {
        Assert.NotNull(KnowledgeCards.Invalid(appliesTo));
        Assert.NotNull(PromptLibrary.InvalidCard(new CardProposal("t", appliesTo, "s", null, null, null, null, "plain")));
    }

    [Fact]
    public void A_good_card_is_accepted_for_review()
        => Assert.Null(PromptLibrary.InvalidCard(new CardProposal("ETABS crashes opening large models", "app:etabs", "closes while opening", null, null, null, null,
            "If ETABS crashes opening big models on these machines, it is the 32-bit graphics cache; approving this offers that fix.")));

    [Fact]
    public void A_card_without_a_plain_explanation_is_refused()   // Ian, 010: the person approving must get a plain line first
        => Assert.Contains("plain", PromptLibrary.InvalidCard(new CardProposal("t", "app:etabs", "s", null, null, null, null))!);

    [Theory]
    [InlineData(0L, true)]
    [InlineData(-1L, true)]
    [InlineData(5L, false)]   // a positive id passes here; the endpoint checks it is a REAL card
    public void An_amends_id_is_a_positive_card_id_or_left_out(long amends, bool refused)
        => Assert.Equal(refused, PromptLibrary.InvalidCard(new CardProposal("t", "app:etabs", "s", null, null, null, null, "plain", amends)) is not null);

    [Fact]
    public void The_report_template_asks_for_a_plain_explanation_and_offers_to_amend()
    {
        var md = PromptComposer.Device(Pc(), new PromptReport(7, "tok_abcdefghijklmnopqrstuvwxyz", "https://x", Cards: true));
        Assert.Contains("plain     =", md);   // the plain-English line is asked for
        Assert.Contains("amends", md);        // and amending a card by id, instead of prose for Ian
    }

    private static DevicePromptInput Pc(IReadOnlyList<KnowledgeCard>? cards = null) => new(
        "KOR-214", "Workstation", AndreasPc, [], null, [], [], [], [], [], [], [], null, [],
        new PromptAccess(false, "aleung · active", true, "1.0.2", true, null, null), Now.AddMinutes(-10), Now,
        new LastCheck("health", Now.AddMinutes(-10), "[{\"ProbeVersion\":8}]"), cards);

    [Fact]
    public void An_ask_about_a_pc_carries_the_question_the_machine_the_network_and_what_applies()
    {
        var applies = Card(1, "ETABS closes opening big models: the 32-bit graphics cache", "app:etabs");
        var other = Card(2, "Revit 2025 hangs on worksharing", "app:revit");
        var md = PromptComposer.Ask(new AskPromptInput(
            "Andrea's ETABS crashed around 2:40 today opening the Tower B model", "ilalonde@korstructural.com", Pc([applies]),
            PromptLibrary.NetworkBrief(), ["PCs: 39, 29 checked in the last 3 days"], [applies], [other], Now),
            new PromptReport(12, "tok_abcdefghijklmnopqrstuvwxyz", "https://KOR-APP01.int.korstructural.com:8445", Cards: true));

        Assert.Contains("# A question about KOR-214", md);
        Assert.Contains("> Andrea's ETABS crashed around 2:40 today opening the Tower B model", md);
        Assert.Contains("KOR-APP01", md);                                 // the network brief
        Assert.Contains("- PCs: 39, 29 checked in the last 3 days", md);   // the network now
        Assert.Contains("etabs.22 22.1.0", md);                            // the machine's apps
        Assert.Contains("### ETABS closes opening big models: the 32-bit graphics cache", md);   // applies: in full
        Assert.Contains("- card 2: Revit 2025 hangs on worksharing", md);                      // the rest: by title
        Assert.Contains("\"ProbeVersion\":8", md);                        // its last check
        Assert.Contains("appliesTo = 'app:etabs'", md);                   // the report can bank a card
        Assert.Contains("/api/prompt-runs/12/outcome", md);
    }

    [Fact]
    public void A_network_wide_ask_carries_no_machine()
    {
        var md = PromptComposer.Ask(new AskPromptInput("Why is the VPN slow in the mornings?", "ilalonde", null, PromptLibrary.NetworkBrief(), [], [], [], Now), null);
        Assert.Contains("# A question about KOR's network", md);
        Assert.DoesNotContain("## The machine", md);
        Assert.Contains("--hosts <machine>", md);
    }

    [Fact]
    public void A_device_prompt_carries_the_cards_that_apply_to_it()
    {
        var md = PromptComposer.Device(Pc([Card(1, "ETABS closes opening big models", "app:etabs")]), null);
        Assert.Contains("### ETABS closes opening big models", md);
        Assert.Contains("- How to check: check", md);
    }

    [Fact]
    public void Before_008_the_report_offers_no_card()
        => Assert.DoesNotContain("appliesTo =", PromptComposer.Device(Pc(), new PromptReport(1, "tok_abcdefghijklmnopqrstuvwxyz", "https://x", Cards: false)));

    // ---- the rule under all of it: nothing a prompt hands a session reaches a machine from Ian's PC.

    // Every verb SessionVerbs.Handles sends through APP01 (the next test pins that list in the CLI's own source).
    private static readonly string[] ThroughApp01 = ["run", "check", "last-check", "knowledge", "findings", "fix", "history", "readings", "action", "trigger", "updates", "changes"];

    [Fact]
    public void Every_netops_command_a_prompt_hands_out_goes_through_APP01()
    {
        var prompts = new[]
        {
            PromptComposer.Device(Pc(), null),
            PromptComposer.Ask(new AskPromptInput("q", "ilalonde", Pc(), PromptLibrary.NetworkBrief(), [], [], [], Now), null),
            PromptComposer.Ask(new AskPromptInput("q", "ilalonde", null, PromptLibrary.NetworkBrief(), [], [], [], Now), null),
        };
        foreach (var md in prompts)
        {
            var verbs = Regex.Matches(md, @"`netops ([a-z-]+)").Select(m => m.Groups[1].Value).ToList();
            Assert.NotEmpty(verbs);   // the scan is looking at something
            Assert.All(verbs, v => Assert.Contains(v, ThroughApp01));
            Assert.DoesNotContain("--direct", md);
            Assert.DoesNotContain("netops health", md);   // the probe straight from this PC
            Assert.Contains("always through APP01", md);
        }
    }

    [Fact]
    public void The_CLI_sends_run_through_APP01_unless_told_direct()
    {
        var verbs = File.ReadAllText(Path.Combine(Repo.Root, "Kor.Operations.NetworkOps.Cli", "SessionVerbs.cs"));
        Assert.Matches(new Regex(@"verb is ""check"" or ""last-check"" or ""knowledge"" or ""findings"" or ""fix"" or ""history"" or ""readings"" or ""action"" or ""trigger"" or ""updates"" or ""changes"" or ""network"" or ""add-pc"" or ""token"" \|\| \(verb == ""run"" && !args\.Contains\(""--direct""\)\)"), verbs);
        var program = File.ReadAllText(Path.Combine(Repo.Root, "Kor.Operations.NetworkOps.Cli", "Program.cs"));
        Assert.True(program.IndexOf("SessionVerbs.Handles", StringComparison.Ordinal) < program.IndexOf("RunEverywhere(hosts", StringComparison.Ordinal),
            "SessionVerbs must be asked before any verb reaches a machine from this PC");
    }

    [Fact]
    public void The_CLI_exposes_the_mcp_bridge_verb()
    {
        // `netops mcp` is the stdio MCP bridge Claude Code launches (.mcp.json); it must stay wired to McpBridge.
        var program = File.ReadAllText(Path.Combine(Repo.Root, "Kor.Operations.NetworkOps.Cli", "Program.cs"));
        Assert.Matches(new Regex(@"verb == ""mcp""[\s\S]{0,120}McpBridge\.RunAsync"), program);
    }

    [Fact]
    public void The_CLI_signs_in_exactly_as_the_app_does()
    {
        var config = File.ReadAllText(Path.Combine(Repo.Root, "Kor.Operations.App", "App.config"));
        var cli = File.ReadAllText(Path.Combine(Repo.Root, "Kor.Operations.NetworkOps.Cli", "AppServer.cs"));
        foreach (var (key, constant) in new[] { ("NetworkOps.ApiBaseUrl", "BaseUrl"), ("NetworkOps.ApiScope", "Scope"), ("NetworkOps.ApiCertSha256", "CertSha256"), ("Graph.TenantId", "TenantId"), ("Graph.ClientId", "ClientId") })
        {
            var inApp = Regex.Match(config, $@"key=""{Regex.Escape(key)}""\s+value=""([^""]+)""").Groups[1].Value;
            var inCli = Regex.Match(cli, $@"const string {constant} = ""([^""]+)""").Groups[1].Value;
            Assert.False(string.IsNullOrEmpty(inApp), $"{key} not found in App.config");
            Assert.Equal(inApp, inCli);
        }
    }

    [Fact]
    public void The_network_brief_is_embedded()
        => Assert.Contains("KOR-DC01", PromptLibrary.NetworkBrief());

    [Fact]
    public void Knowledge_search_needs_every_word()
    {
        var c = Card(1, "ETABS closes opening big models", "app:etabs");
        Assert.True(Kor.Operations.NetworkOps.Service.Api.SessionApi.Matches(c, "etabs crash".Replace("crash", "closes")));
        Assert.False(Kor.Operations.NetworkOps.Service.Api.SessionApi.Matches(c, "etabs revit"));
        Assert.True(Kor.Operations.NetworkOps.Service.Api.SessionApi.Matches(c, null));
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

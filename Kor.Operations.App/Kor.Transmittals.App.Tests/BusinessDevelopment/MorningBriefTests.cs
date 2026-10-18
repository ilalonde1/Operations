using System.IO;
using System.Text.Json;
using Kor.MorningBrief;
using Kor.Operations.App.BusinessDevelopment.Director;
using Kor.Operations.Financials;
using Kor.Operations.PMTools;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Worker.Services.Reporting;
using Moq;
using Xunit;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

/// <summary>Offline gates for 10b/10c/10d. Authored only; no database, session or mail is invoked.</summary>
public sealed class MorningBriefTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 10, 16);

    internal static DirectorHourContent FixtureHour() => new("2026-10-16", "Morning", "Your morning", "Three decisions for today",
        Array.Empty<HourPlanLine>(), null, Array.Empty<HourMove>(), Array.Empty<string>(), Array.Empty<string>(), "Illustrative fixture", null)
    {
        Sections = new[]
        {
            Section("BD", 1, "Call the buyer", 5), Section("COO", 2, "Review overdue invoices", 101), Section("IT", 3, "Review the critical finding"),
        },
    };

    private static HourSection Section(string role, int rank, string move, params long[] cards) => new(role, role + " priorities",
        new[] { new HourPlanLine("slow", role + " position", role + " fixture fact") },
        new[] { new HourMove(rank, move, 20, new[] { role + " why now" }, null, Array.Empty<HourDraft>(), Array.Empty<string>(), Array.Empty<HourLink>()) { Cards = cards } },
        new[] { role + " decision" });

    private static string Json(DirectorHourContent hour) => JsonSerializer.Serialize(hour, DirectorHourFile.JsonOptions);
    private static DoctrineCardRow Card(long id, string role, string status = "accepted") => new(id, role, role + " card " + id,
        "Plain", "any", "Principle", "Recognise", "Play", null, "Book, chapter 1", null, null, null, status, Now, null, null);

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Morning_parses_two_or_three_sections_and_totals_all_moves(int sections)
    {
        var hour = FixtureHour();
        var (content, errors) = DirectorHourFile.Parse(Json(hour with { Sections = hour.Sections!.Take(sections).ToArray() }));
        Assert.Empty(errors);
        Assert.NotNull(content);
        Assert.Empty(content!.Moves);
        Assert.Equal(sections * 20, DirectorHourFile.TotalMinutes(content));
        Assert.Equal(Enumerable.Range(1, sections), DirectorHourFile.AllMoves(content).Select(m => m.Rank));
    }

    [Theory]
    [InlineData("top moves", "top-level moves")]
    [InlineData("zero moves", "section 'COO': moves must hold 1 to 4")]
    [InlineData("five moves", "section 'COO': moves must hold 1 to 4")]
    [InlineData("duplicate rank", "unique across the whole hour")]
    [InlineData("gap", "without gaps")]
    [InlineData("unknown role", "section 'Finance': unknown role")]
    [InlineData("duplicate role", "section 'BD': duplicate role")]
    [InlineData("one section", "2 or 3 sections")]
    [InlineData("null section", "section cannot be null")]
    public void Invalid_morning_is_refused_with_a_named_problem(string fault, string message)
    {
        var hour = FixtureHour();
        var sections = hour.Sections!.ToArray();
        switch (fault)
        {
            case "top moves": hour = hour with { Moves = sections[0].Moves }; break;
            case "zero moves": sections[1] = sections[1] with { Moves = Array.Empty<HourMove>() }; break;
            case "five moves": sections[1] = sections[1] with { Moves = Enumerable.Range(2, 5).Select(r => sections[1].Moves[0] with { Rank = r }).ToArray() }; break;
            case "duplicate rank": sections[1] = sections[1] with { Moves = new[] { sections[1].Moves[0] with { Rank = 1 } } }; break;
            case "gap": sections[1] = sections[1] with { Moves = new[] { sections[1].Moves[0] with { Rank = 8 } } }; break;
            case "unknown role": sections[1] = sections[1] with { Role = "Finance" }; break;
            case "duplicate role": sections[1] = sections[1] with { Role = "BD" }; break;
            case "one section": sections = sections.Take(1).ToArray(); break;
            case "null section": sections[1] = null!; break;
        }
        var (content, errors) = DirectorHourFile.Parse(Json(hour with { Sections = sections }));
        Assert.Null(content);
        Assert.Contains(errors, e => e.Contains(message, StringComparison.Ordinal));
    }

    [Fact]
    public void Twelve_moves_are_valid_and_the_schema_and_write_guard_support_their_ranks()
    {
        var hour = FixtureHour();
        hour = hour with { Sections = hour.Sections!.Select((s, i) => s with
        {
            Moves = Enumerable.Range(i * 4 + 1, 4).Select(r => s.Moves[0] with { Rank = r, Minutes = 5 }).ToArray(),
        }).ToArray() };
        Assert.Empty(DirectorHourFile.Parse(Json(hour)).Errors);
        Assert.Equal(60, DirectorHourFile.TotalMinutes(hour));
        var migration = Source("Kor.Opportunities.Data/Schema/366_MorningHour.sql");
        Assert.Contains("N'Morning'", migration, StringComparison.Ordinal);
        Assert.Contains("MoveRank BETWEEN 1 AND 12", migration, StringComparison.Ordinal);
        Assert.Contains("BEGIN TRANSACTION", migration, StringComparison.Ordinal);
        Assert.Contains("rank is < 1 or > 12", Source("Kor.Opportunities.Data/Crm/BdRhythmStore.cs"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("BD")]
    [InlineData("Operations")]
    public void Legacy_hours_without_sections_still_parse(string kind)
    {
        var hour = FixtureHour() with { Kind = kind, Sections = null, Moves = FixtureHour().Sections![0].Moves };
        var (content, errors) = DirectorHourFile.Parse(Json(hour));
        Assert.Empty(errors);
        Assert.Null(content!.Sections);
        Assert.Single(content.Moves);
        Assert.Equal(20, DirectorHourFile.TotalMinutes(content));
    }

    [Fact]
    public void Card_gate_enforces_each_sections_role_status_and_no_IT_doctrine()
    {
        var cards = new[] { Card(5, "BD"), Card(101, "Operations"), Card(9, "IT"), Card(6, "BD", "proposed"), Card(7, "BD", "retired"), Card(8, "Operations", "rejected") };
        var hour = FixtureHour();
        Assert.Empty(DirectorHourFile.ValidateCards(hour, cards));
        var sections = hour.Sections!.Select(s => s with { Moves = s.Moves.Select(m => m with { Cards = new long[] { 5, 101, 9, 6, 7, 8, 999 } }).ToArray() }).ToArray();
        var errors = DirectorHourFile.ValidateCards(hour with { Sections = sections }, cards);
        Assert.Equal(19, errors.Count); // two valid citations, and seven refused in IT
        Assert.Contains(errors, e => e.Contains("section BD, move 1: card 101", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("section COO, move 2: card 5", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("section IT, move 3: card 9", StringComparison.Ordinal));
    }

    [Fact]
    public void Morning_email_has_all_role_headings_plans_moves_and_decisions()
    {
        var hour = FixtureHour();
        var html = DirectorHourEmail.Render(hour);
        Assert.Equal("Your morning · about 60 minutes", DirectorHourEmail.Subject(hour));
        foreach (var section in hour.Sections!)
        {
            Assert.Contains(">" + section.Role + "</h2>", html, StringComparison.Ordinal);
            Assert.Contains(section.Plan[0].Text, html, StringComparison.Ordinal);
            Assert.Contains(section.Moves[0].Title, html, StringComparison.Ordinal);
            Assert.Contains(section.Decisions[0], html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Email_job_prefers_morning_and_never_falls_back_after_it_was_sent()
    {
        var store = new Mock<IBdRhythmStore>(MockBehavior.Strict);
        var row = new DirectorHourRow(12, Day, "Morning", Json(FixtureHour()), "session", "Ian", Now, null);
        store.Setup(s => s.GetHourAsync("Morning", Day, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        Assert.Same(row, await DirectorHourEmailJob.SelectHourAsync(store.Object, Day, CancellationToken.None));
        var sent = Assert.Single(DirectorHourEmailJob.Compose(row, Array.Empty<BdActionRow>(), Day, Now));
        Assert.StartsWith("Your morning", sent.Subject, StringComparison.Ordinal);
        Assert.Equal(12L, sent.StampHourId);
        store.Setup(s => s.GetHourAsync("Morning", Day, It.IsAny<CancellationToken>())).ReturnsAsync(row with { EmailedAtUtc = Now });
        var emailed = await DirectorHourEmailJob.SelectHourAsync(store.Object, Day, CancellationToken.None);
        Assert.Empty(DirectorHourEmailJob.Compose(emailed, Array.Empty<BdActionRow>(), Day, Now));
        store.Verify(s => s.GetHourAsync("BD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
        store.Setup(s => s.GetHourAsync("Morning", Day, It.IsAny<CancellationToken>())).ReturnsAsync((DirectorHourRow?)null);
        var bd = row with { Kind = "BD" };
        store.Setup(s => s.GetHourAsync("BD", Day, It.IsAny<CancellationToken>())).ReturnsAsync(bd);
        Assert.Same(bd, await DirectorHourEmailJob.SelectHourAsync(store.Object, Day, CancellationToken.None));
    }

    [Fact]
    public void Composer_includes_role_cards_rules_import_coverage_and_source_failures()
    {
        var facts = new MorningFacts(Now,
            new[] { new MorningFact("Accepted BD cards", new[] { "#5 BD principle" }, 1, 1), new MorningFact("Quiet pursuits", new[] { "Pursuit #12" }, 1, 27) },
            new[] { new MorningFact("Accepted Operations cards", new[] { "#101 Operating principle" }, 1, 1), new MorningFact("Deltek AR", Array.Empty<string>(), 0, null, "DSN unavailable") },
            Array.Empty<MorningFact>());
        var prompt = MorningPromptComposer.Compose(facts, Day);
        foreach (var expected in new[] { "## BD", "## COO", "## IT", "#5 BD principle", "#101 Operating principle", "1 of 27", "0 of unknown",
            "Nothing goes to anyone else", "Never call an API", "BdKnowledgeIngest claims search", "BdKnowledgeIngest hour import <file> --by \"<session>\"",
            "Could not read: Deltek AR", "Could not read: IT facts", "2026-10-16", "draft.to is Ian" })
            Assert.Contains(expected, prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Pursuit_top_ten_preserves_the_denominator_and_includes_exactly_fourteen_days()
    {
        var rows = Enumerable.Range(1, 12).Select(i => new MorningFacts.Pursuit(i, "Owner", "Region", "Pursuit " + i, i * 100, Now.AddDays(-14)))
            .Append(new MorningFacts.Pursuit(20, "Owner", "Region", "Recent", 99999, Now.AddDays(-13))).ToArray();
        var stale = MorningFacts.PursuitFacts(rows, Now)[1];
        Assert.Equal(10, stale.Shown);
        Assert.Equal(12, stale.Total);
        Assert.StartsWith("Pursuit #12:", stale.Lines[0], StringComparison.Ordinal);
        Assert.DoesNotContain(stale.Lines, l => l.Contains("Recent", StringComparison.Ordinal));
    }

    [Fact]
    public void Employee_utilization_uses_the_requested_completed_week_and_names_missing_data()
    {
        var week = new DateTime(2026, 10, 4);
        EmployeeWeeklyHours Hours(string name, double billable, DateTime start) => new() { EmployeeId = name, EmployeeName = name, BillableHrs = billable, TotalHrs = 40, WeekStart = start };
        var rows = new[] { Hours("Under", 19, week), Hours("Half", 20, week), Hours("Over", 45, week), Hours("Edge", 44, week), Hours("Wrong week", 1, week.AddDays(7)) };
        var fact = Assert.Single(MorningFacts.EmployeeFacts(rows, week));
        Assert.Equal(2, fact.Shown);
        Assert.Contains(fact.Lines, l => l.StartsWith("Under", StringComparison.Ordinal));
        Assert.Contains(fact.Lines, l => l.StartsWith("Over", StringComparison.Ordinal));
        Assert.DoesNotContain(fact.Lines, l => l.StartsWith("Wrong week", StringComparison.Ordinal));
        Assert.NotNull(Assert.Single(MorningFacts.EmployeeFacts(rows, week.AddDays(-7))).CouldNotRead);
    }

    /// <summary>
    /// Guards the engine-only call path and project-reference direction by source text.
    /// Does not validate a build, live snapshot scope, constructor/configuration needs, or engine numbers.
    /// </summary>
    [Fact]
    public void Coo_source_uses_the_executive_engine_and_references_the_App()
    {
        var source = Source("tools/MorningBrief/MorningFacts.cs");
        foreach (var forbidden in new[] { "new WipFinancialsService", "new ArFinancialsService", "new FirmAnalyticsService", "new ProjectAnalyticsService", "ChargeType" })
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        foreach (var required in new[] { "new FinancialsService", "new ExecutiveSummaryService", "watchlistOnly: false",
            "existingSnapshot: snap", "existingTrend: Array.Empty<PortfolioTrendPoint>()",
            "existingUtilRows: snap.Rows.Select(UtilizationRow.FromProject).ToArray()", "ExecutiveFacts(result, snap.Rows.Count, trendNote)" })
            Assert.Contains(required, source, StringComparison.Ordinal);
        var project = Source("tools/MorningBrief/MorningBrief.csproj");
        Assert.Contains("<TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>", project, StringComparison.Ordinal);
        Assert.Contains("<UseWPF>true</UseWPF>", project, StringComparison.Ordinal);
        Assert.Contains("<Using Include=\"System.IO\"", project, StringComparison.Ordinal);
        Assert.Contains("Kor.Operations.App\\Kor.Operations.App.csproj", project, StringComparison.Ordinal);
        Assert.DoesNotContain("MorningBrief", Source("Kor.Operations.App/Kor.Operations.App.csproj"), StringComparison.Ordinal);
    }

    /// <summary>
    /// Covers all current KPI/alert row shapes, supplied order, top-ten counts and failure notes.
    /// Synthetic results do not validate live scope, constructor/configuration needs, or engine numbers.
    /// </summary>
    [Fact]
    public void Executive_facts_preserve_every_KPI_alert_order_count_and_unavailable_reason()
    {
        var invoices = new[] { 3, 1, 12, 2, 11, 4, 10, 5, 9, 6, 8, 7 }.Select(i =>
            new KpiArInvoiceRow("P" + i, "Project", "PM", "I" + i, "C", "Client", new DateTime(2026, 8, 1), null, i, i * 1000)).ToArray();
        var projects = new[] { new KpiProjectDrilldownRow("P", "Project", "PM", 20, 1.2, .5, .6, true) };
        var ar = new[] { new KpiArOutstandingRow("P", "Project", "PM", 1000, 100, 200, 300, 400, new DateTime(2026, 8, 1)) };
        var history = new[] { new KpiCashHistoryRow("202608", 1000, 600, 300, 100) };
        var accounts = new[] { new KpiCashAccountRow("CAD", "1110", "CAD", "CAD", 1000.25) };
        var backlog = new[] { new KpiBacklogRow("P", "Project", "PM", 1000, 200, 50, 750, .2) };
        var burn = new[] { new KpiBudgetBurnRow("P", "Project", "PM", 120, 100, 1.2, -20) };
        var template = new ExecutiveKpi("Projects", "engine value", "long drill-in prose", "engine status", InlineSubText: "engine inline");
        var kpis = new List<ExecutiveKpi>
        {
            template with { ProjectDrilldownRows = projects },
            template with { Title = "Invoices", ArInvoiceRows = invoices, ArOutstandingRows = ar },
            template with { Title = "AR projects", ArOutstandingRows = ar },
            template with { Title = "Cash accounts", CashAccountRows = accounts, CashHistoryRows = history },
            template with { Title = "Cash history", CashHistoryRows = history },
            template with { Title = "WIP", WipUnbilledRows = new[] { new KpiWipUnbilledRow("P", "Project", "PM", 1000, 200, 800, .8, "202608") } },
            template with { Title = "Backlog", BacklogRows = backlog },
            template with { Title = "Billings", BillingsRows = new[] { new KpiBillingsRow("P", "Project", "PM", 200, 50, 1000, .2, .5) } },
            template with { Title = "Budget burn", BudgetBurnRows = burn },
            template with { Title = "Delivery risk", DeliveryRiskRows = new[] { new KpiDeliveryRiskRow("P", "Project", "PM", "Critical", "Over budget", 1.2, -20) } },
            template with { Title = "Utilization", ValueText = "72.7%", UtilizationRows = new[] { new KpiUtilizationRow("P", "Project", "PM", 30, 10, 40, .75) } },
            ExecutiveKpi.DataUnavailable("Missing KPI", "Fixture reason"),
        };
        var alerts = new List<ExecutiveAlert>
        {
            new("Project alert", "Project message", ProjectDrilldownRows: projects),
            new("Invoice alert", "Invoice message", ArInvoiceRows: invoices, ArOutstandingRows: ar),
            new("AR alert", "AR message", ArOutstandingRows: ar),
            new("Backlog alert", "Backlog message", BacklogRows: backlog),
            new("Burn alert", "Burn message", BudgetBurnRows: burn),
            new("Message-only alert", "No row list"),
        };
        var result = new ExecutiveSummaryResult(Now, kpis, new List<ExecutiveTrend>(), alerts,
            SnapshotLoaded: false, DeltekLoaded: false,
            LoaderFailureMessages: new[] { "Fixture loader failure" }, SchemaDriftMessages: new[] { "Fixture schema drift" });

        var facts = MorningFacts.ExecutiveFacts(result, 123);

        const string scopeSuffix = " · scope not stated by the engine";
        Assert.Equal(kpis.Select(kpi => kpi.Title + scopeSuffix), facts.Skip(1).Take(kpis.Count).Select(fact => fact.Name));
        Assert.Equal(alerts.Select(alert => alert.Title + scopeSuffix), facts.Skip(1 + kpis.Count).Select(fact => fact.Name));
        for (var i = 0; i < kpis.Count; i++)
        {
            var fact = facts[i + 1];
            Assert.Contains(kpis[i].ValueText, fact.Lines);
            Assert.Contains(kpis[i].StatusMessage, fact.Lines);
            if (kpis[i].InlineSubText.Length > 0) Assert.Contains(kpis[i].InlineSubText, fact.Lines);
        }
        for (var i = 0; i < alerts.Count; i++)
            Assert.Contains(alerts[i].Message, facts[1 + kpis.Count + i].Lines);
        foreach (var title in new[] { "Invoices", "Invoice alert" })
        {
            var fact = Assert.Single(facts, f => f.Name == title + scopeSuffix);
            Assert.Equal(10, fact.Shown);
            Assert.Equal(12, fact.Total);
            var shown = fact.Lines.TakeLast(10).ToArray();
            for (var i = 0; i < shown.Length; i++)
                Assert.Contains($"invoice {invoices[i].Invoice},", shown[i], StringComparison.Ordinal);
        }
        Assert.Contains("CAD, account 1110, org CAD: CAD 1,000.", Assert.Single(facts, f => f.Name == "Cash accounts" + scopeSuffix).Lines);
        Assert.Equal("Fixture reason", Assert.Single(facts, f => f.Name == "Missing KPI" + scopeSuffix).CouldNotRead);
        Assert.Contains("SnapshotLoaded: False; DeltekLoaded: False; TrendLoaded: True.", facts[0].Lines);
        Assert.Contains("Loader: Fixture loader failure", facts[0].Lines);
        Assert.Contains("Schema drift: Fixture schema drift", facts[0].Lines);
        var prompt = MorningPromptComposer.Compose(new MorningFacts(Now, Array.Empty<MorningFact>(), facts, Array.Empty<MorningFact>()), Day);
        Assert.Contains("## COO — Executive Summary engine; project-scoped figures cover all active projects (123)", prompt, StringComparison.Ordinal);
        Assert.Contains("### Invoices" + scopeSuffix + " · 10 of 12", prompt, StringComparison.Ordinal);
        Assert.Contains("### Invoice alert" + scopeSuffix + " · 10 of 12", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("partial", string.Join(" ", Assert.Single(facts, f => f.Name == "Utilization" + scopeSuffix).Lines), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks invoice presentation without regrouping or inventing a due date.
    /// Does not validate live invoice grain, snapshot scope, constructors/configuration, or engine ages/balances.
    /// </summary>
    [Fact]
    public void Executive_invoice_rows_keep_the_engine_grain_and_label_invoice_age()
    {
        var invoice = new KpiArInvoiceRow("P1", "First project", "PM", "00038658", "C", "Client", new DateTime(2026, 8, 1), null, 77, 1000.25);
        var rows = new[] { invoice, invoice with { Wbs1 = "P2", ProjectName = "Second project", Balance = 2000 } };
        var result = new ExecutiveSummaryResult(Now,
            new List<ExecutiveKpi> { new("AR", "3,000", "", "", ArInvoiceRows: rows) },
            new List<ExecutiveTrend>(), new List<ExecutiveAlert>());

        var fact = Assert.Single(MorningFacts.ExecutiveFacts(result, 2), f => f.Name == "AR · scope not stated by the engine");

        Assert.Equal(2, fact.Shown);
        Assert.Equal(2, fact.Total);
        Assert.StartsWith("P1 First project", fact.Lines[1], StringComparison.Ordinal);
        Assert.StartsWith("P2 Second project", fact.Lines[2], StringComparison.Ordinal);
        foreach (var line in fact.Lines.Skip(1))
        {
            Assert.Contains("invoice date 2026-08-01", line, StringComparison.Ordinal);
            Assert.Contains("77 days since invoice", line, StringComparison.Ordinal);
            Assert.DoesNotContain("due", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch(@"\d{4}-\d{2}-\d{2}T", line);
        }
    }

    /// <summary>
    /// Proves scope labels come from the field even when familiar titles suggest the opposite scope.
    /// Does not read a database or validate engine scopes, store connections, trend-table contents, or screen sparklines.
    /// </summary>
    [Fact]
    public void Executive_scope_labels_follow_fields_and_the_header_describes_mixed_scopes()
    {
        var result = new ExecutiveSummaryResult(Now, new List<ExecutiveKpi>
        {
            new("AR Outstanding", "1,000", "", "", Scope: ScopeKind.Scoped),
            new("Backlog", "2,000", "", "", Scope: ScopeKind.Firmwide),
            new("Utilization", "75%", "", "", Scope: ScopeKind.None),
        }, new List<ExecutiveTrend>(), new List<ExecutiveAlert> { new("AR alert", "Engine message") });

        var facts = MorningFacts.ExecutiveFacts(result, 812);

        Assert.Equal(new[]
        {
            "Executive Summary coverage",
            "AR Outstanding · all active projects (812)",
            "Backlog · firmwide",
            "Utilization · scope not stated by the engine",
            "AR alert · scope not stated by the engine",
        }, facts.Select(fact => fact.Name));
        const string header = "Executive Summary engine; project-scoped figures cover all active projects (812)";
        Assert.Equal(header, facts[0].SectionScope);
        var prompt = MorningPromptComposer.Compose(new MorningFacts(Now, Array.Empty<MorningFact>(), facts, Array.Empty<MorningFact>()), Day);
        Assert.Contains("## COO — " + header + Environment.NewLine, prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// Covers supplied series/row order, top-ten counts, group order, scopes and trend coverage reasons.
    /// Does not read a database, validate the store connection/table, or compare series values with the screen's sparkline.
    /// </summary>
    [Fact]
    public void Executive_trends_print_series_drilldowns_and_unavailable_coverage_in_engine_order()
    {
        var payers = new[] { 4, 1, 11, 2, 12, 3, 10, 5, 9, 6, 8, 7 }.Select(i =>
            new TrendPayerRow("P" + i, "Project", "PM", "Payer " + i, i * 1000.0, i * 2000.0, i * 3000.0, i * 4000.0)).ToArray();
        var risk = new[]
        {
            new KpiDeliveryRiskRow("R2", "Second project", "PM", "Critical", "Over budget", 1.2, -20),
            new KpiDeliveryRiskRow("R1", "First project", "PM", "At risk", "Watch", .8, 20),
        };
        var trends = new List<ExecutiveTrend>
        {
            new("Revenue", "3,000 / 9,000", "Engine revenue status", new[] { 3000.4, -1500.4, 12.4 },
                TrendPayerRows: payers, DeliveryRiskRows: risk, Scope: ScopeKind.Firmwide),
            new("Delivery Risk", "2 projects", "", new[] { 3.0, 1.0, 2.0 }, DeliveryRiskRows: risk, Scope: ScopeKind.Scoped),
            ExecutiveTrend.DataUnavailable("Missing trend", "Fixture trend unavailable"),
        };
        var result = new ExecutiveSummaryResult(Now,
            new List<ExecutiveKpi> { new("Cash", "1,000", "", "", Scope: ScopeKind.Firmwide) },
            trends, new List<ExecutiveAlert> { new("Alert", "Engine alert") },
            MaxPostedPeriod: new DateTime(2026, 8, 31), TrendLoaded: false);
        const string note = "Portfolio trend not read: KOR_FILESYNC_KORTRANSMITTALSDB unset.";

        var facts = MorningFacts.ExecutiveFacts(result, 23, note);

        Assert.Equal(new[]
        {
            "Executive Summary coverage", "Cash · firmwide", "Revenue · firmwide",
            "Delivery Risk · all active projects (23)", "Missing trend · scope not stated by the engine",
            "Alert · scope not stated by the engine",
        }, facts.Select(fact => fact.Name));
        Assert.Contains("SnapshotLoaded: True; DeltekLoaded: True; TrendLoaded: False.", facts[0].Lines);
        Assert.Contains("MaxPostedPeriod: 2026-08.", facts[0].Lines);
        Assert.Contains(note, facts[0].Lines);
        Assert.Equal(note, facts[0].CouldNotRead);
        Assert.Equal(2, facts[0].Shown);
        Assert.Equal(3, facts[0].Total);
        var revenue = facts[2];
        Assert.Equal(10, revenue.Shown);
        Assert.Equal(12, revenue.Total);
        Assert.Equal("3,000 / 9,000", revenue.Lines[0]);
        Assert.Equal("Engine revenue status", revenue.Lines[1]);
        Assert.Equal("Series, 3 periods, oldest first (period labels are not carried by the engine): 3,000; -1,500; 12", revenue.Lines[2]);
        Assert.Equal("P4 Project, PM PM, payer Payer 4: amount 4,000; earned 8,000; invoiced 12,000; AR 16,000.", revenue.Lines[3]);
        for (var i = 0; i < 10; i++)
            Assert.StartsWith(payers[i].Wbs1 + " ", revenue.Lines[3 + i], StringComparison.Ordinal);
        var delivery = facts[3];
        Assert.Equal(2, delivery.Shown);
        Assert.Equal(2, delivery.Total);
        Assert.Equal("Series, 3 periods, oldest first (period labels are not carried by the engine): 3; 1; 2", delivery.Lines[1]);
        Assert.StartsWith("R2 Second project", delivery.Lines[2], StringComparison.Ordinal);
        Assert.StartsWith("R1 First project", delivery.Lines[3], StringComparison.Ordinal);
        Assert.DoesNotContain(delivery.Lines, string.IsNullOrWhiteSpace);
        Assert.Equal("Fixture trend unavailable", facts[4].CouldNotRead);
        Assert.Equal(new[] { "Data unavailable", "Fixture trend unavailable" }, facts[4].Lines);
        var prompt = MorningPromptComposer.Compose(new MorningFacts(Now, Array.Empty<MorningFact>(), facts, Array.Empty<MorningFact>()), Day);
        Assert.Contains("### Revenue · firmwide · 10 of 12", prompt, StringComparison.Ordinal);
        var fallback = MorningFacts.ExecutiveFacts(result with { MaxPostedPeriod = null }, 23)[0];
        Assert.Equal("Portfolio trend unavailable.", fallback.CouldNotRead);
        Assert.Contains("MaxPostedPeriod: unknown.", fallback.Lines);
    }

    /// <summary>
    /// Checks configured-store and missing-variable wiring by source text without reading environment values.
    /// Does not validate the actual connection, an empty trend table, database execution, or screen sparkline parity.
    /// </summary>
    [Fact]
    public void Coo_trend_store_uses_optional_connection_with_an_empty_fallback()
    {
        var source = Source("tools/MorningBrief/MorningFacts.cs");
        var start = source.IndexOf("private static async Task GatherCooAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = source.IndexOf("public static IReadOnlyList<MorningFact> ExecutiveFacts", start, StringComparison.Ordinal);
        Assert.True(end > start);
        var gather = source[start..end];

        Assert.Contains("OptionalConnection(\"KOR_FILESYNC_KORTRANSMITTALSDB\")", gather, StringComparison.Ordinal);
        Assert.Contains("new SqlFinancialPortfolioSnapshotStore(trendConnection ?? \"unused: trend supplied by caller\")", gather, StringComparison.Ordinal);
        Assert.Contains("trendConnection is not null", gather, StringComparison.Ordinal);
        Assert.Contains("existingTrend: null", gather, StringComparison.Ordinal);
        Assert.Contains("existingTrend: Array.Empty<PortfolioTrendPoint>()", gather, StringComparison.Ordinal);
        Assert.Contains("Portfolio trend not read: KOR_FILESYNC_KORTRANSMITTALSDB unset.", gather, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\bConnection\s*\(\s*""KOR_FILESYNC_KORTRANSMITTALSDB""", gather);
        Assert.Contains("OptionalConnection(string name) => Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine)" +
            "\n        ?? Environment.GetEnvironmentVariable(name);", source.Replace("\r\n", "\n"), StringComparison.Ordinal);
    }

    /// <summary>
    /// Uses the seven measured week counts to gate median completeness and last-complete-week outliers.
    /// Does not read timesheets or validate live scope, constructors/configuration, or the engine's utilization ratio.
    /// </summary>
    [Fact]
    public void Employee_partial_weeks_are_named_and_outliers_use_September_27()
    {
        var fixture = new[]
        {
            (Week: new DateTime(2026, 8, 30), Hours: 927.5, People: 24),
            (Week: new DateTime(2026, 9, 6), Hours: 952.5, People: 24),
            (Week: new DateTime(2026, 9, 13), Hours: 987.0, People: 24),
            (Week: new DateTime(2026, 9, 20), Hours: 928.0, People: 24),
            (Week: new DateTime(2026, 9, 27), Hours: 944.5, People: 24),
            (Week: new DateTime(2026, 10, 4), Hours: 632.5, People: 19),
            (Week: new DateTime(2026, 10, 11), Hours: 292.5, People: 11),
        };
        var rows = fixture.SelectMany(week => Enumerable.Range(0, week.People).Select(i => new EmployeeWeeklyHours
        {
            EmployeeId = "E" + i, EmployeeName = $"{week.Week:yyyy-MM-dd} employee {i}", WeekStart = week.Week,
            TotalHrs = week.Hours / week.People, BillableHrs = week.Hours / week.People * (i == 0 ? .25 : .75),
        })).ToList();
        rows.Add(new EmployeeWeeklyHours { EmployeeId = "e0", WeekStart = new DateTime(2026, 10, 4) });

        var facts = MorningFacts.EmployeeWeekFacts(rows, new DateTime(2026, 10, 11));

        Assert.Contains("partial: week 2026-10-04, 19 of 24 people", facts[0].Lines);
        Assert.Contains("partial: week 2026-10-11, 11 of 24 people", facts[0].Lines);
        Assert.Equal(2, facts[0].Shown);
        Assert.Equal(7, facts[0].Total);
        var outliers = facts[1];
        Assert.Contains("last complete week 2026-09-27", outliers.Name, StringComparison.Ordinal);
        Assert.Equal(1, outliers.Shown);
        Assert.Contains(outliers.Lines, line => line.StartsWith("2026-09-27 employee 0", StringComparison.Ordinal));
        Assert.DoesNotContain(outliers.Lines, line => line.Contains("2026-10-04", StringComparison.Ordinal) || line.Contains("2026-10-11", StringComparison.Ordinal));
        Assert.NotNull(MorningFacts.EmployeeWeekFacts(Array.Empty<EmployeeWeeklyHours>(), new DateTime(2026, 10, 11))[1].CouldNotRead);
    }

    /// <summary>
    /// Checks quiet-day ranking, fee tie-breaks, date-only BD output and title fallback.
    /// Does not execute the pursuit/action SQL or validate live joins, scope, constructors/configuration, or engine numbers.
    /// </summary>
    [Fact]
    public void Pursuits_rank_by_days_quiet_and_BD_dates_and_title_fallbacks_are_plain()
    {
        var rows = new[]
        {
            new MorningFacts.Pursuit(1, "Owner", "Region", "Oldest", null, Now.AddDays(-50)),
            new MorningFacts.Pursuit(2, "Owner", "Region", "Known title", 1000, Now.AddDays(-30)),
            new MorningFacts.Pursuit(3, "Owner", "Region", "  ", 5000, Now.AddDays(-30), "Buyer organisation"),
            new MorningFacts.Pursuit(4, "Owner", "Region", "", null, Now.AddDays(-30), " "),
            new MorningFacts.Pursuit(5, "Owner", "Region", "Recent", 999999, Now.AddDays(-13)),
        };

        var stale = MorningFacts.PursuitFacts(rows, Now)[1];

        foreach (var (id, index) in new[] { 1, 3, 2, 4 }.Select((id, index) => (id, index)))
            Assert.StartsWith($"Pursuit #{id}:", stale.Lines[index], StringComparison.Ordinal);
        Assert.Contains("50 days quiet", stale.Lines[0], StringComparison.Ordinal);
        Assert.Contains("Pursuit #3: Buyer organisation;", stale.Lines[1], StringComparison.Ordinal);
        Assert.Contains("Pursuit #4: (untitled);", stale.Lines[3], StringComparison.Ordinal);
        Assert.All(stale.Lines, line => Assert.DoesNotMatch(@"\d{4}-\d{2}-\d{2}T", line));
        Assert.Contains("last touch/opened floor 2026-08-27", stale.Lines[0], StringComparison.Ordinal);
        var action = new BdActionRow(1, "ian", "Ian", "Call buyer", null, 20, Now.AddDays(-1), "Open",
            null, null, null, null, null, null, null, null, null, null);
        var actions = Assert.Single(MorningFacts.ActionFacts(new[] { action, action with { Id = 2, Status = "Closed" } }, Now));
        Assert.Equal(1, actions.Shown);
        Assert.Contains("(due 2026-10-15)", Assert.Single(actions.Lines), StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\d{4}-\d{2}-\d{2}T", actions.Lines[0]);
    }

    [Fact]
    public async Task Today_counts_all_sections_and_records_the_selected_move_rank()
    {
        var store = new Mock<IBdRhythmStore>();
        store.Setup(s => s.ListActionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BdActionRow>());
        store.Setup(s => s.ListMeetingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BdMeetingRow>());
        store.Setup(s => s.ListReferencesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BdReferenceRow>());
        store.Setup(s => s.ListLeadsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BdLeadRow>());
        store.Setup(s => s.GetHourAsync("Morning", Day, It.IsAny<CancellationToken>())).ReturnsAsync(new DirectorHourRow(12, Day, "Morning", Json(FixtureHour()), "session", "Ian", Now, null));
        store.Setup(s => s.ListMoveResultsAsync(12, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { new HourMoveResult(1, "Done", null, Now) });
        var vm = new BdRhythmViewModel(store.Object, () => Now);
        vm.SetDoctrineCards(new[] { Card(5, "BD"), Card(101, "Operations") });
        await vm.LoadAsync(CancellationToken.None);
        Assert.Equal(new[] { "BD", "COO", "IT" }, vm.HourSections.Select(s => s.Role));
        Assert.Equal("1 of 3 moves done · about 40 minutes left", vm.MovesProgressText);
        var coo = Assert.Single(vm.HourSections[1].Moves);
        Assert.Equal(101L, Assert.Single(coo.Cards).Id);
        await vm.RecordMoveAsync(coo, "NotToday", "Ian", CancellationToken.None);
        store.Verify(s => s.RecordMoveResultAsync(12, 2, "NotToday", null, "Ian", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("2 of 3 moves done · about 20 minutes left", vm.MovesProgressText);
        store.Verify(s => s.GetHourAsync("BD", Day, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Doctrine_selector_loads_operations_and_mutations_stay_with_the_selected_role()
    {
        var store = EmptyDoctrineStore();
        var bd = new[] { Card(5, "BD", "proposed") };
        var operations = new[] { Card(101, "Operations", "proposed"), Card(102, "Operations", "proposed"), Card(103, "Operations") };
        store.Setup(s => s.ListCardsAsync("BD", It.IsAny<CancellationToken>())).ReturnsAsync(() => bd);
        store.Setup(s => s.ListCardsAsync("Operations", It.IsAny<CancellationToken>())).ReturnsAsync(() => operations);
        store.Setup(s => s.ListRecordsAsync("Operations", It.IsAny<CancellationToken>())).ReturnsAsync(new[] { DoctrineCardRecord.Empty(103) });
        store.Setup(s => s.ListRunsAsync("Operations", 50, It.IsAny<CancellationToken>())).ReturnsAsync(new[]
        {
            new BdPromptRunRow(8, "ask", "Operations", "Working capital", null, "Ian", Now, Now, "answered", "Fixture answer", 0),
        });
        store.Setup(s => s.DecideCardAsync(It.IsAny<long>(), It.IsAny<bool>(), "Ian", It.IsAny<CancellationToken>()))
            .Callback<long, bool, string, CancellationToken>((id, accept, _, _) => operations = operations.Select(c => c.Id == id ? c with { Status = accept ? "accepted" : "rejected" } : c).ToArray())
            .ReturnsAsync(true);
        store.Setup(s => s.RetireCardAsync(103, "Ian", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        store.Setup(s => s.StartRunAsync("synthesis", "Operations", It.IsAny<string>(), It.IsAny<string?>(), "Ian", It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(10);
        var vm = new BdDoctrineViewModel(store.Object, () => Now);
        Assert.Equal("BD", vm.Role);
        await vm.LoadAsync(CancellationToken.None);
        await vm.SelectRoleAsync("Operations", CancellationToken.None);
        Assert.Equal(new long[] { 101, 102 }, vm.Proposed.Select(c => c.Id));
        Assert.Equal("Operations", Assert.Single(vm.Runs).Run.Role);
        Assert.False(await vm.DecideAsync(bd[0], true, "Ian", CancellationToken.None));
        Assert.True(await vm.DecideAsync(operations[0], false, "Ian", CancellationToken.None));
        Assert.Equal(1, await vm.AcceptAllAsync("Ian", CancellationToken.None));
        store.Verify(s => s.DecideCardAsync(5, It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.True(await vm.RetireAsync(vm.Accepted.Single(c => c.Id == 103), CancellationToken.None));
        var prompt = await vm.PrepareSynthesisAsync(new[] { "A book" }, null, "Ian", CancellationToken.None);
        Assert.Contains("Synthesize the Operations doctrine", prompt.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("BD card 5", prompt.Markdown, StringComparison.Ordinal);
        store.Verify(s => s.ListClosedPursuitsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Contains("x:Name=\"DoctrineRoleSelector\"", Source("Kor.Operations.App/BusinessDevelopment/Director/BdDirectorWindow.xaml"), StringComparison.Ordinal);
    }

    [Fact]
    public void Doctrine_records_include_citations_inside_morning_sections()
    {
        var records = DoctrineTrackRecord.Compute(new[] { (12L, Day, FixtureHour()) }, new[] { (12L, 2, "Done") },
            Array.Empty<(long, int, DateTimeOffset)>(), new long[] { 5, 101 });
        Assert.Equal(1, records.Single(r => r.CardId == 101).Done);
        Assert.Equal(1, records.Single(r => r.CardId == 5).Undecided);
    }

    [Fact]
    public async Task A_slow_BD_refresh_cannot_overwrite_the_new_operations_selection()
    {
        var store = EmptyDoctrineStore();
        var pending = new TaskCompletionSource<IReadOnlyList<DoctrineCardRecord>>(TaskCreationOptions.RunContinuationsAsynchronously);
        store.Setup(s => s.ListCardsAsync("BD", It.IsAny<CancellationToken>())).ReturnsAsync(new[] { Card(5, "BD") });
        store.Setup(s => s.ListCardsAsync("Operations", It.IsAny<CancellationToken>())).ReturnsAsync(new[] { Card(101, "Operations") });
        store.Setup(s => s.ListRecordsAsync("BD", It.IsAny<CancellationToken>())).Returns(pending.Task);
        var vm = new BdDoctrineViewModel(store.Object, () => Now);
        var earlier = vm.LoadAsync(CancellationToken.None);
        await vm.SelectRoleAsync("Operations", CancellationToken.None);
        pending.SetResult(new[] { DoctrineCardRecord.Empty(5) });
        await earlier;
        Assert.Equal("Operations", vm.Role);
        Assert.Equal(101L, Assert.Single(vm.Accepted).Id);
        Assert.Equal(2, vm.CitationCards.Count);
    }

    [Fact]
    public void Composer_and_runner_have_no_remote_model_client_and_verify_the_database_after_the_session()
    {
        var root = Root();
        var files = Directory.EnumerateFiles(Path.Combine(root, "tools", "MorningBrief"), "*.cs")
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "tools", "MorningBrief"), "*.csproj"))
            .Append(Path.Combine(root, "tools", "run-morning.ps1")).ToArray();
        foreach (var file in files)
            Assert.DoesNotMatch(@"(?i)api\.anthropic\.com|AskService|HttpClient|x-api-key|AnthropicClient|ChatClient|api\.openai\.com|Invoke-RestMethod", File.ReadAllText(file));
        var runner = Source("tools/run-morning.ps1");
        var session = runner.IndexOf("& $claude -p", StringComparison.Ordinal);
        var verify = runner.IndexOf("& $cli hour verify", StringComparison.Ordinal);
        Assert.True(session >= 0 && verify > session);
        Assert.Contains("--kind Morning --after $started", runner, StringComparison.Ordinal);
        Assert.Contains("exit $exitCode", runner, StringComparison.Ordinal);
        var cli = Source("tools/BdKnowledgeIngest/Program.cs");
        Assert.Contains("new[] { \"--kind\", \"--after\" }", cli, StringComparison.Ordinal);
        Assert.Contains("row.ImportedAtUtc <", cli, StringComparison.Ordinal);
        Assert.Contains("GetHourAsync(kind, date, ct)", cli, StringComparison.Ordinal);
    }

    private static Mock<IBdDoctrineStore> EmptyDoctrineStore()
    {
        var store = new Mock<IBdDoctrineStore>();
        store.Setup(s => s.ListCardsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<DoctrineCardRow>());
        store.Setup(s => s.ListRecordsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<DoctrineCardRecord>());
        store.Setup(s => s.ListRunsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BdPromptRunRow>());
        return store;
    }

    private static string Source(string path) => File.ReadAllText(Path.Combine(Root(), path));
    private static string Root()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) return dir.FullName;
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kor.Operations.App.BusinessDevelopment;
using Kor.Operations.App.BusinessDevelopment.Workspace;
using Kor.Operations.App.BusinessDevelopment.Director;
using Kor.Operations.App.Crm;
using Kor.Operations.App.Opportunities;
using Kor.Operations.Controls;
using Kor.Operations.Core.Services;
using Kor.Operations.Graph;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Awards;
using Kor.Opportunities.Core.Scoring;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.Heartbeat;
using Kor.Opportunities.Data.Ingestion;
using Kor.Opportunities.Data.Knowledge;
using Microsoft.Extensions.DependencyInjection;
using Kor.Opportunities.Data.Intel;
using Kor.Opportunities.Data.MajorProjects;
using Kor.Opportunities.Data.Observations;
using Kor.Opportunities.Data.Opportunities;
using Kor.Opportunities.Data.Sources;
using Moq;
using Xunit;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

/// <summary>
/// Renders the BD screens a BD user sees most to PNGs a person opens and LOOKS at
/// (%TEMP%\kor-bd-screens): the dashboard, the RFP window in both its states, Pursuits,
/// the pursuit-health board, and BD Director. The pattern is NetworkOpsWindowsRenderTests'.
///
/// Why it exists: on 2026-10-13/14 three BD screens shipped on string gates alone, and
/// Ian's eyes were the first render, three times ("I don't see ANY difference"; "still
/// ugly as fuck"; "no source and zero usable info"). CLAUDE.md rule 9: the output has
/// been looked at, rendered, not counted. Run this, open the PNGs, then ship.
///
/// WHAT IT COVERS: each view constructs on its REAL view model fed fixture data through
/// mocked stores; every StaticResource it names resolves; the layout is non-blank at the
/// workspace's size. A binding to a property the view model no longer has renders blank
/// rather than throwing, so the PNG is the check, not the pass.
/// WHAT IT DOES NOT COVER: whether it looks GOOD (that is what the PNGs are for, and a
/// person must open them); anything behind Loaded (the views are never shown, so no
/// service is touched and no timer starts); interaction; the real data's text lengths
/// and row counts. A same-class fault it would NOT catch: a list showing the wrong
/// population, because a retired duplicate renders exactly like a live row.
/// Row ORDER is the store's: fixtures are hand-ordered as its ORDER BY would return
/// them, so a wrong ORDER BY is a fault this harness would NOT catch.
/// A fixture value the store could not return would render as if real.
/// </summary>
[Trait("Speed", "Slow")]
public sealed class BdScreensRenderTests
{
    private static readonly Guid BcBid = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public void Bd_screens_build_and_render()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kor-bd-screens");
        Directory.CreateDirectory(dir);
        Exception? failure = null;
        var written = 0;

        // WPF demands STA and xUnit runs MTA, so the whole thing goes on its own thread.
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current is null) _ = new Application();
                Application.Current!.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/Kor.Operations.App;component/Themes/KorTheme.xaml", UriKind.Absolute),
                });
                // Every BD window gets the BD look when it loads (BdLookApplier); the screens here are drawn without a window.
                Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/Kor.Operations.App;component/BusinessDevelopment/Director/BdLook.xaml", UriKind.Absolute),
                });

                written += RenderDashboard(dir);
                written += RenderRealTenders(dir);
                written += RenderRfpWindows(dir);
                written += RenderPursuits(dir);
                written += RenderReview(dir);
                written += RenderPursuitHealth(dir);
                written += RenderDirector(dir);
                written += RenderReplyDialog(dir);
                written += RenderMyDay(dir);
                written += RenderBuilders(dir);
                BdDirectorPendingBidsRegressionTests.VerifySubmissionDialogs();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromMinutes(2)), "Rendering the BD screens did not finish within two minutes.");
        Assert.True(failure is null, $"A BD screen failed to build or render: {failure}");
        Assert.True(written >= 17, $"EXPECTED at least 17 renders; ACTUAL {written}");
    }

    private static int RenderBuilders(string dir)
    {
        var vm = new PursuitChecklistViewModel(Mock.Of<IPursuitRequirementStore>(), Mock.Of<IPursuitFileStore>(),
            1, "2026-RFP-IP-061", "reviewer");
        var file = BdDirectorBuildersRegressionTests.Attachment("2026-RFP-IP-061 - KOR Structural - Draft.docx");
        var attachment = new ChecklistAttachmentRow(file);
        attachment.SetMetadata(new Kor.Operations.Rendering.Proposal.ProposalDocxMetadata("Proposal author", "Reviewer", "7", Now.UtcDateTime));
        vm.Attachments.Add(attachment); vm.SelectedAttachment = attachment;
        vm.Items.Add(BdDirectorBuildersRegressionTests.Requirement(1, "PhrasePresent", "Executive Summary", "Met")
            with { Requirement = "Include an executive summary.", StatusNote = file.FileName + ": phrase found: Executive Summary" });
        vm.Items.Add(BdDirectorBuildersRegressionTests.Requirement(2, "FileName", "{rfp} - KOR Structural - *.docx", "Met")
            with { Requirement = "Name the submission with the RFP number and proponent.", StatusNote = file.FileName + ": file name matches" });
        vm.Items.Add(BdDirectorBuildersRegressionTests.Requirement(3, "PhrasePresent", "incorporates AI", "NotMet")
            with { Requirement = "Disclose the use of artificial intelligence in preparing the proposal.", StatusNote = file.FileName + ": phrase not found: incorporates AI" });
        vm.Items.Add(BdDirectorBuildersRegressionTests.Requirement(4, "Manual", null)
            with { StatusNote = "Verify the authorized signer's signature." });
        vm.Items.Add(BdDirectorBuildersRegressionTests.Requirement(5, "PageLimit", "20")
            with { Requirement = "The proposal must not exceed the stated page limit.", StatusNote = file.FileName + ": Render to PDF to count pages" });
        vm.SelectedItem = vm.Items[3];
        Assert.Equal(new[] { "Met", "Met", "NotMet", "Open", "Open" }, vm.Items.Select(i => i.Status));
        Assert.Equal("Manual", vm.Items[3].CheckKind);
        Assert.Contains("Render to PDF", vm.Items[4].StatusNote);
        var window = new PursuitChecklistWindow(vm);
        var content = (FrameworkElement)window.Content;
        window.Content = null; content.DataContext = vm;
        var count = RenderView(content, new Size(1320, 860), Path.Combine(dir, "director-checklist.png"));
        window.Close();

        var schedule = BdDirectorBuildersRegressionTests.FlaggedSchedule();
        Assert.Equal(2, schedule.Flags.Count);
        Assert.Contains(schedule.Flags, flag => flag.Contains("below the stated statutory load", StringComparison.Ordinal));
        Assert.Contains(schedule.Flags, flag => flag.Contains("quoted hourly implies", StringComparison.Ordinal));
        var editor = new Kor.Operations.App.FeeProposal.Editors.RfpRateScheduleEditor { DataContext = schedule };
        count += RenderView(editor, new Size(1100, 720), Path.Combine(dir, "fee-rate-schedule.png"));

        // Additional review artifacts from the same Leduc specification, not another screen count.
        var spec = Kor.Operations.Rendering.Proposal.OrgChartSpec.FromJson(File.ReadAllText(
            Path.Combine(BdDirectorBuildersRegressionTests.RepoRoot(), "tools/New-ProposalOrgChart.example.json")));
        var chart = Kor.Operations.App.FeeProposal.OrgChartRenderer.Render(spec);
        File.WriteAllText(Path.Combine(dir, "org-chart-leduc.svg"), chart.Svg);
        File.WriteAllBytes(Path.Combine(dir, "org-chart-leduc.png"), chart.Png);
        return count;
    }

    // ---- the 581 real open tenders of 2026-10-20 (hand-labelled for KorBidFit), as a lead first sees them: the
    // dashboard's Closing soon / New RFPs and the Opportunities > Bids tab. Titles are real; deadlines and dates are
    // spread so every row is open. Asserts no road work reaches either screen. ----

    private static int RenderRealTenders(string dir)
    {
        var path = Path.Combine(BdDirectorBuildersRegressionTests.RepoRoot(), "Kor.Opportunities.Data.Tests", "Fixtures", "KorBidFitLabels-2026-10-20.tsv");
        var opps = File.ReadLines(path).Where(l => l.Length > 0 && !l.StartsWith('#')).Select(l => l.Split('\t'))
            .Select((p, i) => new Opportunity
            {
                Id = long.Parse(p[0]), Name = p[2], BuyerName = "Buyer", ProjectProvince = "BC",
                SubmissionDeadlineUtc = Now.AddDays(1 + i % 28), CreatedAtUtc = Now.AddMinutes(-i),
            }).ToList();

        var opportunities = new Mock<IOpportunityStore>();
        opportunities.Setup(s => s.ListAsync(It.IsAny<CancellationToken>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(opps);
        var dashboard = new Mock<IBdDashboardStore>();
        dashboard.Setup(s => s.GetPipelineFunnelAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Kor.Opportunities.Data.MajorProjects.PipelineFunnel(RadarCount: 1079, BidWindowCount: 173, OpenSeatCount: 27));
        var dvm = new DashboardViewModel(opportunities.Object, dashboard.Object,
            new IntelReadService("Server=none;Database=none"), new IntelPersistenceService("Server=none;Database=none"));
        dvm.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert.DoesNotContain(dvm.LatestRfps, r => r.Name.Contains("Road Construction", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(dvm.Deadlines, r => r.Name.Contains("Watermain", StringComparison.OrdinalIgnoreCase));
        var written = RenderView(new DashboardView(dvm, Mock.Of<IServiceProvider>()), new Size(1680, 980), Path.Combine(dir, "dashboard-real-tenders.png"));

        var vm = new OpportunitiesViewModel(Mock.Of<IOpportunityStore>(), Mock.Of<IHeartbeatStore>(),
            Mock.Of<IOpportunityScoringService>(), Mock.Of<IIngestionRunStore>(), Mock.Of<IIngestionTriggerStore>(),
            Mock.Of<IOpportunitySourceStore>(), Mock.Of<IDeltekClientContextService>(),
            Mock.Of<ICrmEngagementStore>(), Mock.Of<ICrmActivityStore>(), Mock.Of<ICrmContactStore>(),
            Mock.Of<IOpportunityObservationStore>(), Mock.Of<IOpportunityInterestedFirmStore>(), Mock.Of<IPursuitGrabStore>());
        foreach (var o in opps) vm.Opportunities.Add(new OpportunityRowView(o) { Kind = OpportunityKind.Bid });
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton(vm);
        services.AddSingleton(sp => new OpportunitiesView(vm, sp));
        services.AddSingleton(new Kor.Operations.App.Options.UserOptions { UserUpnOverride = "omara@korstructural.com" });
        var hub = new OpportunitiesHubView(Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services));
        hub.ShowMode(allTenders: true);
        Assert.DoesNotContain(vm.FilteredOpportunitiesView.Cast<OpportunityRowView>(), r => r.Model.Name.Contains("Road Construction", StringComparison.OrdinalIgnoreCase));
        written += RenderView(hub, new Size(1680, 980), Path.Combine(dir, "bids-real-tenders.png"));

        // As the BD Workspace draws it (Director/BdLook.xaml).
        hub.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Kor.Operations.App;component/BusinessDevelopment/Director/BdLook.xaml", UriKind.Absolute),
        });
        written += RenderView(hub, new Size(1680, 980), Path.Combine(dir, "bids-bdlook.png"));
        return written;
    }

    // ---- the dashboard: funnel, Closing soon, New RFPs, Priority Actions (collapsed) ----

    private static int RenderDashboard(string dir)
    {
        var opportunities = new Mock<IOpportunityStore>();
        opportunities.Setup(s => s.ListAsync(It.IsAny<CancellationToken>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync(Opportunities());
        var dashboard = new Mock<IBdDashboardStore>();
        dashboard.Setup(s => s.GetPipelineFunnelAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Kor.Opportunities.Data.MajorProjects.PipelineFunnel(RadarCount: 1079, BidWindowCount: 173, OpenSeatCount: 27));

        var vm = new DashboardViewModel(opportunities.Object, dashboard.Object,
            new IntelReadService("Server=none;Database=none"), new IntelPersistenceService("Server=none;Database=none"));
        vm.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert.DoesNotContain("failed", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);

        var view = new DashboardView(vm, Mock.Of<IServiceProvider>());
        return RenderView(view, new Size(1680, 980), Path.Combine(dir, "dashboard.png"));
    }

    // ---- the RFP window: a live record with two postings, and a retired duplicate ----

    private static int RenderRfpWindows(string dir)
    {
        var live = Opportunities()[0];
        var removed = Opportunities()[1];

        var observations = new Mock<IOpportunityObservationStore>();
        observations.Setup(s => s.ListByOpportunityAsync(live.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new OpportunityObservation
                {
                    Id = 1942869, OpportunityId = live.Id, OpportunitySourceId = BcBid,
                    Title = "Development Consulting Services - Tumbler Ridge Secondary School Replacement",
                    Url = "https://bcbid.gov.bc.ca/page.aspx/en/bpm/process_manage_extranet/234361",
                    PostedDateUtc = Now.AddDays(-23), IngestedAtUtc = Now.AddDays(-23),
                    Description = "The Ministry of Infrastructure invites proposals from qualified consultants to provide " +
                                  "development consulting services for the replacement of Tumbler Ridge Secondary School. " +
                                  "Services include feasibility, site analysis, concept design, class D costing and a " +
                                  "business case suitable for Treasury Board submission. The successful proponent will " +
                                  "coordinate sub-consultants including structural, mechanical, electrical and civil.",
                },
                new OpportunityObservation
                {
                    Id = 1990619, OpportunityId = live.Id, OpportunitySourceId = BcBid,
                    Title = "Development Consulting Services - Tumbler Ridge Secondary School Replacement (Addendum 1)",
                    Url = "https://bcbid.gov.bc.ca/page.aspx/en/bpm/process_manage_extranet/234361",
                    PostedDateUtc = Now.AddDays(-9), IngestedAtUtc = Now.AddDays(-9),
                    Description = "Addendum 1: closing date extended; questions answered.",
                },
            });
        observations.Setup(s => s.ListByOpportunityAsync(removed.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OpportunityObservation>());
        var sources = new Mock<IOpportunitySourceStore>();
        sources.Setup(s => s.ListEnabledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new OpportunitySource { Id = BcBid, Name = "BC Bid" } });

        var written = 0;
        foreach (var (opportunity, label) in new[] { (live, "rfp-live"), (removed, "rfp-removed") })
        {
            var vm = new OpportunityDetailViewModel(opportunity, observations.Object, sources.Object);
            vm.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            var window = new OpportunityDetailWindow(vm);
            // SizeToContent="Height" leaves Height NaN on a never-shown window; render at the workspace's dialog size.
            written += Render((FrameworkElement)window.Content, new Size(1100, 720), Path.Combine(dir, label + ".png"));
        }

        return written;
    }

    // ---- Pursuits: the CRM page on five engagements ----

    private static int RenderPursuits(string dir)
    {
        var engagements = new Mock<ICrmEngagementStore>();
        engagements.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Engagements());
        var opportunities = new Mock<IOpportunityStore>();
        opportunities.Setup(s => s.ListAsync(It.IsAny<CancellationToken>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync(Opportunities());

        var vm = new CrmViewModel(engagements.Object, Mock.Of<ICrmActivityStore>(), Mock.Of<ICrmContactStore>(),
            opportunities.Object, Mock.Of<IDeltekClientContextService>(), Mock.Of<ICanonicalOrgStore>(),
            Mock.Of<IPursuitContextStore>(), Mock.Of<IOpportunityInterestedFirmStore>(),
            Mock.Of<IPursuitFileStore>(), Mock.Of<IPursuitFileStorage>());
        vm.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert.DoesNotContain("failed", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);

        var view = new CrmView(vm, Mock.Of<IServiceProvider>());
        return RenderView(view, new Size(1680, 980), Path.Combine(dir, "pursuits.png"));
    }

    // ---- BD Director v3: Today, Pursuits (with the side panel open on a reassignment) and Team ----
    // Tender rows follow the live pursuits of 21 Oct 2026 (Banff #1142, Peace River #1163, Leduc #1153 and #1112); the people
    // are the Director fixture's eight. Asks are illustrative.

    private static int RenderDirectorV3(BdDirectorWindow window, FrameworkElement content, BdDirectorViewModel vm, string dir)
    {
        var now = new DateTimeOffset(2026, 10, 21, 21, 30, 0, TimeSpan.Zero);
        RfpAction A(string what, string who, int y, int m, int d, string why) => new(what, who, new DateOnly(y, m, d), why);
        var board = new[]
        {
            new TenderBoardRow(1153, "Request for Proposals for Integrated Project Delivery (IPD) Team Project Partner – Civil Consultant West Campus Recreation Project – Phase 1",
                "City of Leduc", 1, "ishabana@korstructural.com", "Islam Shabana", new DateTimeOffset(2026, 10, 29, 20, 0, 0, TimeSpan.Zero), null, null, "no-go",
                "The work is civil engineering, not structural; KOR already bid the structural seat (IP-061) on 15 Oct.", Array.Empty<RfpQuestion>(), null,
                new[] { A("Archive the pursuit", "owner", 2026, 10, 22, "No-go: this is the civil seat.") }, null, false, 39716, now),
            new TenderBoardRow(1142, "Structural and Civil Engineering - Professional Services Agreement", "Banff Centre", 1, "ishabana@korstructural.com", "Islam Shabana",
                new DateTimeOffset(2026, 11, 5, 21, 0, 0, TimeSpan.Zero), null, null, "decide",
                "Decide: the first work package is squarely KOR's work, but the RFP also asks for civil utility design as a minimum qualification, which KOR does not do.",
                new[] { new RfpQuestion("Can a structural firm propose with a civil sub-consultant?", "Civil is a minimum qualification.", "Appendix E A.3"),
                        new RfpQuestion("Which governs on travel?", "Pricing is 40 points.", "Appendix E D"),
                        new RfpQuestion("Can Banff share the preliminary analysis?", "Methodology asks for it.", "A.3.4") },
                "Fri 23 Oct 2026, 2:00 pm MST", new[] { A("Send the 3 questions to Amaya Kingston", "owner", 2026, 10, 22, "Questions close Fri 2 pm."),
                    A("Decide go or no-go on the civil question", "Ian", 2026, 10, 23, "The answer decides whether KOR can bid.") }, "Amaya_Kingston@banffcentre.ca", false, 41243, now),
            new TenderBoardRow(1163, "Peace Regional Pool Structural Engineering Services", "Town of Peace River", 1, "ilalonde@korstructural.com", "Ian Lalonde",
                new DateTimeOffset(2026, 11, 13, 21, 0, 0, TimeSpan.Zero), null, null, "decide",
                "The core work is structural, and it is the kind KOR does: corroded steel deck, joists and beams over a pool, slide footings and pillars, concrete spalling (Schedule B 2.1; McCuaig FCA 3.1.1-3.1.3). Two things only Ian can settle decide the bid. First, can KOR staff site visits in Peace River, about 5 hours north of Edmonton, through tendering, construction in the Aug-Oct shutdown, and a one-year warranty inspection (Schedule B 2.2.8-2.2.10, 2.4)? Second, does KOR have three recent similar municipal projects, with the people it would propose, to show (RFP 6.1.6)?",
                Enumerable.Range(1, 8).Select(i => new RfpQuestion($"Question {i}", "Why", null)).ToArray(), "Fri 6 Nov 2026",
                new[] { A("Pick the three most recent similar municipal projects and ask those clients to act as references", "owner", 2026, 10, 23, "Experience is 25%."),
                    A("Check COR, TLC or SECOR and the insurance in Schedule F 8.2", "owner", 2026, 10, 27, "A condition of award."),
                    A("Decide go or no-go", "Ian", 2026, 10, 28, "Before the 30 Oct site meeting."),
                    A("Line up sub-consultants", "owner", 2026, 10, 30, "Testing, slide, finishes."),
                    A("Send the questions to Tanya Bell", "Ian", 2026, 11, 4, "Questions close 6 Nov.") }, "tbell@peaceriver.ca", false, 41438, now),
            new TenderBoardRow(1112, "Request for Proposals for Integrated Project Delivery (IPD) Team Project Partner – Structural Consultant", "City of Leduc", 3,
                "ishabana@korstructural.com", "Islam Shabana", new DateTimeOffset(2026, 10, 15, 20, 0, 0, TimeSpan.Zero), "Expect a short-list call", null, null, null,
                Array.Empty<RfpQuestion>(), null, Array.Empty<RfpAction>(), null, false, 39332, now),
            // A grabbed forward project: no tender yet (illustrative).
            new TenderBoardRow(1170, "Mayerthorpe School Replacement", "Alberta Infrastructure", 1, "rbeirne@korstructural.com", "Rory Beirne",
                null, null, null, null, null, Array.Empty<RfpQuestion>(), null, Array.Empty<RfpAction>(), null, false, null, now),
        };
        var asks = new[]
        {
            new BdAsk(11, "ilalonde@korstructural.com", 1163, null, "Decide go or no-go on Peace River", null, 30, new DateTimeOffset(2026, 10, 28, 23, 0, 0, TimeSpan.Zero), "Claude", now, "Open", null, null, null),
            new BdAsk(12, "ilalonde@korstructural.com", 1163, null, "Send the Peace River questions to Tanya Bell", null, 30, new DateTimeOffset(2026, 11, 4, 23, 0, 0, TimeSpan.Zero), "Claude", now, "Open", null, null, null),
            new BdAsk(13, "ilalonde@korstructural.com", 1142, null, "Decide go or no-go on Banff's civil question", null, 30, new DateTimeOffset(2026, 10, 23, 23, 0, 0, TimeSpan.Zero), "Claude", now, "Open", null, null, null),
        };
        var roster = new[]
        {
            new DirectorPerson("Ian Lalonde", "ilalonde@korstructural.com", 2), new DirectorPerson("Islam Shabana", "ishabana@korstructural.com", 3),
            new DirectorPerson("Rory Beirne", "rbeirne@korstructural.com", 7), new DirectorPerson("James DesRoches", "jdesroches@korstructural.com", 30),
        };
        var home = window.Home;
        home.Load(board, asks, vm.People.ToList(), roster, "ilalonde@korstructural.com", "Ian Lalonde", now);
        home.LoadGrabs(new[]
        {
            new RecentGrab(1142, "Structural and Civil Engineering - Professional Services Agreement", "Banff Centre", "ishabana@korstructural.com", "Islam Shabana", now.AddHours(-2), false),
            new RecentGrab(1170, "Mayerthorpe School Replacement", "Alberta Infrastructure", "rbeirne@korstructural.com", "Rory Beirne", now.AddHours(-26), true),
        });
        home.HourTitle = "Close the week's decisions before Friday's question deadlines";
        home.HourProgress = "2 of 5 moves done";
        Assert.True(home.Decisions.Count == 3 && home.Forward.Count == 1 && home.Deciding.Count == 3 && home.Submitted.Count == 1 && home.MyList.Count == 3 && home.NewGrabs.Count == 2,
            $"EXPECTED 3 decisions / 1 forward / 3 out for bid / 1 submitted / 3 on the list / 2 new grabs; ACTUAL {home.Decisions.Count} / {home.Forward.Count} / {home.Deciding.Count} / {home.Submitted.Count} / {home.MyList.Count} / {home.NewGrabs.Count}");
        Assert.Equal("Islam grabbed Structural and Civil Engineering - Professional Services Agreement", home.NewGrabs[0].Title);
        Assert.Contains("Claude: your call", home.NewGrabs[0].Sub, StringComparison.Ordinal);
        Assert.Contains("no RFP yet", home.NewGrabs[1].Sub, StringComparison.Ordinal);

        var written = 0;
        ((System.Windows.Controls.RadioButton)window.FindName("NavToday")).IsChecked = true;
        written += RenderView(content, new Size(1680, 980), Path.Combine(dir, "director-v3-today.png"));
        ((System.Windows.Controls.RadioButton)window.FindName("NavPursuits")).IsChecked = true;
        home.Selected = home.Deciding.First(c => c.Row.EngagementId == 1163);
        home.ReassignTo = roster[1];
        Assert.Contains("3 actions", home.ReassignPreview, StringComparison.Ordinal);
        Assert.Contains("Your 2 stay with you", home.ReassignPreview, StringComparison.Ordinal);
        written += RenderView(content, new Size(1680, 980), Path.Combine(dir, "director-v3-pursuits.png"));
        ((System.Windows.Controls.RadioButton)window.FindName("NavTeam")).IsChecked = true;
        written += RenderView(content, new Size(1680, 980), Path.Combine(dir, "director-v3-team.png"));

        // The BD Workspace a lead uses lands on the same Today, scoped to them: Islam's view. Follow-ups are illustrative.
        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(ICanonicalOrgStore))).Returns(Mock.Of<ICanonicalOrgStore>());
        var workspace = new BdWorkspaceWindow(services.Object, () => null!);
        var lead = new BdDirectorHomeViewModel();
        var islamAsks = new[]
        {
            new BdAsk(21, "ishabana@korstructural.com", 1142, null, "Send the 3 questions to Amaya Kingston", null, 30, new DateTimeOffset(2026, 10, 22, 23, 0, 0, TimeSpan.Zero), "Claude", now, "Open", null, null, null),
            new BdAsk(22, "ishabana@korstructural.com", 1153, null, "Archive the pursuit", null, 30, new DateTimeOffset(2026, 10, 22, 23, 0, 0, TimeSpan.Zero), "Claude", now, "Open", null, null, null),
        };
        lead.Load(board, islamAsks, Array.Empty<BdDirectorPersonRow>(), Array.Empty<DirectorPerson>(), "ishabana@korstructural.com", "Islam Shabana", now, ownOnly: true);
        lead.LoadFollowUps(new[]
        {
            new DirectorFollowUp(901, "Town Hall seismic upgrade", "City of Red Deer", true, now.AddDays(-9), now.AddDays(-30)),
            new DirectorFollowUp(902, "Arena roof assessment", "Town of Stony Plain", false, null, now.AddDays(-45)),
            new DirectorFollowUp(903, "Library expansion feasibility", "Strathcona County", false, null, null),
        });
        // A tender Rory grabbed joined Islam's forward pursuit: Islam's first look (illustrative).
        lead.LoadJoins(new[]
        {
            new PendingJoin(1171, 41500, "Request for Proposals - Lac Ste. Anne Seniors Lodge Replacement - Prime Consultant Services",
                "Lac Ste. Anne Seniors Lodge Replacement", "rbeirne@korstructural.com", "Rory Beirne", now.AddMinutes(-40)),
        });
        Assert.Equal("The RFP for Lac Ste. Anne Seniors Lodge Replacement has posted", lead.Joins.Single().Title);
        Assert.Contains("it becomes Rory's own pursuit", lead.Joins.Single().Explain, StringComparison.Ordinal);
        Assert.True(lead.Decisions.Count == 2 && lead.Decisions.All(d => d.Row.OwnerMailbox == "ishabana@korstructural.com") && !lead.HasNudge,
            $"EXPECTED Islam's 2 tenders and no team nudge; ACTUAL {lead.Decisions.Count} / nudge {lead.HasNudge}");
        Assert.Equal("NEEDS YOUR ATTENTION", lead.DecisionsHeader);
        ((System.Windows.Controls.ContentControl)workspace.FindName("ContentHost")).Content = new DirectorTodayView { DataContext = lead };
        var wsContent = (FrameworkElement)workspace.Content;
        workspace.Content = null;
        wsContent.Resources.MergedDictionaries.Add(workspace.Resources);
        written += RenderView(wsContent, new Size(1680, 980), Path.Combine(dir, "workspace-today.png"));
        workspace.Close();
        return written;
    }

    // ---- Claude's RFP review: the strip on a pursuit in its three states, and the review window (Banff Centre, 21 Oct) ----

    private static int RenderReview(string dir)
    {
        var engagements = new Mock<ICrmEngagementStore>();
        engagements.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Engagements());
        var opportunities = new Mock<IOpportunityStore>();
        opportunities.Setup(s => s.ListAsync(It.IsAny<CancellationToken>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync(Opportunities());
        var files = new Mock<IPursuitFileStore>();
        files.Setup(s => s.ListByEngagementAsync(It.IsAny<long>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<PursuitFile>
        {
            new(29, 501, 39410, "Appendix C - Rate Bid Form.xlsx", "", null, 30_650, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Now.AddHours(-2), "apc", true),
            new(28, 501, 39410, "Appendix D - Past Performance Questionnaire.pdf", "", null, 473_605, "application/pdf", Now.AddHours(-2), "apc", true),
            new(27, 501, 39410, "RFP# PF-1026-02.pdf", "", null, 1_193_698, "application/pdf", Now.AddHours(-2), "apc", true),
        });
        var storage = new Mock<IPursuitFileStorage>();
        storage.SetupGet(s => s.IsConfigured).Returns(true);

        var activities = new Mock<ICrmActivityStore>();
        activities.Setup(s => s.ListByEngagementAsync(It.IsAny<long>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<CrmActivity>());
        var contacts = new Mock<ICrmContactStore>();
        contacts.Setup(s => s.ListByEngagementAsync(It.IsAny<long>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<CrmContact>());

        var vm = new CrmViewModel(engagements.Object, activities.Object, contacts.Object,
            opportunities.Object, Mock.Of<IDeltekClientContextService>(), Mock.Of<ICanonicalOrgStore>(),
            Mock.Of<IPursuitContextStore>(), Mock.Of<IOpportunityInterestedFirmStore>(), files.Object, storage.Object);
        vm.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        var view = new CrmView(vm, Mock.Of<IServiceProvider>());
        vm.Selected = vm.Engagements.First();
        DrainDispatcher();

        var review = BanffReview();
        var status = new PursuitReviewStatus(501, "APC", 3, Now.AddHours(-2), Now.AddHours(-2), true, null, null, null, null, null, null, 0);
        var written = 0;
        vm.ShowReviewForRender(PursuitRfpReview.Describe(status with { PortalFiles = 0, LastFileAtUtc = null }, Now), null, running: false);
        written += RenderView(view, new Size(1680, 980), Path.Combine(dir, "review-strip-waiting.png"));
        vm.ShowReviewForRender(PursuitRfpReview.Describe(status with { RunId = 7, RunCreatedAtUtc = Now.AddMinutes(-4), RunCreatedBy = "ilalonde@korstructural.com" }, Now), null, running: true);
        written += RenderView(view, new Size(1680, 980), Path.Combine(dir, "review-strip-reading.png"));
        vm.ShowReviewForRender(PursuitRfpReview.Describe(status with { Reviews = 1, RunId = 7, RunCreatedAtUtc = Now.AddMinutes(-30), RunReportedAtUtc = Now.AddMinutes(-3) }, Now), review, running: false);
        written += RenderView(view, new Size(1680, 980), Path.Combine(dir, "review-strip-ready.png"));

        // The same screen as the BD Workspace draws it: the BD look's buttons and tables (Director/BdLook.xaml).
        view.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Kor.Operations.App;component/BusinessDevelopment/Director/BdLook.xaml", UriKind.Absolute),
        });
        written += RenderView(view, new Size(1680, 980), Path.Combine(dir, "pursuits-bdlook.png"));

        var window = new PursuitReviewWindow(review, "Structural and Civil Engineering – Professional Services Agreement (RFP PF-1026-02)",
            "Banff Centre for Arts and Creativity", "Amaya_Kingston@banffcentre.ca", new DateTimeOffset(2026, 11, 2, 21, 0, 0, TimeSpan.Zero), () => { },
            "ishabana@korstructural.com");
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        written += RenderView(content, new Size(980, 860), Path.Combine(dir, "review-window.png"));
        window.Close();
        return written;
    }

    private static PursuitReviewRow BanffReview() => new(1, 501, null, "decide",
        "Banff Centre for Arts and Creativity wants structural and civil engineers on a Master Services Agreement: 3 years, extendable to 9, paid hourly as needed, with no guaranteed volume. Only one firm gets the first work package: the Theatre Complex roof trusses and joists, which a preliminary analysis says are undersized, plus the parkade and gangways. Scoring: pricing 40, team 20, qualifications and references 20, scope coverage 10, methodology 10; the top 3 present.",
        "Decide: the first work package is squarely KOR's work, but the RFP also asks for civil utility design as a minimum qualification, which KOR does not do (Appendix E A.3).\n"
        + "The rate form has a line for sub-consultant mark-up, which suggests a civil sub-consultant is acceptable; ask before investing in the proposal.\n"
        + "Pricing is 40 of 100 points; the RFP and the rate form disagree on how travel to Banff is charged.\n"
        + "References: 3 to 5 past clients must send Banff the questionnaire directly - start those requests now if it is a go.",
        new[]
        {
            new RfpQuestion("Will Banff Centre accept a proposal from a structural engineering firm that provides the civil utility services through a named civil sub-consultant?", "Civil utility experience is a minimum qualification; KOR is structural only. The answer decides whether KOR can bid at all.", "Appendix E A.2.1, A.3"),
            new RfpQuestion("Section D says rates must include all travel, but the Rate Bid Form has separate travel lines. Which governs?", "Pricing is 40 points; travel built into hourly rates makes them look higher than local firms'.", "Appendix E D; Appendix C"),
            new RfpQuestion("Can Banff Centre share the preliminary analysis of the Theatre Complex roof trusses and the existing drawings?", "The methodology section asks for a detailed approach to this exact work.", "Appendix E A.2.2, A.3.4"),
        },
        "Fri 23 Oct 2026, 2:00 pm MST", "Amaya_Kingston@banffcentre.ca", "All three files read.", null, "Claude (session with Ian, 21 Oct)", Now.AddMinutes(-3),
        new[]
        {
            new RfpAction("Decide go or no-go on the civil question", "Ian", new DateOnly(2026, 10, 22), "Questions close Fri 23 Oct 2:00 pm MST; the civil answer decides whether KOR can bid."),
            new RfpAction("Send the 3 questions to Amaya Kingston", "owner", new DateOnly(2026, 10, 22), "Gives Banff a day to answer before the cutoff."),
            new RfpAction("Ask 3 to 5 past clients to return the Appendix D questionnaire", "owner", new DateOnly(2026, 10, 27), "Clients reply to Banff directly; they need lead time."),
            new RfpAction("Confirm a civil sub-consultant and their rates", "owner", new DateOnly(2026, 10, 28), "The rate form asks for Lead Civil, Senior Civil and Civil Designer rates."),
        });

    // ---- the pursuit-health board: cold, watch, warm, never touched ----

    private static int RenderPursuitHealth(string dir)
    {
        var store = new Mock<IPursuitOverwatchStore>();
        store.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[]
        {
            new PursuitOverwatchRow(501, 39410, "ilalonde@korstructural.com", (int)CrmEngagementStage.Drafting,
                "Prime Consultant Services: RCH Mammography Replacement", "Fraser Health Authority", "Lower Mainland",
                Now.AddDays(-35), Now.AddDays(-60), 3, null, Now.AddDays(-34)),
            new PursuitOverwatchRow(504, null, "jbryson@korstructural.com", (int)CrmEngagementStage.Drafting,
                "North Langford Secondary School", "School District 62 (Sooke)", "Vancouver Island",
                null, Now.AddDays(-15), 0, null, Now.AddDays(-15)),
            new PursuitOverwatchRow(502, 39411, "rwong@korstructural.com", (int)CrmEngagementStage.Submitted,
                "Request for Supplier Qualifications for Engineering Prime Consultant Services", "Government of Alberta (APC)", "Alberta",
                Now.AddDays(-12), Now.AddDays(-40), 7, Now.AddDays(-12), Now.AddDays(-5)),
            new PursuitOverwatchRow(503, 39412, "ilalonde@korstructural.com", (int)CrmEngagementStage.Drafting,
                "Whitecourt Project – Prime Consultant Services", "Government of Alberta (APC)", "Alberta",
                Now.AddDays(-2), Now.AddDays(-8), 2, Now.AddDays(-1), Now.AddDays(-8)),
        });

        var vm = new OverwatchViewModel(store.Object);
        vm.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert.DoesNotContain("failed", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);

        var view = new OverwatchView(vm, Mock.Of<IGraphFacade>(), Mock.Of<IProposalStaffStore>());
        return RenderView(view, new Size(1680, 760), Path.Combine(dir, "pursuit-health.png"));
    }

    private static int RenderDirector(string dir)
    {
        // Brief 9a provides all live counts, aggregate overdue/no-action totals,
        // Omar's 5/19 and Ian's 0/1, but omits the other per-owner 2-column table.
        // Those six splits below are ILLUSTRATIVE and preserve 24 overdue / 74
        // missing / 3 future. They are not additional production measurements.
        // Display names beyond Omar use the names supplied by the brief.
        var people = new[]
        {
            new BdDirectorPerson("omara@korstructural.com", "Omar Alcazar Pastrana", new[] { "Omar", "omara@korstructural.com" }, 25, 5, 19, 25, 0, 0, 0, 3, 0, SecondSeat: 2), // illustrative assigned count
            new BdDirectorPerson("Conor", "Conor", new[] { "Conor" }, 22, 5, 16, 22, 0, 0, 0, 3, 0),
            new BdDirectorPerson("Islam", "Islam", new[] { "Islam" }, 20, 5, 15, 20, 0, 0, 0, 3, 0),
            new BdDirectorPerson("Jim", "Jim", new[] { "Jim" }, 18, 5, 12, 18, 0, 0, 0, 8, 0),
            new BdDirectorPerson("John", "John", new[] { "John" }, 9, 3, 6, 9, 0, 0, 0, 7, 0),
            new BdDirectorPerson("Rory", "Rory", new[] { "Rory" }, 4, 1, 3, 4, 0, 0, 0, 0, 0),
            new BdDirectorPerson("jbryson@korstructural.com", "John Bryson", new[] { "jbryson@korstructural.com" }, 2, 0, 2, 2, 0, 0, 0, 1, 2),
            new BdDirectorPerson("ilalonde@korstructural.com", "Ian", new[] { "ilalonde@korstructural.com" }, 1, 0, 1, 1, 0, 0, 0, 0, 6, SecondSeat: 1),
        };
        Assert.True(people.Sum(p => p.Live) == 101 && people.Sum(p => p.OverdueNextAction) == 24
                    && people.Sum(p => p.NoNextAction) == 74 && people.Sum(p => p.BuyersMailed90) == 25,
            "EXPECTED Director fixture totals 101 live / 24 overdue / 74 missing / 25 buyers emailed; ACTUAL fixture totals differ");

        // Detail titles, dates, stage split/medians, deadline coverage and source
        // fees are layout examples; the brief does not measure those dimensions.
        var records = new List<CrmEngagement>();
        var drill = new List<PursuitOverwatchRow>();
        var names = new[] { "North Langford Secondary School", "Prime Consultant Services: RCH Mammography Replacement",
            "Whitecourt Project – Prime Consultant Services" };
        var buyers = new[] { "School District 62 (Sooke)", "Fraser Health Authority", "Government of Alberta" };
        foreach (var person in people)
        for (var index = 0; index < person.Live; index++)
        {
            var id = records.Count + 1L;
            var stage = id % 3 == 0 ? CrmEngagementStage.Submitted : CrmEngagementStage.Drafting;
            var nameIndex = id == 26 ? 1 : id == 48 ? 2 : index % names.Length;
            long? opportunityId = id is 1 or 26 or 48 ? 9000 + id : null;
            records.Add(new CrmEngagement
            {
                Id = id, OpportunityId = opportunityId, Stage = stage, OwnerStaffId = person.OwnerKeys[0],
                OpenedAtUtc = Now.AddDays(-150), UpdatedAtUtc = Now.AddDays(-41),
            });
            drill.Add(new PursuitOverwatchRow(id, opportunityId, person.OwnerKeys[0], (int)stage, names[nameIndex],
                buyers[nameIndex], null, index % 2 == 0 ? Now.AddDays(-120) : null,
                Now.AddDays(-150), index % 2 == 0 ? 1 : 0, null, Now.AddDays(-110)));
        }
        // Historical source rows remain separate from the 101 live pursuits.
        foreach (var id in new long[] { 1001, 1002 })
            records.Add(new CrmEngagement { Id = id, Stage = CrmEngagementStage.Won, ExternalSource = "Deltek.CustomProposal", ProposedFee = 62500m });

        var director = new Mock<IBdDirectorStore>();
        director.Setup(s => s.GetPeopleAsync(It.IsAny<CancellationToken>())).ReturnsAsync(people);
        director.Setup(s => s.GetFunnelAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new BdDirectorFunnel(
            new Dictionary<int, double> { [1] = 110, [3] = 95 },
            new[]
            {
                new BdDirectorUndecidedPursuit(1, names[0], buyers[0], "Omar", Now.AddDays(-65)),
                new BdDirectorUndecidedPursuit(26, names[1], buyers[1], "Conor", Now.AddDays(-54)),
                new BdDirectorUndecidedPursuit(48, names[2], buyers[2], "Islam", Now.AddDays(-43)),
            }, 98, 0, 0, 0));
        var pursuits = new Mock<IPursuitOverwatchStore>();
        pursuits.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(drill);
        var recordStore = new Mock<ICrmEngagementStore>();
        recordStore.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(records);
        var attribution = new Mock<IBdAttributionStore>();
        // The requested second source row is a rendering placeholder with zero
        // wins. The real source query returns only sources with at least one win;
        // adding a second real winning source would contradict Won=0 here.
        attribution.Setup(s => s.GetSummaryAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new BdAttributionSummary(
            0, 0m, 101, records.Count(r => r.Stage == CrmEngagementStage.Submitted), 0,
            new[] { new AttributionSourceRow("Deltek.CustomProposal", 2, 125000m), new AttributionSourceRow("Grab.BC Bid", 0, 0m) },
            Array.Empty<AttributionOwnerRow>()));
        var accounts = DirectorAccounts();
        var measuredAt = new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero);
        Assert.True(accounts.Count == 15 && accounts.Count(a => a.LivePursuits > 0) == 4
            && accounts.Count(a => a.LastTouchUtc >= measuredAt.AddDays(-90) && a.LastTouchUtc <= measuredAt) == 3,
            "EXPECTED 15 accounts / 4 with live pursuits / 3 touched within 90 days of the measurement date; ACTUAL fixture differs");
        director.Setup(s => s.GetAccountsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(accounts);
        var start = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var targets = new[]
        {
            new BdTarget(1, BdTargetMetrics.LivePursuits, "Firm", null, start, start.AddMonths(3), 120, null, "Ian", measuredAt),
            new BdTarget(2, BdTargetMetrics.AccountsWithLivePursuit, "Firm", null, start, start.AddMonths(3), 100, null, "Ian", measuredAt),
        };
        var ledger = new Mock<IBdDirectorLedgerStore>();
        ledger.Setup(s => s.ListTargetsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(targets);
        director.Setup(s => s.GetTargetActualsAsync(It.IsAny<IReadOnlyList<BdTargetKey>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BdTargetActuals(new Dictionary<BdTargetKey, decimal>
            {
                [new BdTargetKey(targets[0].Metric, "Firm", null, start, start.AddMonths(3))] = 101,
                [new BdTargetKey(targets[1].Metric, "Firm", null, start, start.AddMonths(3))] = 77,
            }, 101, 0));
        // Asks, decisions, account dates/open asks/client identifiers and the
        // selected Deltek summary below are illustrative layout data, not measurements.
        ledger.Setup(s => s.ListAsksAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(new[]
        {
            new BdAsk(1, "Omar", 1, null, "Confirm the buyer's next meeting date.", "The next step has been waiting.", 30, measuredAt.AddDays(7), "Ian", measuredAt, "Open", null, null, null),
            new BdAsk(2, "Conor", null, 141, "Arrange a short account review.", "Agree on who follows up.", 60, measuredAt.AddDays(9), "Ian", measuredAt, "Open", null, null, null),
            new BdAsk(3, "Islam", null, null, "Bring two prospects for discussion.", null, 45, measuredAt.AddDays(14), "Ian", measuredAt, "Open", null, null, null),
        });
        var deltek = new Mock<IDeltekClientContextService>();
        deltek.Setup(s => s.LoadAsync(accounts[0].ClendorClientId, It.IsAny<CancellationToken>())).ReturnsAsync(new DeltekClientIntelligence
        {
            ClientId = accounts[0].ClendorClientId, ClientName = accounts[0].DisplayName, LifetimeFee = 12500000m, ProjectCount = 378,
            LatestProjectName = "Client portfolio review", LatestProjectStart = new DateTime(2026, 8, 1),
            SummaryPostedThrough = new DateTime(2026, 9, 1), Ar = new DeltekArSummary(125000m, 18000m, 7),
        });
        var claims = new Mock<IKnowledgeClaimStore>();
        // Passage counts are measured in brief 9d. All extraction/review counts,
        // claims and passage text below are ILLUSTRATIVE, not extracted book content.
        claims.Setup(s => s.ListSourcesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[]
        {
            new KnowledgeSourceSummary(4, "Managing the professional service firm", "David Maister", 663, 16, 3, 1, 0),
            new KnowledgeSourceSummary(11, "SPIN Selling", "Neil Rackham", 316, 8, 2, 0, 0),
            new KnowledgeSourceSummary(18, "The win without pitching manifesto", "Blair Enns", 114, 8, 2, 1, 1),
        });
        var concept = new KnowledgeConceptRow(1, "qualify-before-investing", "Qualify before investing", 7, 3);
        claims.Setup(s => s.ListConceptsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { concept });
        var illustrativeClaims = new[]
        {
            new KnowledgeClaimRow(1, 4, "Managing the professional service firm", 101, "Illustrative passage A", "Rule",
                "Illustrative: agree on a buyer's need before preparing a proposal.", 0.95m, new[] { concept.Name }, null, null, null),
            new KnowledgeClaimRow(2, 4, "Managing the professional service firm", 102, "Illustrative passage B", "Heuristic",
                "Illustrative: review the cost of a pursuit before assigning more staff time.", 0.80m, new[] { concept.Name }, null, null, null),
            new KnowledgeClaimRow(3, 4, "Managing the professional service firm", 103, "Illustrative passage C", "Rule",
                "Illustrative: check the available time.", 0.85m, new[] { concept.Name }, null, null, null),
        };
        claims.Setup(s => s.ListClaimsAsync(4, "Unreviewed", It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(illustrativeClaims);
        claims.Setup(s => s.GetPassageAsync(It.IsAny<long>(), It.IsAny<CancellationToken>())).ReturnsAsync((long id, CancellationToken ct) =>
            new KnowledgePassageText(id, "Managing the professional service firm", id == 101 ? "Illustrative passage A" : "Illustrative passage B",
                "ILLUSTRATIVE RENDER FIXTURE — not a quotation or extracted passage.\n\nThis sample text shows how the reader can compare a claim with its source passage. The production view displays the full stored passage and its original reference."));
        var market = new DirectorMarketFixture("Managing the professional service firm", "Managing the professional service firm");
        market.Scoring.SetupLedger(ledger);
        market.Scoring.SetupVerdicts(claims);
        DirectorMarketDefaults.SetupPendingBids(director);
        market.SetupSignals(director);
        claims.Setup(s => s.GetClaimAsync(It.IsAny<long>(), It.IsAny<CancellationToken>())).ReturnsAsync((long id, CancellationToken ct) =>
            illustrativeClaims.Single(c => c.Id == id) with
            {
                RetiredAtUtc = id == 2 ? DateTimeOffset.UnixEpoch : null,
                RetiredReason = id == 2 ? "Illustrative retirement reason." : null,
            });
        using var vm = new BdDirectorViewModel(director.Object, pursuits.Object, recordStore.Object, attribution.Object, ledger.Object, deltek.Object, claims.Object,
            market.Dashboard.Object, market.Opportunities.Object, market.Brief, Mock.Of<IPursuitFileStore>(), Mock.Of<ICrmActivityStore>(), DirectorMarketDefaults.Staff());
        vm.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(vm.Live == 101 && vm.PeopleCount == 8 && vm.Outcomes == "No live pursuit has been decided yet.",
            $"EXPECTED 101 live / 8 people / no decided live pursuits; ACTUAL {vm.Live} / {vm.PeopleCount} / {vm.Outcomes}");

        var window = new BdDirectorWindow(vm, Mock.Of<IProposalStaffStore>());
        ((System.Windows.Controls.RadioButton)window.FindName("NavLibrary")).IsChecked = true; // v3: the tabbed screens live in Library
        var content = (FrameworkElement)window.Content;
        var tabs = Assert.IsType<System.Windows.Controls.TabControl>(window.FindName("Tabs"));
        // Render just the content; Loaded never fires and no telemetry/DI/DB runs.
        // Preserve the window's resources and inherited DataContext on detaching.
        window.Content = null;
        content.DataContext = vm;
        content.Resources.MergedDictionaries.Add(window.Resources);
        var v3 = RenderDirectorV3(window, content, vm, dir);
        ((System.Windows.Controls.RadioButton)window.FindName("NavLibrary")).IsChecked = true; // the older screens live in Library now
        var files = new[] { "director-number.png", "director-people.png", "director-funnel.png", "director-accounts.png", "director-asks.png", "director-knowledge.png", "director-market.png", "director-week.png" };
        var written = 0;
        for (var i = 0; i < files.Length; i++)
        {
            SelectData(tabs, i + 3); // inside Data, Actions, Meetings and References come first
            if (i == 3)
            {
                vm.SelectedAccount = vm.Accounts[0];
                vm.AccountLoadTask.GetAwaiter().GetResult();
            }
            if (i == 5)
            {
                vm.ClaimsLoadTask.GetAwaiter().GetResult();
                vm.PassageLoadTask.GetAwaiter().GetResult();
                Assert.Equal(new[] { 663, 316, 114 }, vm.KnowledgeBooks.Select(b => b.Source.Passages).ToArray());
                Assert.NotNull(vm.SelectedPassage);
            }
            if (i == 6)
                Assert.True(vm.MarketSignals.Count == 3 && vm.MarketDeadlines.Count == 3 && vm.ForwardPipeline.Count == 3 && vm.OpenSeats.Count == 2,
                    "EXPECTED illustrative Market rows: 3 signals, 3 RFPs, 3 projects, 2 seats.");
            if (i == 7)
            {
                Assert.True(vm.BriefSections.Count == 5 && vm.BriefSections.All(s => s.Citations.Count == 2),
                    "EXPECTED five illustrative sections with two citations each.");
                vm.SelectedBriefCitation = vm.BriefSections[0].Citations[1];
                vm.PassageLoadTask.GetAwaiter().GetResult();
                Assert.Contains("Retired", vm.CitationStatus, StringComparison.Ordinal);
                Assert.NotNull(vm.SelectedPassage);
            }
            content.UpdateLayout();
            written += RenderView(content, new Size(1680, 980), Path.Combine(dir, files[i]));
            if (i == 1)
            {
                Assert.Contains(VisualChildren<System.Windows.Controls.DataGrid>(content), grid =>
                    grid.Columns.Any(column => Equals(column.Header, "Second seat")));
                Assert.Equal(1, vm.People.Single(p => p.PersonKey == "ilalonde@korstructural.com").SecondSeat);
            }
            if (i == 4)
            {
                Assert.Equal("Brief calls: 6 scored · 3 right · 2 wrong · 1 unclear · 2 past review unscored", vm.BriefTrackRecordSummary);
                Assert.Contains(VisualChildren<System.Windows.Controls.TextBlock>(content), b => b.Text == vm.BriefTrackRecordSummary);
                Assert.Contains(VisualChildren<System.Windows.Controls.TextBlock>(content), b => b.Text == vm.BriefTrackRecordBreakdown);
            }
            if (i == 5)
            {
                Assert.Contains(VisualChildren<System.Windows.Controls.TextBlock>(content), b => b.Text == "2 wrong · 0 right");
                Assert.Contains(VisualChildren<System.Windows.Controls.TextBlock>(content), b => b.Text == "⚑ Review claim"
                    && b.Visibility == Visibility.Visible && b.Foreground is SolidColorBrush brush && brush.Color == Colors.Red);
                var row = vm.KnowledgeClaims[0];
                var dialog = new NoteDialog("Retire claim", "Reason", 200, required: true, initialNote: row.RetirementReason);
                Assert.Equal("Cited by 2 brief recommendations scored wrong: decisions 105, 106",
                    Assert.IsType<System.Windows.Controls.TextBox>(dialog.FindName("NoteBox")).Text);
                dialog.Close();
                claims.Verify(s => s.RetireAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            }
            if (i == 7)
            {
                Assert.Contains(VisualChildren<System.Windows.Controls.TextBlock>(content), b => b.Text == "Call: Pending");
                var day = new Kor.Operations.Converters.BdDayConverter().Convert(DirectorScoringFixture.ReviewAt, typeof(string), null!, System.Globalization.CultureInfo.CurrentCulture);
                // A TextBlock composed of <Run>s leaves .Text empty; the words are in its Inlines.
                static string Shown(System.Windows.Controls.TextBlock b) =>
                    b.Inlines.Count == 0 ? b.Text : string.Concat(b.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text));
                var reviewTexts = VisualChildren<System.Windows.Controls.TextBlock>(content).Where(b => Shown(b).StartsWith(", review", StringComparison.Ordinal))
                    .Select(b => $"[{Shown(b)}] {b.Visibility}").ToList();
                Assert.True(reviewTexts.Contains($"[, review {day}] Visible"),
                    $"EXPECTED a visible ', review {day}'; ACTUAL: " + string.Join(" | ", reviewTexts));
                var callTexts = VisualChildren<System.Windows.Controls.TextBlock>(content).Select(b => b.Text).Where(t => t.StartsWith("Call:", StringComparison.Ordinal)).ToList();
                Assert.True(callTexts.Contains("Call: Right"), "EXPECTED a rendered 'Call: Right'; ACTUAL call texts: " + string.Join(" | ", callTexts));
            }
        }
        market.BriefStore.Verify(s => s.GetLatestAsync(It.IsAny<CancellationToken>()), Times.Once);
        ledger.Verify(s => s.GetBriefTrackRecordAsync(It.IsAny<CancellationToken>()), Times.Once);
        claims.Verify(s => s.ListClaimVerdictsAsync(It.IsAny<CancellationToken>()), Times.Once);
        director.Verify(s => s.ListPendingBidsAsync(It.IsAny<CancellationToken>()), Times.Once);
        director.Verify(s => s.CountRelationshipPursuitsAsync(It.IsAny<CancellationToken>()), Times.Once);
        return written + RenderPendingBids(dir) + RenderRhythm(dir);
    }

    /// <summary>Version 2's four first tabs on an illustrative rhythm store: the prepared hour,
    /// the action register (one overdue, one closed and filtered out), two meetings and three references.</summary>
    private static int RenderRhythm(string dir)
    {
        var now = new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.FromHours(-7));
        const string hour = """
        {
          "forDate": "2026-10-16", "kind": "BD", "title": "Your BD hour, Friday 16 October",
          "lede": "Illustrative: a week since the vote. Keep the promises from the call.",
          "plan": [
            { "status": "off", "label": "Owed by you", "text": "The recap has not gone out." },
            { "status": "on", "label": "Clean start", "text": "Stale pursuits archived." }
          ],
          "callout": "Illustrative callout: the quiet months are for relationships.",
          "moves": [
            { "rank": 1, "title": "Send the recap", "minutes": 15, "whyNow": ["Promised at the close of the call."], "goals": [2],
              "cards": [5, 7],
              "principle": { "quote": "Illustrative quote.", "cite": "Illustrative book, ch. 1" },
              "drafts": [ { "to": "Ian", "subject": "Recap", "body": "Illustrative draft body." },
                { "to": "omar@korstructural.com", "subject": "Your pursuits this week: 1 urgent, 2 to move",
                  "body": "Hi Omar,\n\n3 of your pursuits need you this week; the first 1 is urgent.\n\n1. URGENT · Example tower (Example Developments)\nDo: Call Pat Example and ask whether the rezoning hearing moved.\nBy: Wed 21 Oct\nWhy: Your next step was due on 30 Sep.\n\n2. Example midrise (Example Homes)\nDo: Send the Example Homes team the Langford Central sheet.\nBy: Fri 23 Oct\nWhy: Never touched since May; KOR has done 4 jobs for them.\n\n3. Example Architects\nDo: Ask for 20 minutes on their Kelowna studio's pipeline.\nBy: Fri 23 Oct\nWhy: They opened a Kelowna studio in May.\n\nReply with one line per number: done, moved to <date>, or dropped and why.\nIan",
                  "touchstones": ["501:9:2026-09-30", "502:7:2026-05-30", "503:8:2026-09-01"] },
                { "to": "jim@korstructural.com", "subject": "Your pursuits this week: 1 to move",
                  "body": "Hi Jim,\n\n1 of your pursuits needs you this week.\n\n1. Example school (Example District)\nDo: Ask for the debrief.\nBy: Fri 23 Oct\nWhy: Submitted six weeks ago with no decision.\n\nReply with one line per number: done, moved to <date>, or dropped and why.\nIan",
                  "touchstones": ["601:6:2026-09-04"] } ],
              "steps": ["Read it once", "Send it to yourself first"],
              "links": [ { "label": "Open the actions", "kind": "director", "id": 2 } ] },
            { "rank": 2, "title": "Call Example Client", "minutes": 20, "whyNow": ["Illustrative reason."],
              "drafts": [], "steps": [], "links": [ { "label": "Example Client", "kind": "org", "id": 141 } ] }
          ],
          "decisions": ["Illustrative decision."], "meetings": ["Illustrative meeting note."],
          "sources": "Illustrative fixture.", "notCovered": "Everything real."
        }
        """;
        var store = new Mock<IBdRhythmStore>();
        store.Setup(s => s.GetHourAsync("BD", DateOnly.FromDateTime(now.Date), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectorHourRow(1, DateOnly.FromDateTime(now.Date), "BD", hour, "claude", "ilalonde@korstructural.com", now, null));
        BdActionRow Action(long id, string owner, string name, string text, int dueDays, string status) =>
            new(id, owner, name, text, "Illustrative why", 15, now.AddDays(dueDays), status, status == "Open" ? null : now, null, null,
                1, "BD Updates", now.AddDays(-7), "Vancouver", "9 Oct BD call, 12:30", null, null);
        store.Setup(s => s.ListActionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[]
        {
            Action(1, "ilalonde@korstructural.com", "Ian Lalonde", "Send the recap", -2, "Open"),
            Action(2, "omar@korstructural.com", "Omar Example", "List five Vancouver targets", 3, "Open"),
            Action(3, "jim@korstructural.com", "Jim Example", "Book the San Diego trip", -5, "Done"),
        });
        store.Setup(s => s.ListMeetingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[]
        {
            new BdMeetingRow(1, "AllHands", "BD Updates", null, null, now.AddDays(-7), 60, "Held", "Team", null, "Illustrative recap.", null),
            new BdMeetingRow(2, "OneToOne", "Omar one-to-one", "omar@korstructural.com", "Omar Example", now.AddDays(4), 20, "Planned", null, "Illustrative agenda.", null, null),
        });
        store.Setup(s => s.ListLeadsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[]
        {
            new BdLeadRow("ilalonde@korstructural.com", "Ian Lalonde", "Manager", null, 10),
            new BdLeadRow("omar@korstructural.com", "Omar Example", "Lead", "Vancouver/LowerMainland", 20),
            new BdLeadRow("jim@korstructural.com", "Jim Example", "Lead", "USA", 60),
        });
        store.Setup(s => s.ListMoveResultsAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new HourMoveResult(2, "NotToday", null, now) });
        store.Setup(s => s.ListReferencesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[]
        {
            new BdReferenceRow(1, "Fraser Health", "Example school", "Architect", "Example Architects", "Pat Example", "604-555-0100", null, null, null, now.AddDays(-1), "ilalonde@korstructural.com", "Confirmed", null),
            new BdReferenceRow(2, "Fraser Health", "Example clinic", "Owner", "Example Owner", null, null, null, null, null, null, null, "NotCalled", null),
            new BdReferenceRow(3, "Fraser Health", "Example hall", "Contractor", null, null, null, null, null, null, null, null, "NotCalled", null),
        });
        var rhythm = new BdRhythmViewModel(store.Object, () => now);
        rhythm.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        rhythm.SetPlanGoals(new Dictionary<int, string> { [2] = "No live pursuit untouched for 14 days or more" });
        Assert.True(rhythm.HasHour, "EXPECTED the illustrative hour to parse; ACTUAL: " + rhythm.HourMessage);
        // Jim's roll-up has been opened; Omar's has not.
        rhythm.SetCoveredTouchstones(new[] { "601:6:2026-09-04" });
        var rollups = rhythm.Moves[0].Drafts.Where(d => d.IsRollup).ToArray();
        Assert.Equal(new[] { "   3 pursuits · not opened yet", "   1 pursuit" }, rollups.Select(d => d.RollupText));
        Assert.Equal(new[] { "", "   ✓ Opened, covered this week" }, rollups.Select(d => d.OpenedText));
        Assert.Equal(new[] { "Roll-up to Omar: ", "Roll-up to Jim: " }, rollups.Select(d => d.KindText));
        File.WriteAllText(Path.Combine(dir, "director-email.html"), "<!doctype html><meta charset=\"utf-8\"><title>Email</title>"
            + DirectorHourEmail.Render(rhythm.Hour!, 1));
        Assert.Equal(2, rhythm.Actions.Count); // Open filter: the closed action is not shown
        Assert.Equal(2, rhythm.Meetings.Count);
        Assert.Equal(3, rhythm.References.Count);

        Assert.Equal(new[] { "Ian Lalonde", "Omar Example", "Jim Example" }, rhythm.PeopleCards.Select(p => p.Name));
        Assert.Equal("1 of 2 moves done · about 15 minutes left", rhythm.MovesProgressText);
        Assert.Equal(new long[] { 1 }, rhythm.MyActions.Select(a => a.Row.Id));
        Assert.Equal("2 days late", rhythm.MyActions[0].DueText);
        Assert.Equal("Omar Example", rhythm.SelectedPerson?.Name); // the first lead, not the manager
        Assert.StartsWith("One-to-one: Omar Example (20 minutes)", rhythm.PersonAgenda, StringComparison.Ordinal);

        var pending = new DirectorPendingBidsFixture();
        using var vm = pending.Create();
        vm.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        var doctrineStore = new Mock<IBdDoctrineStore>();
        DoctrineCardRow Card(long id, string status, string title, string plain) => new(id, "BD", title, plain, "situation:going-cold, moment:pursuit",
            "A relationship that has gone quiet is rescued by a useful reason to call, not by checking in.", "No touch in 14 days on a live pursuit.",
            "Send one piece of their world they have not seen (a permit, a hire, a neighbour's project) and ask one question about it.",
            "You are writing \"just checking in\".", "Fox, Rain Making, ch. 7; Maister, The Trusted Advisor, ch. 9", null, 1, null, status, now, null, null);
        doctrineStore.Setup(s => s.ListCardsAsync("BD", It.IsAny<CancellationToken>())).ReturnsAsync(new[]
        {
            Card(11, "proposed", "Rescue a relationship that has gone quiet", "Illustrative: when a pursuit goes 14 days without a touch, Today will suggest a reason to call instead of a check-in."),
            Card(12, "proposed", "Price on the value of certainty", "Illustrative: when KOR's fee is far below the field, the doctrine says explain the scope gap before cutting the price."),
            Card(5, "accepted", "Lead the one-to-one with their list", "Illustrative accepted card."),
            Card(6, "accepted", "Give the buyer a useful reason to call", "Illustrative recorded card."),
            Card(7, "retired", "Retired recap habit", "Illustrative retired citation."),
        });
        doctrineStore.Setup(s => s.ListCardsAsync("Operations", It.IsAny<CancellationToken>())).ReturnsAsync(new[]
        {
            Card(101, "accepted", "Make working capital visible", "Illustrative Operations doctrine.") with { Role = "Operations" },
            Card(102, "proposed", "Review the billing queue", "Illustrative Operations proposal.") with { Role = "Operations" },
        });
        doctrineStore.Setup(s => s.ListCardsAsync("IT", It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<DoctrineCardRow>());
        doctrineStore.Setup(s => s.ListRecordsAsync("Operations", It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<DoctrineCardRecord>());
        doctrineStore.Setup(s => s.ListRunsAsync("Operations", It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BdPromptRunRow>());
        doctrineStore.Setup(s => s.ListRecordsAsync("BD", It.IsAny<CancellationToken>())).ReturnsAsync(new[]
        {
            new DoctrineCardRecord(5, 3, 0, 0, 3, 0, 0, 0, 0, new DateOnly(2026, 10, 16), new[] { "Skipped" }),
            new DoctrineCardRecord(6, 6, 4, 1, 1, 0, 2, 1, 0, new DateOnly(2026, 10, 15), Array.Empty<string>()),
        });
        doctrineStore.Setup(s => s.ListRunsAsync("BD", It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new[]
        {
            new BdPromptRunRow(3, "ask", "BD", "Northland Revelstoke", "How do I restart Northland?", "ian", now.AddHours(-1), now.AddMinutes(-40), "answered",
                "Illustrative answer: call John Bryson first; the relationship is his. Bring the Cabot Revelstoke news as the reason.", 1),
            new BdPromptRunRow(4, "synthesis", "BD", "the BD doctrine", null, "ian", now.AddMinutes(-5), null, null, null, 0),
        });
        var doctrine = new BdDoctrineViewModel(doctrineStore.Object, () => now);
        doctrine.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert.Equal("2 accepted · 2 waiting for you · 1 question(s) still with Claude", doctrine.Summary);
        Assert.Single(doctrine.NeedsLook);

        var window = new BdDirectorWindow(vm, Mock.Of<IProposalStaffStore>(), rhythm, doctrine);
        ((System.Windows.Controls.RadioButton)window.FindName("NavLibrary")).IsChecked = true; // v3: the tabbed screens live in Library
        var tabs = Assert.IsType<System.Windows.Controls.TabControl>(window.FindName("Tabs"));
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        content.DataContext = vm;
        content.Resources.MergedDictionaries.Add(window.Resources);
        var files = new[] { "director-today.png", "director-people.png", "director-pursuits.png", "director-doctrine.png", "director-actions.png", "director-meetings.png", "director-references.png" };
        var written = 0;
        for (var i = 0; i < files.Length; i++)
        {
            if (i < 4) tabs.SelectedIndex = i; else SelectData(tabs, i - 4);
            content.UpdateLayout();
            written += RenderView(content, new Size(1680, i is 0 or 3 ? 1500 : 980), Path.Combine(dir, files[i]));
            if (i == 1)
            {
                Assert.Equal(3, VisualChildren<System.Windows.Controls.ListBox>(content).Single(l => l.Name == "PeopleCardsList").Items.Count);
                Assert.Contains(VisualChildren<System.Windows.Controls.TextBlock>(content), b => b.Text == "Next one-to-one Tue 20 Oct");
            }
            if (i == 2)
            {
                var list = VisualChildren<System.Windows.Controls.ListBox>(content).Single(l => l.Name == "PursuitLinesList");
                Assert.Equal(vm.PursuitLines.Count, list.Items.Count);
                Assert.True(vm.PursuitLines.Count > 0, "EXPECTED the pending-bids fixture's live pursuits on the Pursuits screen");
            }
            if (i == 0)
            {
                Assert.Contains(VisualChildren<System.Windows.Controls.TextBlock>(content), b => b.Text == "Your BD hour, Friday 16 October");
                Assert.Contains(VisualChildren<System.Windows.Controls.TextBlock>(content),
                    b => b.Text == "Serves: No live pursuit untouched for 14 days or more" && b.Visibility == Visibility.Visible);
                Assert.Contains(VisualChildren<System.Windows.Controls.Button>(content), b => Equals(b.Content, "Open the actions"));
                // Only move 1 carries a principle; move 2's quote box must not draw empty.
                var quotes = VisualChildren<System.Windows.Controls.Border>(content)
                    .Count(b => b.Visibility == Visibility.Visible && b.Background is SolidColorBrush q && q.Color == (Color)ColorConverter.ConvertFromString("#E6EDF3"));
                Assert.True(quotes == 1, "EXPECTED one visible quote box; ACTUAL " + quotes);
                // The moves use the page, not the width of their longest line.
                var title = VisualChildren<System.Windows.Controls.TextBlock>(content).First(b => b.Text == "Your BD hour, Friday 16 October");
                var column = (FrameworkElement)VisualTreeHelper.GetParent(title);
                Assert.True(column.ActualWidth >= 880, "EXPECTED the moves to fill their 900 cap; ACTUAL width " + column.ActualWidth);
                // Move 2 was answered "Not today": its card folds to its title and says so.
                Assert.Contains(VisualChildren<System.Windows.Controls.TextBlock>(content), b => b.Text == "Not today");
                Assert.Contains(VisualChildren<System.Windows.Controls.TextBlock>(content), b => b.Text == "From the doctrine: Lead the one-to-one with their list");
                Assert.Contains(VisualChildren<System.Windows.Controls.TextBlock>(content), b => b.Text == "retired");
                Assert.Contains(VisualChildren<System.Windows.Controls.TextBlock>(content), b => b.Text == "From the doctrine: Retired recap habit"
                    && b.TextDecorations.Any(d => d.Location == TextDecorationLocation.Strikethrough));
                var citation = VisualChildren<System.Windows.Controls.Button>(content).Single(b => Equals(b.Tag, 5L));
                citation.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Assert.Same(window.FindName("DoctrineTab"), tabs.SelectedItem);
                Assert.Equal(5L, doctrine.FocusedCard!.Id);
                doctrine.FocusCard(-1);
                tabs.SelectedIndex = 0;
            }
            if (i == 4)
                Assert.Equal(2, VisualChildren<System.Windows.Controls.DataGrid>(content).Single(g => g.Name == "ActionsGrid").Items.Count);
            if (i == 3)
            {
                Assert.Equal(2, VisualChildren<System.Windows.Controls.ItemsControl>(content).Single(c => c.Name == "ProposedCardsList").Items.Count);
                Assert.Equal(2, VisualChildren<System.Windows.Controls.ItemsControl>(content).Single(c => c.Name == "RunsList").Items.Count);
                Assert.Contains(VisualChildren<System.Windows.Controls.Button>(content), b => Equals(b.Content, "Synthesize from the books"));
                Assert.Single(VisualChildren<System.Windows.Controls.ItemsControl>(content).Single(c => c.Name == "FlaggedCardsList").Items);
                Assert.Contains(VisualChildren<System.Windows.Controls.TextBlock>(content), b => b.Text == "Needs a look");
                Assert.Contains(VisualChildren<System.Windows.Controls.TextBlock>(content), b => b.Text == "Skipped the last 3 times it came up");
                Assert.Contains(VisualChildren<System.Windows.Controls.TextBlock>(content), b => b.Text == "Suggested 6 · done 4 · not today 1 · not doing 1 · 2 pursuits advanced");
                Assert.Contains(VisualChildren<System.Windows.Controls.Button>(content), b => Equals(b.Content, "Retire"));
                Assert.Contains(VisualChildren<System.Windows.Controls.Button>(content), b => Equals(b.Content, "Ask Claude to amend"));
            }
        }
        var morning = MorningBriefTests.FixtureHour();
        store.Setup(s => s.GetHourAsync("Morning", DateOnly.FromDateTime(now.Date), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectorHourRow(9, DateOnly.FromDateTime(now.Date), "Morning",
                System.Text.Json.JsonSerializer.Serialize(morning, DirectorHourFile.JsonOptions), "session", "Ian", now, null));
        store.Setup(s => s.ListMoveResultsAsync(9, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<HourMoveResult>());
        rhythm.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        tabs.SelectedIndex = 0;
        content.UpdateLayout();
        written += RenderView(content, new Size(1680, 1900), Path.Combine(dir, "director-today-morning.png"));
        Assert.Equal("0 of 3 moves done · about 60 minutes left", rhythm.MovesProgressText);
        foreach (var role in new[] { "BD", "COO", "IT" })
        {
            Assert.Contains(VisualChildren<System.Windows.Controls.TextBlock>(content), b => b.Text == role);
            // The decision line is two Runs (a bold label, then the text), so TextBlock.Text is empty; read the Runs.
            Assert.Contains(VisualChildren<System.Windows.Controls.TextBlock>(content),
                b => string.Concat(b.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text)) == "Decision: " + role + " decision");
        }
        Assert.Contains(VisualChildren<System.Windows.Controls.TextBlock>(content), b => b.Text == "From the doctrine: Make working capital visible");
        Assert.Equal(3, VisualChildren<System.Windows.Controls.Button>(content).Count(b => Equals(b.Content, "Done")));

        // A tab's content is only templated once it is laid out in a host, so render the Doctrine tab
        // before looking for its role selector; then render it again showing the Operations doctrine.
        tabs.SelectedIndex = 3;
        RenderView(content, new Size(1680, 1500), Path.Combine(dir, "director-doctrine.png"));
        var selector = VisualChildren<System.Windows.Controls.ComboBox>(content).Single(c => c.Name == "DoctrineRoleSelector");
        selector.SelectedItem = "Operations"; // mocked reads complete synchronously in the selection handler
        written += RenderView(content, new Size(1680, 1500), Path.Combine(dir, "director-doctrine-operations.png"));
        Assert.Equal("Operations", doctrine.Role);
        Assert.Equal(102L, Assert.Single(doctrine.Proposed).Id);
        Assert.Equal(101L, Assert.Single(doctrine.Accepted).Id);
        return written;
    }

    /// <summary>Today, People, Pursuits and Doctrine come first; every other view sits inside Data.</summary>
    private static void SelectData(System.Windows.Controls.TabControl tabs, int index)
    {
        var data = tabs.Items.OfType<System.Windows.Controls.TabItem>().Single(t => Equals(t.Header, "Data"));
        tabs.SelectedItem = data;
        Assert.IsType<System.Windows.Controls.TabControl>(data.Content).SelectedIndex = index;
    }

    private static IEnumerable<T> VisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in VisualChildren<T>(child)) yield return nested;
        }
    }

    private static int RenderMyDay(string dir)
    {
        var fixture = new DirectorMyDayFixture();
        fixture.Items.RemoveAll(i => i.EngagementId is 2 or 4);
        using var vm = fixture.Create();
        vm.LoadMyDayAsync(CancellationToken.None, fixture.Now).GetAwaiter().GetResult();
        var window = new BdDirectorWindow(vm, Mock.Of<IProposalStaffStore>());
        ((System.Windows.Controls.RadioButton)window.FindName("NavLibrary")).IsChecked = true; // v3: the tabbed screens live in Library
        var tabs = Assert.IsType<System.Windows.Controls.TabControl>(window.FindName("Tabs"));
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        content.DataContext = vm;
        content.Resources.MergedDictionaries.Add(window.Resources);
        SelectData(tabs, 12);
        var written = RenderView(content, new Size(1680, 1280), Path.Combine(dir, "director-myday.png"));
        Assert.Equal(new[] { CallSheetKind.Pursuit, CallSheetKind.Ask, CallSheetKind.Account }, vm.MyDay.Select(r => r.Item.Kind));
        Assert.Contains(VisualChildren<System.Windows.Controls.TextBlock>(content), b => b.Text == "Known by Priya Engineer (3 jobs)");
        Assert.Contains(VisualChildren<System.Windows.Controls.Button>(content), b => Equals(b.Content, "Start tracking") && b.Visibility == Visibility.Visible);
        static string Shown(System.Windows.Controls.TextBlock b) => b.Inlines.Count == 0 ? b.Text
            : string.Concat(b.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text));
        var signalDay = BdDateDisplay.Day(fixture.Now.AddDays(-45));
        Assert.Contains(VisualChildren<System.Windows.Controls.TextBlock>(content), b => Shown(b) == "Last signal " + signalDay + ": Illustrative old signal");
        Assert.Contains(VisualChildren<System.Windows.Controls.TextBlock>(content), b => Shown(b).StartsWith("Next action overdue since ", StringComparison.Ordinal));
        Assert.Contains(VisualChildren<System.Windows.Controls.TextBlock>(content), b => Shown(b).StartsWith("Ask due ", StringComparison.Ordinal));
        return written;
    }

    private static int RenderPendingBids(string dir)
    {
        // A coherent six-pursuit fixture for this tab; the older 101-pursuit
        // fixture above remains the reference for the other eight screenshots.
        var fixture = new DirectorPendingBidsFixture();
        using var vm = fixture.Create();
        vm.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert.Equal(6, vm.Live);
        Assert.Equal(2, vm.PeopleCount);
        var window = new BdDirectorWindow(vm, Mock.Of<IProposalStaffStore>());
        ((System.Windows.Controls.RadioButton)window.FindName("NavLibrary")).IsChecked = true; // v3: the tabbed screens live in Library
        var tabs = Assert.IsType<System.Windows.Controls.TabControl>(window.FindName("Tabs"));
        var grid = Assert.IsType<System.Windows.Controls.DataGrid>(window.FindName("PendingBidsGrid"));
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        content.DataContext = vm;
        content.Resources.MergedDictionaries.Add(window.Resources);
        for (var i = 0; i < 12; i++) SelectData(tabs, i);
        var written = RenderView(content, new Size(1680, 980), Path.Combine(dir, "director-pending.png"));
        Assert.Equal(3, vm.PendingBids.Count);
        Assert.Equal(3, vm.RelationshipPursuits);
        Assert.Equal(2, vm.PendingBids.Count(b => b.IsPending));
        Assert.Single(vm.PendingBids, b => b.IsUrgent);
        Assert.DoesNotContain(vm.PendingBids, b => b.Bid.EngagementId >= 10);
        Assert.Contains("3 relationship pursuits not shown", vm.PendingBidsSummary, StringComparison.Ordinal);
        var redRows = 0;
        foreach (var bid in vm.PendingBids)
        {
            var renderedRow = Assert.IsType<System.Windows.Controls.DataGridRow>(grid.ItemContainerGenerator.ContainerFromItem(bid));
            var red = renderedRow.Foreground is SolidColorBrush brush && brush.Color == Colors.Firebrick;
            Assert.Equal(bid.IsUrgent, red);
            if (red) redRows++;
            var cell = Assert.IsType<System.Windows.Controls.TextBlock>(grid.Columns[7].GetCellContent(bid));
            Assert.Equal(bid.SubmittedAtUtc is { } at ? BdDateDisplay.DayAndTime(at) : "", cell.Text);
        }
        Assert.Equal(1, redRows);
        fixture.VerifyReads(Times.Once());
        return written;
    }

    private static IReadOnlyList<BdDirectorAccount> DirectorAccounts()
    {
        var recent = new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
        var september = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var old = new DateTimeOffset(2024, 11, 8, 0, 0, 0, TimeSpan.Zero);
        // Client identifiers, last-project dates and open-ask counts are illustrative.
        BdDirectorAccount Row(long id, string name, string kind, int count, int live = 0, DateTimeOffset? touch = null)
            => new(id, name, kind, "fixture-" + id, count, true, new DateTime(2026, 8, 1), live, touch, id == 141 ? 1 : 0);
        return new[]
        {
            Row(141, "Northland Properties Ltd.", "KorClient", 378),
            Row(200, "Larco Investments Ltd.", "KorClient", 185),
            Row(49, "Cressey Development Group", "KorClient", 117),
            Row(28, "Polygon Development Ltd.", "KorClient", 114),
            Row(157, "Reliance Properties Ltd.", "KorClient", 82),
            Row(69677, "Bosa Development", "Developer", 80, 1, recent),
            Row(12, "Wesgroup Properties Ltd.", "KorClient", 80),
            Row(207, "Anthem Properties Group Ltd.", "KorClient", 72, 1, recent),
            Row(218, "Bucci Developments Ltd.", "KorClient", 66),
            Row(69802, "Appia Development", "Developer", 63),
            Row(20962, "Mondiale Development", "Developer", 57),
            Row(69730, "Solterra Development Corp.", "Developer", 56, 1, september),
            Row(29, "Intracorp Projects Ltd.", "KorClient", 46),
            Row(38943, "Bosa Properties", "Developer", 44, 1, old),
            Row(125, "Ledingham McAllister Properties Ltd.", "KorClient", 40),
        };
    }

    // ---- fixtures: shaped like the 2026-10-14 production rows, including the retired duplicate ----

    private static List<Opportunity> Opportunities() => new()
    {
        new Opportunity
        {
            Id = 39408, OpportunityKey = "BCBID-233225",
            Name = "Development Consulting Services - Tumbler Ridge Secondary School Replacement",
            BuyerName = "Ministry of Infrastructure", ProjectCity = "Tumbler Ridge", ProjectProvince = "BC",
            Status = OpportunityStatus.New, SubmissionDeadlineUtc = Now.AddDays(5).AddHours(2),
            RfpReleaseDate = DateOnly.FromDateTime(Now.AddDays(-23).DateTime),
            RelevanceTier = RelevanceTier.Low, RelevanceScore = 8m, IsPrimeConsultantRfp = true, PrimeProjectSector = "school",
            CreatedAtUtc = Now.AddDays(-23),
        },
        new Opportunity
        {
            Id = 39516, OpportunityKey = "BCBID-234361",
            Name = "Development Consulting Services - Tumbler Ridge Secondary School Replacement",
            BuyerName = "Ministry of Infrastructure", ProjectProvince = "BC",
            Status = OpportunityStatus.New, SubmissionDeadlineUtc = Now.AddDays(5).AddHours(2),
            RelevanceTier = RelevanceTier.Low, RelevanceScore = 8m, IsPrimeConsultantRfp = true, PrimeProjectSector = "school",
            DismissedAtUtc = Now.AddDays(-6), DismissedBy = "BdOpportunityDedup",
            DismissedReason = "Duplicate of BCBID-233225, the record kept. Its postings and history were merged there.",
            CreatedAtUtc = Now.AddDays(-20),
        },
        new Opportunity
        {
            Id = 39410, OpportunityKey = "BONFIRE-FHA-7731",
            Name = "Prime Consultant Services: RCH Mammography Replacement",
            BuyerName = "Fraser Health Authority", ProjectCity = "New Westminster", ProjectProvince = "BC",
            Status = OpportunityStatus.Pursuing, OwnerStaffId = "ilalonde@korstructural.com",
            SubmissionDeadlineUtc = Now.AddDays(16).AddHours(3), EstimatedValue = 1_960_000m,
            RelevanceTier = RelevanceTier.High, RelevanceScore = 72m, IsPrimeConsultantRfp = true, PrimeProjectSector = "healthcare",
            CreatedAtUtc = Now.AddDays(-4),
        },
        new Opportunity
        {
            Id = 39411, OpportunityKey = "APC-2026-1188",
            Name = "Request for Supplier Qualifications for Engineering Prime Consultant Services",
            BuyerName = "Government of Alberta (APC)", ProjectCity = "Edmonton", ProjectProvince = "AB",
            Status = OpportunityStatus.New, SubmissionDeadlineUtc = Now.AddDays(26).AddHours(1),
            RelevanceTier = RelevanceTier.Medium, RelevanceScore = 41m, IsPrimeConsultantRfp = true, PrimeProjectSector = "civic",
            CreatedAtUtc = Now.AddDays(-1),
        },
        new Opportunity
        {
            Id = 39412, OpportunityKey = "APC-2026-1201",
            Name = "Whitecourt Project – Prime Consultant Services",
            BuyerName = "Government of Alberta (APC)", ProjectProvince = "AB",
            Status = OpportunityStatus.New, SubmissionDeadlineUtc = null,
            RelevanceTier = RelevanceTier.Medium, RelevanceScore = 35m, IsPrimeConsultantRfp = true, PrimeProjectSector = "institutional",
            CreatedAtUtc = Now.AddHours(-20),
        },
        new Opportunity
        {
            Id = 39413, OpportunityKey = "BCBID-234895",
            Name = "Design Consulting Services - Whitehorse General Hospital Project Early Works",
            BuyerName = "Infrastructure BC Inc.", ProjectCity = "Whitehorse", ProjectProvince = "YT",
            Status = OpportunityStatus.New, SubmissionDeadlineUtc = Now.AddDays(21),
            RelevanceTier = RelevanceTier.High, RelevanceScore = 66m, IsPrimeConsultantRfp = true, PrimeProjectSector = "healthcare",
            CreatedAtUtc = Now.AddDays(-2),
        },
        new Opportunity
        {
            Id = 39414, OpportunityKey = "APC-2026-1210",
            Name = "PRIME CONSULTANT SERVICES for Project 1 - CALGARY - MODERNIZATION AND ADDITION OF 10 TO 12 SCHOOL IN VISTA HEIGHTS and/or Project 2 – CALGARY – MODERNIZATION AND ADDITION OF 10 TO 12 SCHOOL IN RADISSON HEIGHTS",
            BuyerName = "Government of Alberta (APC)", ProjectCity = "Calgary", ProjectProvince = "AB",
            Status = OpportunityStatus.New, SubmissionDeadlineUtc = Now.AddHours(6),
            RelevanceTier = RelevanceTier.Low, RelevanceScore = 12m, IsPrimeConsultantRfp = true, PrimeProjectSector = "school",
            CreatedAtUtc = Now.AddDays(-12),
        },
        new Opportunity
        {
            Id = 39415, OpportunityKey = "BCBID-234526",
            Name = "Prime Consultant - SE02 Second-Floor Patio Renovation",
            BuyerName = "BC Institute of Technology", ProjectCity = "Burnaby", ProjectProvince = "BC",
            Status = OpportunityStatus.New, SubmissionDeadlineUtc = Now.AddDays(14).AddHours(-2),
            RelevanceTier = RelevanceTier.Medium, RelevanceScore = 38m, IsPrimeConsultantRfp = true, PrimeProjectSector = "education",
            CreatedAtUtc = Now.AddDays(-3),
        },
    };

    private static List<CrmEngagement> Engagements() => new()
    {
        new CrmEngagement
        {
            Id = 501, OpportunityId = 39410, Stage = CrmEngagementStage.Drafting, OwnerStaffId = "ilalonde@korstructural.com",
            Region = "Lower Mainland", ProposedFee = 148_000m, OpenedAtUtc = Now.AddDays(-60), UpdatedAtUtc = Now.AddDays(-35),
            NextActionDueUtc = Now.AddDays(3), NextActionNote = "Call Fraser Health PM about the mechanical sub.",
            Notes = "Sub to the architect; we have the seat if they get the prime.",
        },
        new CrmEngagement
        {
            Id = 502, OpportunityId = 39411, Stage = CrmEngagementStage.Submitted, OwnerStaffId = "rwong@korstructural.com",
            Region = "Alberta", ProposedFee = 92_500m, OpenedAtUtc = Now.AddDays(-40), UpdatedAtUtc = Now.AddDays(-12),
        },
        new CrmEngagement
        {
            Id = 503, OpportunityId = 39412, Stage = CrmEngagementStage.Drafting, OwnerStaffId = "ilalonde@korstructural.com",
            Region = "Alberta", OpenedAtUtc = Now.AddDays(-8), UpdatedAtUtc = Now.AddDays(-2),
        },
        new CrmEngagement
        {
            Id = 504, OpportunityId = null, Stage = CrmEngagementStage.Drafting, OwnerStaffId = "jbryson@korstructural.com",
            Region = "Vancouver Island", OpenedAtUtc = Now.AddDays(-15), UpdatedAtUtc = Now.AddDays(-15),
            PotentialProjects = "North Langford Secondary School",
            BuyerOrgName = "School District 62 (Sooke)",
        },
        new CrmEngagement
        {
            Id = 505, OpportunityId = 39415, Stage = CrmEngagementStage.Won, OwnerStaffId = "ilalonde@korstructural.com",
            Region = "Lower Mainland", ProposedFee = 41_000m, OpenedAtUtc = Now.AddDays(-90), UpdatedAtUtc = Now.AddDays(-20),
            ClosedAtUtc = Now.AddDays(-20),
        },
    };

    // ---- rendering, verbatim from NetworkOpsWindowsRenderTests ----

    private static void DrainDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    /// <summary>A UserControl as the workspace hosts it: under an AdornerLayer (a Window has one, a bare
    /// control does not), with the placeholders drawn, which otherwise wait for a Loaded that never fires.</summary>
    /// <summary>Record reply on a roll-up, with an owner's answer pasted in: the preview names every pursuit, problems in red.</summary>
    private static int RenderReplyDialog(string dir)
    {
        var draft = new Kor.Opportunities.Data.Crm.HourDraft("omara@korstructural.com", "Your pursuits this week", "Hi Omar,")
        { Touchstones = new[] { "312:9:2026-09-30", "401:7:2026-05-30", "455:5:2026-10-01" } };
        var titles = new Dictionary<long, string> { [312] = "Example tower (Example Developments)", [401] = "Example rental (Example Homes)", [455] = "Example hotel" };
        var dialog = new Kor.Operations.App.BusinessDevelopment.Director.ReplyDialog("Omar",
            Kor.Opportunities.Data.Crm.RollupReply.Items(draft), titles, new DateOnly(2026, 10, 20));
        ((System.Windows.Controls.TextBox)dialog.FindName("ReplyBox")).Text = "Thanks Ian,\n1. done, called Pat\n2. moved to Fri 30 Oct\n3. maybe later";
        var preview = ((System.Windows.Controls.TextBlock)dialog.FindName("PreviewText")).Text;
        Assert.Contains("1. Example tower (Example Developments): done; its due next step is cleared; logged.", preview);
        Assert.Contains("2. Example rental (Example Homes): next step moved to Fri 30 Oct; logged.", preview);
        Assert.Contains("not understood", ((System.Windows.Controls.TextBlock)dialog.FindName("ProblemText")).Text);
        Assert.True(((System.Windows.Controls.Button)dialog.FindName("SaveButton")).IsEnabled);
        var content = (FrameworkElement)dialog.Content;
        dialog.Content = null;
        return RenderView(content, new Size(640, 520), Path.Combine(dir, "director-reply-dialog.png"));
    }

    private static int RenderView(FrameworkElement view, Size size, string path)
    {
        var host = new AdornerDecorator { Child = view };
        try { return Render(host, size, path, afterFirstLayout: () => Placeholder.RefreshAll(view)); }
        finally { host.Child = null; } // A tabbed window's content is rendered again for its next tab.
    }

    private static int Render(FrameworkElement root, Size size, string path, Action? afterFirstLayout = null)
    {
        // A DataGrid sizes its star columns in work it QUEUES on the dispatcher once it knows its
        // viewport; a never-shown view has no running dispatcher, so without pumping the queue those
        // columns render collapsed. Lay out, drain, lay out again.
        for (var pass = 0; pass < 2; pass++)
        {
            root.Measure(size);
            root.Arrange(new Rect(size));
            root.UpdateLayout();
            DrainDispatcher();
            if (pass == 0) afterFirstLayout?.Invoke();
        }

        var target = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        target.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using (var stream = File.Create(path)) encoder.Save(stream);
        Assert.True(new FileInfo(path).Length > 10_000, $"{path} is suspiciously small: the view may have rendered blank.");
        return 1;
    }
}

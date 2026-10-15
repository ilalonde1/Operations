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
using Kor.Operations.App.BusinessDevelopment.Workspace;
using Kor.Operations.App.BusinessDevelopment.Director;
using Kor.Operations.App.Crm;
using Kor.Operations.App.Opportunities;
using Kor.Operations.Controls;
using Kor.Operations.Core.Services;
using Kor.Operations.Graph;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Awards;
using Kor.Opportunities.Data.Crm;
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

                written += RenderDashboard(dir);
                written += RenderRfpWindows(dir);
                written += RenderPursuits(dir);
                written += RenderPursuitHealth(dir);
                written += RenderDirector(dir);
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
        Assert.True(written >= 6, $"EXPECTED at least 6 renders; ACTUAL {written}");
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
            new BdDirectorPerson("omara@korstructural.com", "Omar Alcazar Pastrana", new[] { "Omar", "omara@korstructural.com" }, 25, 5, 19, 25, 0, 0, 0, 3, 0),
            new BdDirectorPerson("Conor", "Conor", new[] { "Conor" }, 22, 5, 16, 22, 0, 0, 0, 3, 0),
            new BdDirectorPerson("Islam", "Islam", new[] { "Islam" }, 20, 5, 15, 20, 0, 0, 0, 3, 0),
            new BdDirectorPerson("Jim", "Jim", new[] { "Jim" }, 18, 5, 12, 18, 0, 0, 0, 8, 0),
            new BdDirectorPerson("John", "John", new[] { "John" }, 9, 3, 6, 9, 0, 0, 0, 7, 0),
            new BdDirectorPerson("Rory", "Rory", new[] { "Rory" }, 4, 1, 3, 4, 0, 0, 0, 0, 0),
            new BdDirectorPerson("jbryson@korstructural.com", "John Bryson", new[] { "jbryson@korstructural.com" }, 2, 0, 2, 2, 0, 0, 0, 1, 2),
            new BdDirectorPerson("ilalonde@korstructural.com", "Ian", new[] { "ilalonde@korstructural.com" }, 1, 0, 1, 1, 0, 0, 0, 0, 6),
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
        var vm = new BdDirectorViewModel(director.Object, pursuits.Object, recordStore.Object, attribution.Object);
        vm.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(vm.Live == 101 && vm.PeopleCount == 8 && vm.Outcomes == "No live pursuit has been decided yet.",
            $"EXPECTED 101 live / 8 people / no decided live pursuits; ACTUAL {vm.Live} / {vm.PeopleCount} / {vm.Outcomes}");

        var window = new BdDirectorWindow(vm, Mock.Of<IProposalStaffStore>());
        var content = (FrameworkElement)window.Content;
        // Render just the content; Loaded never fires and no telemetry/DI/DB runs.
        // Preserve the window's resources and inherited DataContext on detaching.
        window.Content = null;
        content.DataContext = vm;
        content.Resources.MergedDictionaries.Add(window.Resources);
        return RenderView(content, new Size(1680, 980), Path.Combine(dir, "director.png"));
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
    private static int RenderView(FrameworkElement view, Size size, string path)
        => Render(new AdornerDecorator { Child = view }, size, path, afterFirstLayout: () => Placeholder.RefreshAll(view));

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

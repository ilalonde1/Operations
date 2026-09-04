#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Ingestion.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kor.Opportunities.Data.Tests;

/// <summary>
/// WHAT THESE COVER: the four things ArcGIS does differently from every other
/// JSON feed we read, each of which silently produces wrong data rather than an
/// error — the features/attributes envelope, epoch-millisecond dates,
/// exceededTransferLimit paging, and the one-application-many-parcel-rows fan-out
/// that would otherwise file the same project up to nine times. Plus the
/// HTTP-200-with-an-error-body case, the status filter and page-size capping.
///
/// WHAT THEY DO NOT COVER: anything downstream of the provider — the relevance
/// gate, OpportunityKey composition, dedup against existing rows, or whether a
/// given municipality's layer is an APPLICATIONS layer rather than a static
/// zoning overlay. A same-class fault they would NOT catch: a config row pointed
/// at a "Development Permit Area" polygon layer, which returns perfectly valid
/// features and would ingest cleanly as nonsense. That one is caught only by
/// looking at what a source actually delivered — see
/// tools/BdIntegrityCheck's source_everything_filtered_out.
/// </summary>
public sealed class ArcGisFeatureOpportunityProviderTests
{
    // 2025-08-06T00:00:00Z and 2023-03-21T00:00:00Z, the shape Victoria ships.
    private const long Aug2025Ms = 1754438400000L;
    private const long Mar2023Ms = 1679356800000L;

    private static readonly Dictionary<string, string> VictoriaConfig = new(StringComparer.OrdinalIgnoreCase)
    {
        ["arcgis.externalRefField"] = "FOLDER_NUMBER",
        ["arcgis.titleField"] = "SUBJECT",
        ["arcgis.buyerOverride"] = "City of Victoria",
        ["arcgis.typeField"] = "AppType",
        ["arcgis.statusField"] = "STATUS",
        ["arcgis.descriptionField"] = "PURPOSE",
        ["arcgis.postedDateField"] = "CREATED_DATE",
        ["arcgis.addressFields"] = "HOUSE,STREET",
        ["arcgis.requiredStatuses"] = "ACTIVE,ON HOLD",
        ["arcgis.contactNameField"] = "CityContact",
        ["arcgis.contactEmailField"] = "email",
        ["arcgis.contactPhoneField"] = "phone",
        ["arcgis.detailUrlTemplate"] = "https://tender.victoria.ca/webapps/ourcity/prospero/details.aspx?folderNumber={ref}",
        ["arcgis.cityOverride"] = "Victoria",
        ["arcgis.provinceOverride"] = "BC",
    };

    [Fact]
    public async Task ManyParcelRowsCollapseToOneApplicationWithEveryAddress()
    {
        // Victoria's REZ00901 is nine features - one per parcel - with identical
        // attributes. Ingested raw it is nine copies of one tower project.
        var page = Response(
            exceeded: false,
            Row("REZ00901", "846 Broughton Street", "Rezoning", "ACTIVE", "846", "BROUGHTON ST", Aug2025Ms,
                "Three mixed-use towers with residential above commercial uses."),
            Row("REZ00901", "846 Broughton Street", "Rezoning", "ACTIVE", "829", "FORT ST", Aug2025Ms,
                "Three mixed-use towers with residential above commercial uses."),
            Row("DPV00229", "2545 Westbourne Place", "Dev Permit with Variance", "ON HOLD", "2545", "WESTBOURNE PL",
                Mar2023Ms, "Construct a new maintenance building for the Fire Department."));

        var results = await FetchAsync(VictoriaConfig, Meta(2000), page);

        Assert.Equal(2, results.Count);

        var rez = results.Single(r => r.ExternalReference == "REZ00901");
        Assert.Equal("Rezoning — 846 Broughton Street", rez.Title);
        Assert.Equal("846 BROUGHTON ST; 829 FORT ST", rez.Location);
        Assert.Equal("City of Victoria", rez.Buyer);
        Assert.Equal("Victoria", rez.ProjectCity);
        Assert.Equal("BC", rez.ProjectProvince);
        Assert.Equal(
            "https://tender.victoria.ca/webapps/ourcity/prospero/details.aspx?folderNumber=REZ00901",
            rez.Url);
    }

    [Fact]
    public async Task EpochMillisecondDatesBecomeRealDatesAndTheEarliestWins()
    {
        var page = Response(
            exceeded: false,
            Row("REZ00901", "846 Broughton Street", "Rezoning", "ACTIVE", "846", "BROUGHTON ST", Aug2025Ms, "x"),
            Row("REZ00901", "846 Broughton Street", "Rezoning", "ACTIVE", "829", "FORT ST", Mar2023Ms, "x"));

        var results = await FetchAsync(VictoriaConfig, Meta(2000), page);

        var posted = Assert.Single(results).PostedDateUtc;
        Assert.NotNull(posted);
        Assert.Equal(new DateTime(2023, 3, 21, 0, 0, 0, DateTimeKind.Utc), posted!.Value.UtcDateTime);
    }

    [Fact]
    public async Task ANumberThatIsNotATimestampIsRejectedRatherThanBecomingA1970Date()
    {
        // 20260827 read as epoch ms is 1970-01-01. A wrong-but-plausible date is
        // worse than none: it would sort to the top of "newest first".
        var page = Response(
            exceeded: false,
            Row("REZ00001", "Somewhere", "Rezoning", "ACTIVE", "1", "MAIN ST", 20260827L, "x"));

        var results = await FetchAsync(VictoriaConfig, Meta(2000), page);

        Assert.Null(Assert.Single(results).PostedDateUtc);
    }

    [Fact]
    public async Task PagingFollowsExceededTransferLimitAndAdvancesTheOffset()
    {
        var handler = new StubHandler();
        handler.Add("?f=json", Meta(2));
        handler.Add(
            "resultOffset=0",
            Response(
                exceeded: true,
                Row("REZ00001", "One", "Rezoning", "ACTIVE", "1", "MAIN ST", Aug2025Ms, "x"),
                Row("REZ00002", "Two", "Rezoning", "ACTIVE", "2", "MAIN ST", Aug2025Ms, "x")));
        handler.Add(
            "resultOffset=2",
            Response(
                exceeded: false,
                Row("REZ00003", "Three", "Rezoning", "ACTIVE", "3", "MAIN ST", Aug2025Ms, "x")));

        var results = await RunAsync(handler, VictoriaConfig);

        Assert.Equal(3, results.Count);
        Assert.Contains(handler.Requested, u => u.Contains("resultOffset=2", StringComparison.Ordinal));
        Assert.Contains(handler.Requested, u => u.Contains("resultRecordCount=2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PageSizeIsCappedToTheLayersOwnMaxRecordCount()
    {
        var cfg = new Dictionary<string, string>(VictoriaConfig, StringComparer.OrdinalIgnoreCase)
        {
            ["arcgis.pageSize"] = "5000",
        };

        var handler = new StubHandler();
        handler.Add("?f=json", Meta(1000));
        handler.Add("resultOffset=0", Response(exceeded: false));

        await RunAsync(handler, cfg);

        Assert.Contains(handler.Requested, u => u.Contains("resultRecordCount=1000", StringComparison.Ordinal));
        Assert.DoesNotContain(handler.Requested, u => u.Contains("resultRecordCount=5000", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnArcGisErrorBodyReturnedAsHttp200StopsTheReadInsteadOfThrowing()
    {
        var handler = new StubHandler();
        handler.Add("?f=json", Meta(2000));
        handler.Add("resultOffset=0", """{"error":{"code":400,"message":"Unable to complete operation."}}""");

        var results = await RunAsync(handler, VictoriaConfig);

        Assert.Empty(results);
    }

    [Fact]
    public async Task RowsFailingTheStatusFilterOrMissingAnIdOrTitleAreDropped()
    {
        var page = Response(
            exceeded: false,
            Row("REZ00001", "Kept", "Rezoning", "ACTIVE", "1", "MAIN ST", Aug2025Ms, "x"),
            Row("REZ00002", "Withdrawn", "Rezoning", "CANCELLED", "2", "MAIN ST", Aug2025Ms, "x"),
            Row("", "No folder number", "Rezoning", "ACTIVE", "3", "MAIN ST", Aug2025Ms, "x"),
            Row("REZ00004", "", "Rezoning", "ACTIVE", "4", "MAIN ST", Aug2025Ms, "x"));

        var results = await FetchAsync(VictoriaConfig, Meta(2000), page);

        Assert.Equal("REZ00001", Assert.Single(results).ExternalReference);
    }

    [Fact]
    public async Task TheDescriptionCarriesThePurposeBecauseThatIsWhatTheRelevanceGateReads()
    {
        var page = Response(
            exceeded: false,
            Row("DPV00229", "2545 Westbourne Place", "Dev Permit with Variance", "ACTIVE", "2545", "WESTBOURNE PL",
                Mar2023Ms, "Construct a new maintenance building for the Fire Department."));

        var results = await FetchAsync(VictoriaConfig, Meta(2000), page);

        var only = Assert.Single(results);
        Assert.Equal("Construct a new maintenance building for the Fire Department.", only.Description);
        Assert.Equal("2545 WESTBOURNE PL", only.Location);
    }

    [Fact]
    public async Task WhenParcelRowsCarryDIFFERENTDescriptionsBothAreKept()
    {
        // Adversarial review 2026-09-04, verified live: Coquitlam file 22-067
        // (Morningstar Homes) is two rows — phase one and phase two — and
        // first-wins was silently discarding "a townhouse site with 92 units".
        // Collapsing rows must never choose between differing content.
        var cfg = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["arcgis.externalRefField"] = "PROJECT_NUMBER",
            ["arcgis.titleField"] = "ADDRESS",
            ["arcgis.buyerOverride"] = "City of Coquitlam",
            ["arcgis.descriptionField"] = "PROJECT_DESCRIPTION",
            ["arcgis.addressFields"] = "ADDRESS",
        };

        var page = JsonSerializer.Serialize(new
        {
            features = new[]
            {
                new
                {
                    attributes = new
                    {
                        PROJECT_NUMBER = "22-067",
                        ADDRESS = "3409, 3411, 3415, 3421, 3435 Galloway",
                        PROJECT_DESCRIPTION = "Phase one to create 29 single family lots, 5 Duplexes.",
                    },
                },
                new
                {
                    attributes = new
                    {
                        PROJECT_NUMBER = "22-067",
                        ADDRESS = "3421 & 3435 Galloway Ave",
                        PROJECT_DESCRIPTION = "Phase two to create a townhouse site with 92 units.",
                    },
                },
            },
        });

        var results = await FetchAsync(cfg, Meta(2000), page);

        var only = Assert.Single(results);
        Assert.Contains("29 single family lots", only.Description);
        Assert.Contains("townhouse site with 92 units", only.Description);
    }

    [Fact]
    public async Task IdenticalDescriptionsAcrossParcelRowsAreNotRepeated()
    {
        // The other half of the same rule: Victoria's parcel rows are identical,
        // so de-duplicating is what keeps the merged text readable.
        var page = Response(
            exceeded: false,
            Row("REZ00901", "846 Broughton Street", "Rezoning", "ACTIVE", "846", "BROUGHTON ST", Aug2025Ms,
                "Three mixed-use towers."),
            Row("REZ00901", "846 Broughton Street", "Rezoning", "ACTIVE", "829", "FORT ST", Aug2025Ms,
                "Three mixed-use towers."));

        var results = await FetchAsync(VictoriaConfig, Meta(2000), page);

        Assert.Equal("Three mixed-use towers.", Assert.Single(results).Description);
    }

    [Fact]
    public async Task AnApplicantNameIsPushedToTheFrontOfTheDescription()
    {
        // Coquitlam's layer names the party who filed - the developer. It is the
        // most valuable field in these feeds and must not sit only in RawJson.
        var cfg = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["arcgis.externalRefField"] = "PROJECT_NUMBER",
            ["arcgis.titleField"] = "ADDRESS",
            ["arcgis.buyerOverride"] = "City of Coquitlam",
            ["arcgis.descriptionField"] = "PROJECT_DESCRIPTION",
            ["arcgis.applicantField"] = "APPLICANT",
        };

        var page = JsonSerializer.Serialize(new
        {
            features = new[]
            {
                new
                {
                    attributes = new
                    {
                        PROJECT_NUMBER = "18-130",
                        ADDRESS = "269 King St",
                        PROJECT_DESCRIPTION = "To construct a Triplex.",
                        APPLICANT = "Rail House Builders Inc.",
                    },
                },
            },
        });

        var results = await FetchAsync(cfg, Meta(2000), page);

        Assert.Equal(
            "Applicant: Rail House Builders Inc.. To construct a Triplex.",
            Assert.Single(results).Description);
    }

    [Fact]
    public async Task ADescriptionCanBeComposedFromSeveralFields()
    {
        // Maple Ridge splits it: WorkProposed says what is being built,
        // Description says where the file has got to. Both matter.
        var cfg = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["arcgis.externalRefField"] = "ReferenceFile",
            ["arcgis.titleField"] = "Name",
            ["arcgis.buyerOverride"] = "City of Maple Ridge",
            ["arcgis.descriptionFields"] = "WorkProposed,Description",
        };

        var page = JsonSerializer.Serialize(new
        {
            features = new[]
            {
                new
                {
                    attributes = new
                    {
                        ReferenceFile = "2026-088-RZ",
                        Name = "2026-088-RZ - 20596 DEWDNEY TRUNK RD",
                        WorkProposed = "Institutional",
                        Description = "Application is in review stage.",
                    },
                },
            },
        });

        var results = await FetchAsync(cfg, Meta(2000), page);

        Assert.Equal("Institutional Application is in review stage.", Assert.Single(results).Description);
    }

    // ---- harness -------------------------------------------------------

    private static async Task<IReadOnlyList<Core.Ingestion.OpportunityCandidate>> FetchAsync(
        IReadOnlyDictionary<string, string> cfg,
        string metaJson,
        string pageJson)
    {
        var handler = new StubHandler();
        handler.Add("?f=json", metaJson);
        handler.Add("resultOffset=0", pageJson);
        return await RunAsync(handler, cfg);
    }

    private static async Task<IReadOnlyList<Core.Ingestion.OpportunityCandidate>> RunAsync(
        StubHandler handler,
        IReadOnlyDictionary<string, string> cfg)
    {
        using var http = new HttpClient(handler);
        var provider = new ArcGisFeatureOpportunityProvider(
            http,
            NullLogger<ArcGisFeatureOpportunityProvider>.Instance);

        var source = new OpportunitySource
        {
            Id = Guid.NewGuid(),
            Name = "Test_ArcGis",
            SourceType = OpportunitySourceType.ArcGisFeatureService,
            BaseUrl = "https://example.test/server/rest/services/Dev/MapServer/3",
            RequestTimeoutSeconds = 30,
        };

        return await provider.FetchAsync(source, cfg, CancellationToken.None);
    }

    private static string Meta(int maxRecordCount)
        => JsonSerializer.Serialize(new
        {
            name = "Development Applications",
            maxRecordCount,
        });

    private static string Response(bool exceeded, params object[] rows)
        => JsonSerializer.Serialize(new
        {
            objectIdFieldName = "OBJECTID",
            exceededTransferLimit = exceeded,
            features = rows,
        });

    /// <summary>One feature, shaped exactly as Victoria's layer 3 returns it.</summary>
    private static object Row(
        string folder,
        string subject,
        string appType,
        string status,
        string house,
        string street,
        long createdMs,
        string purpose)
        => new
        {
            attributes = new
            {
                FOLDER_NUMBER = folder,
                SUBJECT = subject,
                AppType = appType,
                STATUS = status,
                HOUSE = house,
                STREET = street,
                CREATED_DATE = createdMs,
                PURPOSE = purpose,
                CityContact = "ROB BATEMAN",
                email = "rbateman@victoria.ca",
                phone = "250.361.0292",
            },
        };

    /// <summary>Serves canned bodies by URL fragment and records what was asked for.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly List<(string Fragment, string Body)> _routes = new();

        public List<string> Requested { get; } = new();

        public void Add(string urlFragment, string body) => _routes.Add((urlFragment, body));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            Requested.Add(url);

            foreach (var (fragment, body) in _routes)
            {
                if (url.Contains(fragment, StringComparison.Ordinal))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(body),
                    });
                }
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}"),
            });
        }
    }
}

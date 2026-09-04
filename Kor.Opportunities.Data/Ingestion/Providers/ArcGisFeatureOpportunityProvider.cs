#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Data.Ingestion.Providers;

/// <summary>
/// Reads an ArcGIS Feature/Map Server layer — the platform behind ArcGIS Hub and
/// ArcGIS Open Data, which is what most BC municipalities and regional districts
/// publish through. One adapter for the platform means a new city is a config
/// row rather than a new scraper.
///
/// WHY THIS EXISTS: BC Stats discontinued the Major Projects Inventory (last
/// issue Q3 2025), so the province has no forward-pipeline feed. Municipal
/// development-permit and rezoning APPLICATIONS are earlier than MPI ever was —
/// they name the site, the purpose and the applicant's agent months before a
/// tender exists, which is when the structural engineer is actually chosen.
///
/// Four ways ArcGIS differs from the CKAN-shaped GenericJson provider, every one
/// of them verified against City of Victoria's live layer on 2026-09-03:
///   1. Rows are <c>features[].attributes</c>, not a flat array.
///   2. There is no total in the page — more data is signalled by
///      <c>exceededTransferLimit</c>, and an ArcGIS *error* is HTTP 200 with an
///      "error" object in the body.
///   3. Dates are epoch MILLISECONDS, not ISO strings.
///   4. **One application is many rows.** These layers are spatial: a rezoning
///      spanning nine parcels is nine features with identical attributes but
///      different geometry. Victoria's 258 rows are 146 applications. Ingesting
///      the raw rows would file the same project up to nine times, so rows are
///      collapsed on the application number and their addresses merged.
///
/// ⚠ "Development Permit AREA" layers are static zoning overlays, not
/// applications. Point this at an applications layer or it will ingest polygons.
/// </summary>
public sealed class ArcGisFeatureOpportunityProvider : IOpportunityProvider
{
    private const int DefaultPageSize = 2000;
    private const int DefaultMaxPagesPerRun = 20;
    private const int HardPageCeiling = 5000;
    private const int MaxLocationLength = 400;

    private readonly HttpClient _httpClient;
    private readonly ILogger<ArcGisFeatureOpportunityProvider> _logger;

    public ArcGisFeatureOpportunityProvider(
        HttpClient httpClient,
        ILogger<ArcGisFeatureOpportunityProvider> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public OpportunitySourceType SourceType => OpportunitySourceType.ArcGisFeatureService;

    public async Task<IReadOnlyList<OpportunityCandidate>> FetchAsync(
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig,
        CancellationToken ct)
    {
        var map = ArcGisMapping.Build(sourceConfig);
        var layerUrl = source.BaseUrl.TrimEnd('/');

        // Size pages to what the SERVICE says it will give, not to what someone
        // guessed. Asking for more than maxRecordCount silently truncates, and
        // asking for far less is how a run hits its cancellation timeout: a
        // GovCanada backfill was cancelled at 120 small pages where 8 large ones
        // finished comfortably. Few and large.
        var serviceMax = await ReadMaxRecordCountAsync(layerUrl, source, ct).ConfigureAwait(false);
        var pageSize = Math.Clamp(Math.Min(map.PageSize, serviceMax ?? map.PageSize), 1, HardPageCeiling);

        var byRef = new Dictionary<string, Application>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        var rowsRead = 0;
        var rowsSkipped = 0;
        var offset = 0;
        var pages = 0;
        var truncated = false;

        while (pages < map.MaxPagesPerRun && !ct.IsCancellationRequested)
        {
            var url = BuildQueryUrl(layerUrl, map.Where, map.OutFields, offset, pageSize);
            using var doc = await GetJsonAsync(url, source, ct).ConfigureAwait(false);
            if (doc is null)
            {
                truncated = true;
                break;
            }

            pages++;
            var root = doc.RootElement;

            // An ArcGIS error is HTTP 200 with an "error" object in the body —
            // it will not surface as a failed request.
            if (root.TryGetProperty("error", out var err))
            {
                _logger.LogWarning(
                    "ArcGIS source {SourceName} returned an error payload at offset {Offset}: {Error}",
                    source.Name, offset, err.ToString());
                truncated = true;
                break;
            }

            if (!root.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            var countThisPage = 0;
            foreach (var feature in features.EnumerateArray())
            {
                countThisPage++;
                rowsRead++;
                if (!feature.TryGetProperty("attributes", out var attrs) || attrs.ValueKind != JsonValueKind.Object)
                {
                    rowsSkipped++;
                    continue;
                }

                if (!TryAccumulate(attrs, map, byRef, order))
                {
                    rowsSkipped++;
                }
            }

            var more = root.TryGetProperty("exceededTransferLimit", out var xfer)
                       && xfer.ValueKind == JsonValueKind.True;

            if (!more || countThisPage == 0)
            {
                break;
            }

            offset += pageSize;

            if (pages >= map.MaxPagesPerRun)
            {
                truncated = true;
            }
        }

        if (truncated)
        {
            // Say it out loud. A partial read that looks like a clean one is the
            // exact failure the freshness checks were built for.
            _logger.LogWarning(
                "ArcGIS source {SourceName} returned a PARTIAL read ({Pages} page(s) of {PageSize}, cap {MaxPages}).",
                source.Name, pages, pageSize, map.MaxPagesPerRun);
        }

        var results = order.Select(k => byRef[k].ToCandidate(map)).ToList();

        _logger.LogInformation(
            "ArcGIS source {SourceName}: {Rows} row(s) over {Pages} page(s) collapsed to {Apps} application(s); {Skipped} row(s) skipped.",
            source.Name, rowsRead, pages, results.Count, rowsSkipped);

        return results;
    }

    /// <summary>
    /// Folds one feature row into its application. Returns false when the row
    /// carries nothing usable.
    /// </summary>
    private static bool TryAccumulate(
        JsonElement attrs,
        ArcGisMapping map,
        Dictionary<string, Application> byRef,
        List<string> order)
    {
        var externalRef = ReadString(attrs, map.ExternalRefField)?.Trim();
        var title = ReadString(attrs, map.TitleField)?.Trim();

        // No stable id means it cannot be deduped across runs; no title means the
        // relevance gate has nothing to read. Either way it is noise.
        if (string.IsNullOrWhiteSpace(externalRef) || string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        if (map.StatusField is not null && map.RequiredStatuses.Count > 0)
        {
            var status = ReadString(attrs, map.StatusField);
            if (status is null || !map.RequiredStatuses.Contains(status.Trim()))
            {
                return false;
            }
        }

        if (!byRef.TryGetValue(externalRef, out var app))
        {
            app = new Application(externalRef, attrs.GetRawText());
            byRef[externalRef] = app;
            order.Add(externalRef);
        }

        app.Absorb(attrs, map, title);
        return true;
    }

    /// <summary>
    /// One real application, folded together from every parcel row that carries
    /// its number. Scalars take the first non-empty value seen; addresses are the
    /// union, in order; the date is the earliest, because that is when the
    /// application was actually made.
    /// </summary>
    private sealed class Application
    {
        private readonly List<string> _addresses = new();

        public Application(string externalRef, string rawJson)
        {
            ExternalRef = externalRef;
            RawJson = rawJson;
        }

        public string ExternalRef { get; }

        public string RawJson { get; }

        public string? Title { get; private set; }

        public string? AppType { get; private set; }

        public string? Description { get; private set; }

        /// <summary>
        /// The party who filed — i.e. the developer or their agent. Some layers
        /// carry it (Coquitlam's APPLICANT names "Rail House Builders Inc.");
        /// most do not. It is the single most valuable field these feeds hold,
        /// so where it exists it is pushed to the FRONT of the description
        /// rather than left in RawJson where nothing reads it.
        /// </summary>
        public string? Applicant { get; private set; }

        public DateTimeOffset? PostedDateUtc { get; private set; }

        public string? ContactName { get; private set; }

        public string? ContactEmail { get; private set; }

        public string? ContactPhone { get; private set; }

        public int RowCount { get; private set; }

        public void Absorb(JsonElement attrs, ArcGisMapping map, string title)
        {
            RowCount++;
            Title ??= title;
            AppType ??= ReadString(attrs, map.TypeField);
            Description ??= JoinFields(attrs, map.DescriptionFields, " ");
            Applicant ??= ReadString(attrs, map.ApplicantField);
            ContactName ??= ReadString(attrs, map.ContactNameField);
            ContactEmail ??= ReadString(attrs, map.ContactEmailField);
            ContactPhone ??= ReadString(attrs, map.ContactPhoneField);

            var posted = ReadEpochMillis(attrs, map.PostedDateField);
            if (posted is not null && (PostedDateUtc is null || posted < PostedDateUtc))
            {
                PostedDateUtc = posted;
            }

            var address = BuildAddress(attrs, map.AddressFields);
            if (address is not null && !_addresses.Contains(address, StringComparer.OrdinalIgnoreCase))
            {
                _addresses.Add(address);
            }
        }

        public OpportunityCandidate ToCandidate(ArcGisMapping map)
        {
            // The application TYPE is the strongest relevance signal these layers
            // carry ("Rezoning" vs "Sign Variance"), so it belongs in the title
            // the gate reads, not buried in the payload.
            var title = string.IsNullOrWhiteSpace(AppType) ? Title! : $"{AppType} — {Title}";
            var location = JoinCapped(_addresses, "; ", MaxLocationLength);

            var description = Description ?? location;
            if (!string.IsNullOrWhiteSpace(Applicant))
            {
                description = $"Applicant: {Applicant}. {description}";
            }

            return new OpportunityCandidate
            {
                Title = Trim(title, 400)!,
                Buyer = map.BuyerOverride,
                Location = location,
                Url = BuildDetailUrl(map, ExternalRef),
                Description = Trim(description, 4000),
                PostedDateUtc = PostedDateUtc,
                ExternalReference = Trim(ExternalRef, 200),
                ProjectCity = map.CityOverride,
                ProjectProvince = map.ProvinceOverride,
                BuyerContactName = Trim(ContactName, 200),
                BuyerContactEmail = Trim(ContactEmail, 200),
                BuyerContactPhone = Trim(ContactPhone, 100),
                RawJson = RawJson,
            };
        }
    }

    private static string? JoinCapped(IReadOnlyList<string> parts, string sep, int max)
    {
        if (parts.Count == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        var kept = 0;
        foreach (var p in parts)
        {
            var addition = sb.Length == 0 ? p : sep + p;
            if (sb.Length + addition.Length > max)
            {
                break;
            }

            sb.Append(addition);
            kept++;
        }

        if (sb.Length == 0)
        {
            return Trim(parts[0], max);
        }

        if (kept < parts.Count)
        {
            var more = $" (+{parts.Count - kept} more)";
            if (sb.Length + more.Length <= max)
            {
                sb.Append(more);
            }
        }

        return sb.ToString();
    }

    private static string BuildDetailUrl(ArcGisMapping map, string externalRef)
    {
        if (!string.IsNullOrWhiteSpace(map.DetailUrlTemplate))
        {
            return map.DetailUrlTemplate!.Replace("{ref}", Uri.EscapeDataString(externalRef), StringComparison.Ordinal);
        }

        return map.FallbackUrl ?? string.Empty;
    }

    private static string? BuildAddress(JsonElement attrs, IReadOnlyList<string> fields)
        => JoinFields(attrs, fields, " ");

    /// <summary>Concatenates the non-empty values of several attributes, in order.</summary>
    private static string? JoinFields(JsonElement attrs, IReadOnlyList<string> fields, string separator)
    {
        if (fields.Count == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        foreach (var f in fields)
        {
            var v = ReadString(attrs, f);
            if (string.IsNullOrWhiteSpace(v) || string.Equals(v.Trim(), "None", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append(separator);
            }

            sb.Append(v.Trim());
        }

        return sb.Length == 0 ? null : sb.ToString();
    }

    /// <summary>
    /// ArcGIS date fields are epoch MILLISECONDS. Read as ISO they silently
    /// become nonsense dates, so they are converted explicitly.
    /// </summary>
    private static DateTimeOffset? ReadEpochMillis(JsonElement attrs, string? field)
    {
        if (field is null || !TryGetPropertyCaseInsensitive(attrs, field, out var el))
        {
            return null;
        }

        long ms;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var n))
        {
            ms = n;
        }
        else if (el.ValueKind == JsonValueKind.String
                 && long.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s))
        {
            ms = s;
        }
        else
        {
            return null;
        }

        // Guard against a field that is not actually a timestamp: reject anything
        // before 1990 or after 2100.
        if (ms < 631_152_000_000 || ms > 4_102_444_800_000)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(ms);
    }

    private static string? ReadString(JsonElement attrs, string? field)
    {
        if (field is null || !TryGetPropertyCaseInsensitive(attrs, field, out var el))
        {
            return null;
        }

        var s = el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.ToString(),
            _ => null,
        };

        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var p in obj.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string BuildQueryUrl(string layerUrl, string where, string outFields, int offset, int pageSize)
        => $"{layerUrl}/query?where={Uri.EscapeDataString(where)}" +
           $"&outFields={Uri.EscapeDataString(outFields)}" +
           "&returnGeometry=false&f=json" +
           $"&resultOffset={offset.ToString(CultureInfo.InvariantCulture)}" +
           $"&resultRecordCount={pageSize.ToString(CultureInfo.InvariantCulture)}";

    private async Task<int?> ReadMaxRecordCountAsync(string layerUrl, OpportunitySource source, CancellationToken ct)
    {
        try
        {
            using var doc = await GetJsonAsync($"{layerUrl}?f=json", source, ct).ConfigureAwait(false);
            if (doc is not null
                && doc.RootElement.TryGetProperty("maxRecordCount", out var m)
                && m.ValueKind == JsonValueKind.Number
                && m.TryGetInt32(out var v)
                && v > 0)
            {
                return v;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Not fatal — fall back to the configured page size.
            _logger.LogDebug(ex, "ArcGIS source {SourceName}: could not read maxRecordCount.", source.Name);
        }

        return null;
    }

    private async Task<JsonDocument?> GetJsonAsync(string url, OpportunitySource source, CancellationToken ct)
    {
        var timeout = TimeSpan.FromSeconds(Math.Max(10, source.RequestTimeoutSeconds));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Kor.Opportunities.Worker/1.0 (+ilalonde@korstructural.com)");
        request.Headers.TryAddWithoutValidation("Accept", "application/json,*/*;q=0.8");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "ArcGIS source {SourceName}: HTTP {Status} from {Url}.",
                source.Name, (int)response.StatusCode, url);
            return null;
        }

        var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            return await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token).ConfigureAwait(false);
        }
    }

    private static string? Trim(string? value, int max)
        => value is null ? null : (value.Length <= max ? value : value[..max]);

    /// <summary>Per-source field mapping, from OpportunitySourceMappings.</summary>
    internal sealed record ArcGisMapping(
        string Where,
        string OutFields,
        int PageSize,
        int MaxPagesPerRun,
        string ExternalRefField,
        string TitleField,
        string BuyerOverride,
        string? TypeField,
        string? StatusField,
        IReadOnlyList<string> DescriptionFields,
        string? ApplicantField,
        string? PostedDateField,
        string? ContactNameField,
        string? ContactEmailField,
        string? ContactPhoneField,
        IReadOnlyList<string> AddressFields,
        IReadOnlySet<string> RequiredStatuses,
        string? DetailUrlTemplate,
        string? FallbackUrl,
        string? CityOverride,
        string? ProvinceOverride)
    {
        public static ArcGisMapping Build(IReadOnlyDictionary<string, string> cfg)
        {
            return new ArcGisMapping(
                Get(cfg, "arcgis.where") ?? "1=1",
                Get(cfg, "arcgis.outFields") ?? "*",
                ReadInt(cfg, "arcgis.pageSize", DefaultPageSize),
                ReadInt(cfg, "arcgis.maxPagesPerRun", DefaultMaxPagesPerRun),
                Required(cfg, "arcgis.externalRefField"),
                Required(cfg, "arcgis.titleField"),
                Required(cfg, "arcgis.buyerOverride"),
                Get(cfg, "arcgis.typeField"),
                Get(cfg, "arcgis.statusField"),
                // Singular key kept for the common one-field case; the plural
                // form concatenates, for layers that split the story across
                // "what is proposed" and "where the file has got to".
                SplitList(Get(cfg, "arcgis.descriptionFields") ?? Get(cfg, "arcgis.descriptionField")),
                Get(cfg, "arcgis.applicantField"),
                Get(cfg, "arcgis.postedDateField"),
                Get(cfg, "arcgis.contactNameField"),
                Get(cfg, "arcgis.contactEmailField"),
                Get(cfg, "arcgis.contactPhoneField"),
                SplitList(Get(cfg, "arcgis.addressFields")),
                new HashSet<string>(SplitList(Get(cfg, "arcgis.requiredStatuses")), StringComparer.OrdinalIgnoreCase),
                Get(cfg, "arcgis.detailUrlTemplate"),
                Get(cfg, "arcgis.fallbackUrl"),
                Get(cfg, "arcgis.cityOverride"),
                Get(cfg, "arcgis.provinceOverride"));
        }

        private static string? Get(IReadOnlyDictionary<string, string> cfg, string key)
            => cfg.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

        private static string Required(IReadOnlyDictionary<string, string> cfg, string key)
            => Get(cfg, key) ?? throw new InvalidOperationException($"ArcGIS source requires mapping '{key}'.");

        private static int ReadInt(IReadOnlyDictionary<string, string> cfg, string key, int fallback)
            => int.TryParse(Get(cfg, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0
                ? v
                : fallback;

        private static IReadOnlyList<string> SplitList(string? csv)
            => string.IsNullOrWhiteSpace(csv)
                ? Array.Empty<string>()
                : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

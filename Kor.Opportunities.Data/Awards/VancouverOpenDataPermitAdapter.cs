#nullable enable
using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Data.Awards;

/// <summary>
/// Pulls the issued-building-permits JSON dataset from opendata.vancouver.ca.
/// Re-runs are cheap because UpsertAsync dedupes on (PermitSourceId, ExternalId).
/// </summary>
public sealed class VancouverOpenDataPermitAdapter
{
    public const string AdapterName = "VancouverOpenData";

    private readonly HttpClient _http;
    private readonly IBuildingPermitStore _store;
    private readonly CanonicalOrgResolver _resolver;
    private readonly ILogger<VancouverOpenDataPermitAdapter> _logger;
    private readonly int _maxBytesPerResponse;
    private readonly int _maxRowsPerRun;

    public VancouverOpenDataPermitAdapter(
        HttpClient http,
        IBuildingPermitStore store,
        CanonicalOrgResolver resolver,
        ILogger<VancouverOpenDataPermitAdapter> logger,
        int maxBytesPerResponse = 50 * 1024 * 1024,
        int maxRowsPerRun = 20000)
    {
        _http = http;
        _store = store;
        _resolver = resolver;
        _logger = logger;
        _maxBytesPerResponse = maxBytesPerResponse > 0 ? maxBytesPerResponse : int.MaxValue;
        _maxRowsPerRun = maxRowsPerRun > 0 ? maxRowsPerRun : int.MaxValue;
    }

    public sealed record AdapterResult(int Pulled, int Upserted, int CanonicalsResolved, int Failed);

    /// <summary>Opendatasoft v2.1 caps a page at 100 and the offset at 10,000.</summary>
    private const int OdsPageSize = 100;

    private const int OdsMaxOffset = 10_000;

    /// <summary>
    /// Accepts either the legacy <c>/exports/json</c> endpoint stored in
    /// PermitSource or a records URL, and returns the records endpoint. Kept
    /// tolerant so an old configuration row still works after this change.
    /// </summary>
    internal static string ToRecordsEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return endpoint;
        }

        var trimmed = endpoint.Trim();
        var query = trimmed.IndexOf('?', StringComparison.Ordinal);
        if (query >= 0)
        {
            trimmed = trimmed[..query];
        }

        var exports = trimmed.IndexOf("/exports/", StringComparison.OrdinalIgnoreCase);
        if (exports >= 0)
        {
            return trimmed[..exports] + "/records";
        }

        return trimmed.EndsWith("/records", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed.TrimEnd('/') + "/records";
    }

    public async Task<AdapterResult> ImportAsync(PermitSourceRow source, int maxRowsPerSource, CancellationToken ct)
    {
        var rowCap = maxRowsPerSource > 0 ? Math.Min(_maxRowsPerRun, maxRowsPerSource) : _maxRowsPerRun;

        // ── Why this pages instead of downloading the export ────────────────
        // The configured endpoint was Opendatasoft's /exports/json, which returns
        // the WHOLE dataset in one response. That worked until the dataset grew
        // past the 50 MB response cap, and then this source went dark for THREE
        // MONTHS — last successful ingest 2026-06-07, still IsActive = 1, still
        // polled daily, failing every time with "Content-Length 82765401 exceeds
        // configured limit (52428800)" into a column nobody reads.
        //
        // Raising the cap only moves the date it breaks again. The /records
        // endpoint pages, and sorted newest-first a daily run touches one or two
        // pages instead of 80 MB. Same field names, so the mapping below is
        // unchanged.
        var recordsBase = ToRecordsEndpoint(source.Endpoint);

        var pulled = 0;
        var upserted = 0;
        var canonicals = 0;
        var failed = 0;
        var offset = 0;

        var items = new List<JsonNode?>();
        while (pulled + items.Count < rowCap && offset < OdsMaxOffset)
        {
            ct.ThrowIfCancellationRequested();
            var take = Math.Min(OdsPageSize, rowCap - items.Count);
            var url = $"{recordsBase}?limit={take.ToString(CultureInfo.InvariantCulture)}" +
                      $"&offset={offset.ToString(CultureInfo.InvariantCulture)}" +
                      "&order_by=issuedate%20desc";

            using var pageResp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!pageResp.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Vancouver OpenData {(int)pageResp.StatusCode}");
            }

            var pageBody = await Kor.Opportunities.Data.Ingestion.HttpReadHelpers.ReadStringWithCapAsync(
                pageResp.Content,
                _maxBytesPerResponse,
                "Vancouver permits",
                ct).ConfigureAwait(false);

            var pageResults = JsonNode.Parse(pageBody)?["results"]?.AsArray();
            if (pageResults is null || pageResults.Count == 0)
            {
                break;
            }

            foreach (var n in pageResults)
            {
                items.Add(n);
            }

            if (pageResults.Count < take)
            {
                break;
            }

            offset += take;
        }

        if (items.Count == 0)
        {
            return new AdapterResult(0, 0, 0, 0);
        }

        _logger.LogInformation(
            "Vancouver permits: {Count} record(s) read over {Pages} page(s).",
            items.Count, (items.Count + OdsPageSize - 1) / OdsPageSize);

        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            if (pulled >= rowCap)
            {
                _logger.LogWarning("Cap reached, stopping at {Rows} rows.", pulled);
                break;
            }

            pulled++;

            try
            {
                var ext = ReadString(item?["permitnumber"]) ?? ReadString(item?["permitnumbercreateddate"]);
                if (string.IsNullOrWhiteSpace(ext))
                {
                    failed++;
                    continue;
                }

                var previousNames = await _store.GetOrgNamesSnapshotAsync(source.Id, ext, ct).ConfigureAwait(false);
                var previousOwnerName = previousNames.HasValue ? previousNames.Value.OwnerName : null;
                var previousApplicantName = previousNames.HasValue ? previousNames.Value.ApplicantName : null;
                var previousContractorName = previousNames.HasValue ? previousNames.Value.ContractorName : null;
                decimal? lat = null;
                decimal? lng = null;
                if (item?["geom"]?["geometry"]?["coordinates"]?.AsArray() is { Count: >= 2 } coords)
                {
                    lng = ReadDecimal(coords[0]);
                    lat = ReadDecimal(coords[1]);
                }

                var upsert = new BuildingPermitUpsert(
                    PermitSourceId: source.Id,
                    ExternalId: ext!,
                    PermitNumber: ReadString(item?["permitnumber"]),
                    PermitCategory: ReadString(item?["permitcategory"]) ?? ReadString(item?["typeofwork"]),
                    WorkType: ReadString(item?["typeofwork"]),
                    ProjectDescription: ReadString(item?["projectdescription"]),
                    EstimatedValue: ReadDecimal(item?["projectvalue"]),
                    NumberOfDwellingUnits: ReadInt(item?["numberofdwellingunits"]),
                    Address: ReadString(item?["address"]),
                    City: "Vancouver",
                    PostalCode: ReadString(item?["postalcode"]),
                    GeoLocalArea: ReadString(item?["geolocalarea"]),
                    Latitude: lat,
                    Longitude: lng,
                    AppliedDate: ReadDate(item?["applicationdate"]),
                    IssuedDate: ReadDate(item?["issuedate"]),
                    OwnerName: ReadString(item?["propertyowner"]) ?? ReadString(item?["applicant"]),
                    ApplicantName: ReadString(item?["applicant"]),
                    ContractorName: ReadString(item?["buildingcontractor"]),
                    SpecificUseCategory: ReadString(item?["specificusecategory"]),
                    PropertyUse: ReadString(item?["propertyuse"]),
                    RawJson: item?.ToJsonString());

                var permitId = await _store.UpsertAsync(upsert, ct).ConfigureAwait(false);
                upserted++;

                if (!SameName(previousOwnerName, upsert.OwnerName))
                {
                    var canon = string.IsNullOrWhiteSpace(upsert.OwnerName)
                        ? null
                        : await _resolver.ResolveAsync(
                            upsert.OwnerName,
                            OrgKinds.Unknown,
                            "BuildingPermit.Owner",
                            ct,
                            createArchived: true).ConfigureAwait(false);
                    await _store.SetOwnerCanonicalAsync(permitId, canon, ct).ConfigureAwait(false);
                    if (canon.HasValue)
                    {
                        canonicals++;
                    }
                }

                if (!SameName(previousApplicantName, upsert.ApplicantName))
                {
                    var canon = string.IsNullOrWhiteSpace(upsert.ApplicantName)
                        ? null
                        : await _resolver.ResolveAsync(
                            upsert.ApplicantName,
                            OrgKinds.Unknown,
                            "BuildingPermit.Applicant",
                            ct,
                            createArchived: true).ConfigureAwait(false);
                    await _store.SetApplicantCanonicalAsync(permitId, canon, ct).ConfigureAwait(false);
                    if (canon.HasValue)
                    {
                        canonicals++;
                    }
                }

                if (!SameName(previousContractorName, upsert.ContractorName))
                {
                    var canon = string.IsNullOrWhiteSpace(upsert.ContractorName)
                        ? null
                        : await _resolver.ResolveAsync(
                            upsert.ContractorName,
                            OrgKinds.Unknown,
                            "BuildingPermit.Contractor",
                            ct,
                            createArchived: true).ConfigureAwait(false);
                    await _store.SetContractorCanonicalAsync(permitId, canon, ct).ConfigureAwait(false);
                    if (canon.HasValue)
                    {
                        canonicals++;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Permit row failed.");
                failed++;
            }
        }

        return new AdapterResult(pulled, upserted, canonicals, failed);
    }

    private static bool SameName(string? existing, string? incoming)
        => string.Equals(existing?.Trim(), incoming?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string? ReadString(JsonNode? n)
    {
        if (n is null) return null;

        try
        {
            var s = n.GetValue<string?>();
            return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static decimal? ReadDecimal(JsonNode? n)
    {
        if (n is null) return null;

        try
        {
            return n.GetValue<decimal?>();
        }
        catch
        {
            try
            {
                var s = n.GetValue<string?>();
                return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
            }
            catch
            {
                return null;
            }
        }
    }

    private static int? ReadInt(JsonNode? n)
    {
        if (n is null) return null;

        try
        {
            return n.GetValue<int?>();
        }
        catch
        {
            try
            {
                var s = n.GetValue<string?>();
                return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;
            }
            catch
            {
                return null;
            }
        }
    }

    private static DateTime? ReadDate(JsonNode? n)
    {
        var s = ReadString(n);
        if (string.IsNullOrWhiteSpace(s))
        {
            return null;
        }

        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
            ? d.Date
            : null;
    }
}

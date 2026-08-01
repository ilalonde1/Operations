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

    public async Task<AdapterResult> ImportAsync(PermitSourceRow source, int maxRowsPerSource, CancellationToken ct)
    {
        var rowCap = maxRowsPerSource > 0 ? Math.Min(_maxRowsPerRun, maxRowsPerSource) : _maxRowsPerRun;
        using var resp = await _http.GetAsync(source.Endpoint, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Vancouver OpenData {(int)resp.StatusCode}");
        }

        var body = await Kor.Opportunities.Data.Ingestion.HttpReadHelpers.ReadStringWithCapAsync(
            resp.Content,
            _maxBytesPerResponse,
            "Vancouver permits",
            ct).ConfigureAwait(false);

        var root = JsonNode.Parse(body);
        var arr = root?.AsArray();
        if (arr is null)
        {
            return new AdapterResult(0, 0, 0, 0);
        }

        var pulled = 0;
        var upserted = 0;
        var canonicals = 0;
        var failed = 0;

        foreach (var item in arr)
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

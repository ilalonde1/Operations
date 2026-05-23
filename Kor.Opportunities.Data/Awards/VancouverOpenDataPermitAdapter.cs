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

    public VancouverOpenDataPermitAdapter(
        HttpClient http,
        IBuildingPermitStore store,
        CanonicalOrgResolver resolver,
        ILogger<VancouverOpenDataPermitAdapter> logger)
    {
        _http = http;
        _store = store;
        _resolver = resolver;
        _logger = logger;
    }

    public sealed record AdapterResult(int Pulled, int Upserted, int CanonicalsResolved, int Failed);

    public async Task<AdapterResult> ImportAsync(PermitSourceRow source, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(source.Endpoint, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Vancouver OpenData {(int)resp.StatusCode}");
        }

        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
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
            pulled++;

            try
            {
                var ext = ReadString(item?["permitnumber"]) ?? ReadString(item?["permitnumbercreateddate"]);
                if (string.IsNullOrWhiteSpace(ext))
                {
                    failed++;
                    continue;
                }

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

                if (!string.IsNullOrWhiteSpace(upsert.OwnerName))
                {
                    var canon = await _resolver.ResolveAsync(
                        upsert.OwnerName,
                        OrgKinds.Unknown,
                        "BuildingPermit.Owner",
                        ct).ConfigureAwait(false);
                    if (canon.HasValue)
                    {
                        await _store.SetOwnerCanonicalAsync(permitId, canon.Value, ct).ConfigureAwait(false);
                        canonicals++;
                    }
                }

                if (!string.IsNullOrWhiteSpace(upsert.ApplicantName))
                {
                    var canon = await _resolver.ResolveAsync(
                        upsert.ApplicantName,
                        OrgKinds.Unknown,
                        "BuildingPermit.Applicant",
                        ct).ConfigureAwait(false);
                    if (canon.HasValue)
                    {
                        await _store.SetApplicantCanonicalAsync(permitId, canon.Value, ct).ConfigureAwait(false);
                        canonicals++;
                    }
                }

                if (!string.IsNullOrWhiteSpace(upsert.ContractorName))
                {
                    var canon = await _resolver.ResolveAsync(
                        upsert.ContractorName,
                        OrgKinds.Unknown,
                        "BuildingPermit.Contractor",
                        ct).ConfigureAwait(false);
                    if (canon.HasValue)
                    {
                        await _store.SetContractorCanonicalAsync(permitId, canon.Value, ct).ConfigureAwait(false);
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

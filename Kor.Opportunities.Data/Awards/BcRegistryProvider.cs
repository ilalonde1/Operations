#nullable enable
using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Data.Awards;

/// <summary>
/// Pulls corporate-registration snapshots from OrgBook BC's public API.
/// Open Government Licence - BC, no auth required. Documented at
/// https://orgbook.gov.bc.ca/api/.
/// </summary>
public sealed class BcRegistryProvider : IEnrichmentProvider
{
    private const string BaseUrl = "https://orgbook.gov.bc.ca/api/v4";

    private readonly HttpClient _http;
    private readonly ICanonicalOrgStore _store;
    private readonly ILogger<BcRegistryProvider> _logger;

    public BcRegistryProvider(HttpClient http, ICanonicalOrgStore store, ILogger<BcRegistryProvider> logger)
    {
        _http = http;
        _store = store;
        _logger = logger;
    }

    public string Name => "BcRegistry";
    public TimeSpan TtlOnSuccess => TimeSpan.FromDays(365);
    public TimeSpan TtlOnFailure => TimeSpan.FromDays(7);

    public async Task<EnrichmentResult> RefreshAsync(long canonicalOrgId, CancellationToken ct)
    {
        var info = await _store.GetNameAndKindAsync(canonicalOrgId, ct).ConfigureAwait(false);
        if (info is null)
        {
            return new EnrichmentResult(EnrichmentStatuses.Failed, "Canonical org not found.", null, null);
        }

        var (name, kind) = info.Value;
        if (string.IsNullOrWhiteSpace(name))
        {
            return new EnrichmentResult(EnrichmentStatuses.NoData, "Empty name.", null, null);
        }

        try
        {
            var searchUrl = $"{BaseUrl}/search/topic?q={Uri.EscapeDataString(name)}&inactive=false";
            using var searchResp = await _http.GetAsync(searchUrl, ct).ConfigureAwait(false);
            if (!searchResp.IsSuccessStatusCode)
            {
                return new EnrichmentResult(
                    EnrichmentStatuses.Failed,
                    $"OrgBook search {(int)searchResp.StatusCode}",
                    null,
                    null);
            }

            var searchBody = await searchResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var search = JsonNode.Parse(searchBody);
            var results = search?["results"]?.AsArray();
            if (results is null || results.Count == 0)
            {
                return new EnrichmentResult(EnrichmentStatuses.NoData, "No OrgBook match.", searchBody, null);
            }

            var top = results[0];
            var topicId = ReadTopicId(top);
            // OrgBook v4 search result already contains everything we need —
            // entity_name is in names[] filtered by type='entity_name'; status, type,
            // jurisdiction, registration date all live in attributes[]. No detail fetch needed.
            var legalName = ReadNameByType(top, "entity_name");

            if (string.IsNullOrWhiteSpace(topicId))
            {
                return new EnrichmentResult(EnrichmentStatuses.NoData, "No topic id on top hit.", searchBody, null);
            }
            if (string.IsNullOrWhiteSpace(legalName))
            {
                return new EnrichmentResult(EnrichmentStatuses.NoData, "No entity_name on top hit.", searchBody, null);
            }
            if (!IsLikelyMatch(name, legalName))
            {
                return new EnrichmentResult(
                    EnrichmentStatuses.NoData,
                    $"Best hit '{legalName}' not a confident match for '{name}'.",
                    searchBody,
                    null);
            }

            var status = top?["inactive"]?.GetValue<bool?>() == true ? "Historical" : "Active";
            var entityType = ReadAttribute(top, "entity_type");
            var jurisdiction = ReadAttribute(top, "registered_jurisdiction")
                           ?? ReadAttribute(top, "home_jurisdiction");

            DateTime? incorporationDate = null;
            var incorporationRaw = ReadAttribute(top, "registration_date");
            if (!string.IsNullOrWhiteSpace(incorporationRaw)
                && DateTime.TryParse(incorporationRaw, CultureInfo.InvariantCulture,
                                     DateTimeStyles.AssumeUniversal, out var parsedDate))
            {
                incorporationDate = parsedDate.Date;
            }

            // OrgBook's source_id is the BC Registry registration number (e.g. "FM1013732"),
            // not the CRA business number. The CRA BN, when known, lives in names[] with type='business_number'.
            var businessNumber = ReadNameByType(top, "business_number");
            var registeredOffice = top?["addresses"]?.AsArray() is { Count: > 0 } addrArr
                ? ReadString(addrArr[0]?["civic_address"])
                : null;

            var snapshot = new BcRegistrySnapshot(
                TopicId: topicId,
                LegalName: legalName,
                EntityType: entityType,
                Status: status,
                IncorporationDate: incorporationDate,
                Jurisdiction: jurisdiction,
                BusinessNumber: businessNumber,
                RegisteredOffice: registeredOffice);

            await _store.RecordBcRegistrySnapshotAsync(canonicalOrgId, snapshot, ct).ConfigureAwait(false);

            return new EnrichmentResult(
                EnrichmentStatuses.Ok,
                ErrorMessage: null,
                ResultJson: searchBody,
                Notes: $"Matched '{name}' to '{legalName}' (topic {topicId}).");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "BcRegistry refresh failed for canonical id {Id}.", canonicalOrgId);
            return new EnrichmentResult(EnrichmentStatuses.Failed, ex.Message, null, null);
        }
    }

    private static string? ReadTopicId(JsonNode? node)
    {
        // OrgBook /api/v4 returns the topic id as `result.id` (integer) at the top
        // level of each search result. Older shapes had `topic_id` or nested `topic.id`,
        // so fall through to those as defenses.
        return ReadIntAsString(node?["id"])
            ?? ReadIntAsString(node?["topic_id"])
            ?? ReadIntAsString(node?["topic"]?["id"]);
    }

    /// <summary>
    /// OrgBook v4 search results carry an array of named claims, each tagged by
    /// type — e.g. type='entity_name' for the legal name, type='business_number'
    /// for the CRA BN. Find the first one matching the requested type.
    /// </summary>
    private static string? ReadNameByType(JsonNode? node, string nameType)
    {
        var arr = node?["names"]?.AsArray();
        if (arr is null) return null;
        foreach (var item in arr)
        {
            if (string.Equals(ReadString(item?["type"]), nameType, StringComparison.OrdinalIgnoreCase))
            {
                return ReadString(item?["text"]);
            }
        }
        return null;
    }

    /// <summary>
    /// OrgBook v4 attributes[] is similarly key/value-shaped; type tells you what
    /// claim it is (entity_status, entity_type, registered_jurisdiction, etc.).
    /// </summary>
    private static string? ReadAttribute(JsonNode? node, string attrType)
    {
        var arr = node?["attributes"]?.AsArray();
        if (arr is null) return null;
        foreach (var item in arr)
        {
            if (string.Equals(ReadString(item?["type"]), attrType, StringComparison.OrdinalIgnoreCase))
            {
                return ReadString(item?["value"]);
            }
        }
        return null;
    }

    private static string? ReadIntAsString(JsonNode? node)
    {
        if (node is null) return null;
        try
        {
            // Try int first; fall back to string for older shapes that returned numeric strings.
            return node.GetValue<int>().ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            try { return node.GetValue<string?>(); } catch { return null; }
        }
    }

    private static string? ReadString(JsonNode? node)
    {
        if (node is null) return null;

        try
        {
            return node.GetValue<string?>();
        }
        catch
        {
            try
            {
                return node.GetValue<int>().ToString(CultureInfo.InvariantCulture);
            }
            catch
            {
                return node.ToString();
            }
        }
    }

    /// <summary>
    /// Coarse name-match guard. Normalize both names and strip common corporate
    /// suffixes so OrgBook's fuzzy search does not attach a loose hit.
    /// </summary>
    private static bool IsLikelyMatch(string query, string hit)
    {
        static string Norm(string s) => new string(s.ToLowerInvariant()
            .Replace(" ltd", "")
            .Replace(" inc", "")
            .Replace(" corp", "")
            .Replace(" corporation", "")
            .Replace(" limited", "")
            .Replace(" company", "")
            .Replace(" group", "")
            .Replace(" llp", "")
            .Replace(".", "")
            .Replace(",", "")
            .Replace("'", "")
            .Replace("&", "and")
            .Where(char.IsLetterOrDigit)
            .ToArray());

        var normalizedQuery = Norm(query);
        var normalizedHit = Norm(hit);
        if (normalizedQuery.Length == 0 || normalizedHit.Length == 0) return false;
        if (normalizedQuery == normalizedHit) return true;
        if (normalizedQuery.Length >= 4 && normalizedHit.Contains(normalizedQuery, StringComparison.Ordinal)) return true;
        if (normalizedHit.Length >= 4 && normalizedQuery.Contains(normalizedHit, StringComparison.Ordinal)) return true;
        return false;
    }
}

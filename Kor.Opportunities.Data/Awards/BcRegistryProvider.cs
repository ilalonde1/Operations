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
            var topName = ReadTopName(top);

            if (string.IsNullOrWhiteSpace(topicId))
            {
                return new EnrichmentResult(EnrichmentStatuses.NoData, "No topic_id on top hit.", searchBody, null);
            }

            if (!IsLikelyMatch(name, topName ?? ""))
            {
                return new EnrichmentResult(
                    EnrichmentStatuses.NoData,
                    $"Best hit '{topName}' not a confident match for '{name}'.",
                    searchBody,
                    null);
            }

            var detailUrl = $"{BaseUrl}/topic/{Uri.EscapeDataString(topicId)}/formatted";
            using var detailResp = await _http.GetAsync(detailUrl, ct).ConfigureAwait(false);
            if (!detailResp.IsSuccessStatusCode)
            {
                return new EnrichmentResult(
                    EnrichmentStatuses.Failed,
                    $"OrgBook detail {(int)detailResp.StatusCode}",
                    null,
                    null);
            }

            var detailBody = await detailResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var detail = JsonNode.Parse(detailBody);

            var legalName = detail?["names"]?.AsArray() is { Count: > 0 } namesArr
                ? ReadString(namesArr[0]?["text"])
                : topName;
            var entityType = ReadString(detail?["type"]?["name"])
                          ?? ReadString(detail?["topic"]?["type"]);
            var status = detail?["inactive"]?.GetValue<bool?>() == true ? "Historical" : "Active";
            var jurisdiction = detail?["addresses"]?.AsArray() is { Count: > 0 } addrArr
                ? ReadString(addrArr[0]?["province"])
                : null;

            DateTime? incorporationDate = null;
            var incorporationRaw = ReadString(detail?["topic"]?["source_id_date"])
                                ?? ReadString(detail?["created_at"]);
            if (DateTime.TryParse(
                    incorporationRaw,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out var parsedDate))
            {
                incorporationDate = parsedDate.Date;
            }

            var businessNumber = ReadString(detail?["topic"]?["source_id"]);
            var registeredOffice = detail?["addresses"]?.AsArray() is { Count: > 0 } officeArr
                ? ReadString(officeArr[0]?["civic_address"])
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
                ResultJson: detailBody,
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
        return ReadString(node?["topic_id"])
            ?? ReadString(node?["topic"]?["id"]);
    }

    private static string? ReadTopName(JsonNode? node)
    {
        if (node?["names"]?.AsArray() is { Count: > 0 } names)
        {
            return ReadString(names[0]?["text"]);
        }

        return ReadString(node?["topic"]?["source_id"]);
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

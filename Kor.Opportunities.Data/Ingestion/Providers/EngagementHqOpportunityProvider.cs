#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Data.Ingestion.Providers;

/// <summary>
/// EngagementHQ / Bang the Table public consultation platforms.
///
/// Several BC regional districts publish their development and zoning
/// applications as ENGAGEMENT PROJECTS rather than on a map — the Regional
/// District of Nanaimo says so in as many words on its own Current Development
/// Applications page, which is a one-line redirect to getinvolved.rdn.ca.
/// The platform exposes every project at <c>/projects.json</c> with no key.
///
/// Why this matters: those regional districts cover the UNINCORPORATED areas
/// (Nanoose Bay, Errington, Bowser, Coombs) that no municipal feed reaches, and
/// the descriptions routinely name the applicant, which most municipal ArcGIS
/// layers do not.
///
/// ⚠ A regional district is NOT its member municipalities. RDN applications are
/// electoral-area applications; the City of Parksville and the Town of Qualicum
/// Beach run their own planning and publish separately.
///
/// Config keys (all optional except where noted):
///   engagementhq.projectsUrl      absolute url to projects.json; defaults to
///                                 BaseUrl + "/projects.json"
///   engagementhq.titleRegex       a project counts as an application only if
///                                 its NAME matches this. Default matches
///                                 "PL2026-028", "Development Application",
///                                 "Zoning Amendment", "Rezoning", "Subdivision"
///                                 and "OCP Amendment". Without it the feed also
///                                 carries parks plans, budgets and elections.
///   engagementhq.includeArchived  "true" to keep archived projects. Default
///                                 false — archived means decided, and by then
///                                 the structural engineer was chosen.
///   engagementhq.buyerOverride    buyer name; defaults to the source name
///   engagementhq.cityOverride     ProjectCity
///   engagementhq.provinceOverride ProjectProvince, default "BC"
/// </summary>
public sealed class EngagementHqOpportunityProvider : IOpportunityProvider
{
    private static readonly Regex TagRx = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex WsRx = new(@"\s+", RegexOptions.Compiled);

    // Kept deliberately narrow. A consultation platform carries far more than
    // development applications, and everything it carries looks like a project.
    private const string DefaultTitlePattern =
        @"PL\d{4}-\d+|development\s+application|zoning\s+amendment|rezoning|subdivis|official\s+community\s+plan\s+amendment|ocp\s+amendment";

    private readonly HttpClient _http;
    private readonly ILogger<EngagementHqOpportunityProvider> _log;

    public EngagementHqOpportunityProvider(HttpClient http, ILogger<EngagementHqOpportunityProvider> log)
    {
        _http = http;
        _log = log;
    }

    public OpportunitySourceType SourceType => OpportunitySourceType.EngagementHq;

    public async Task<IReadOnlyList<OpportunityCandidate>> FetchAsync(
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig,
        CancellationToken ct)
    {
        var url = Get(sourceConfig, "engagementhq.projectsUrl")
                  ?? source.BaseUrl.TrimEnd('/') + "/projects.json";

        var titleRx = new Regex(
            Get(sourceConfig, "engagementhq.titleRegex") ?? DefaultTitlePattern,
            RegexOptions.IgnoreCase);

        var includeArchived = string.Equals(
            Get(sourceConfig, "engagementhq.includeArchived"), "true", StringComparison.OrdinalIgnoreCase);

        var buyer = Get(sourceConfig, "engagementhq.buyerOverride") ?? source.Name;
        var city = Get(sourceConfig, "engagementhq.cityOverride");
        var province = Get(sourceConfig, "engagementhq.provinceOverride") ?? "BC";

        using var doc = await _http.GetFromJsonAsync<JsonDocument>(url, ct).ConfigureAwait(false)
                        ?? throw new InvalidOperationException($"EngagementHQ returned no body for {url}.");

        if (!doc.RootElement.TryGetProperty("projects", out var projects)
            || projects.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                $"EngagementHQ payload at {url} has no 'projects' array — the platform's shape changed.");
        }

        var results = new List<OpportunityCandidate>();
        var seenNames = 0;
        var droppedArchived = 0;
        var droppedTitle = 0;

        foreach (var p in projects.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            seenNames++;

            var name = Clean(Str(p, "name"));
            if (name.Length == 0)
            {
                continue;
            }

            if (!titleRx.IsMatch(name))
            {
                droppedTitle++;
                continue;
            }

            if (!includeArchived && Bool(p, "archived"))
            {
                droppedArchived++;
                continue;
            }

            var description = Clean(Str(p, "description"));
            var permalink = Str(p, "permalink");
            var id = p.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number
                ? idEl.GetInt64().ToString(CultureInfo.InvariantCulture)
                : null;

            // The platform gives a slug, not an absolute url. Build one so the
            // opportunity is clickable and so the dedup hash has a stable Url.
            var link = string.IsNullOrWhiteSpace(permalink)
                ? source.BaseUrl
                : source.BaseUrl.TrimEnd('/') + "/" + permalink.TrimStart('/');

            // The file number in the title is the stable external reference;
            // the numeric id changes if a project is recreated.
            var fileNo = Regex.Match(name, @"PL\d{4}-\d+", RegexOptions.IgnoreCase);
            var externalRef = fileNo.Success ? fileNo.Value.ToUpperInvariant() : (id ?? permalink);

            results.Add(new OpportunityCandidate
            {
                Title = Trim(name, 400) ?? name,
                Buyer = buyer,
                Location = city,
                Url = link,
                Description = Trim(description, 4000),
                PostedDateUtc = Date(p, "published_at") ?? Date(p, "created_at"),
                ProjectCity = city,
                ProjectProvince = province,
                ExternalReference = Trim(externalRef, 200),
                SourceInternalId = id,
                RawJson = p.GetRawText(),
            });
        }

        _log.LogInformation(
            "EngagementHQ {Source}: {Kept} application(s) from {Total} project(s) ({DroppedTitle} not applications, {DroppedArchived} archived).",
            source.Name, results.Count, seenNames, droppedTitle, droppedArchived);

        return results;
    }

    private static string? Get(IReadOnlyDictionary<string, string> cfg, string key)
        => cfg.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    private static string Str(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";

    private static bool Bool(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v)
           && (v.ValueKind == JsonValueKind.True
               || (v.ValueKind == JsonValueKind.String && string.Equals(v.GetString(), "true", StringComparison.OrdinalIgnoreCase)));

    private static DateTimeOffset? Date(JsonElement el, string prop)
    {
        var raw = Str(el, prop);
        return !string.IsNullOrWhiteSpace(raw)
               && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed : null;
    }

    /// <summary>Strip the HTML the platform stores descriptions as, and the
    /// entities inside it. Left as plain text because the relevance gate reads
    /// Description, and markup between words hides the words from it.</summary>
    private static string Clean(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return "";
        }

        var t = TagRx.Replace(s, " ");
        t = t.Replace("&nbsp;", " ")
             .Replace("&amp;", "&")
             .Replace("&quot;", "\"")
             .Replace("&#39;", "'")
             .Replace("&rsquo;", "'")
             .Replace("&lsquo;", "'")
             .Replace("&ldquo;", "\"")
             .Replace("&rdquo;", "\"")
             .Replace("&ndash;", "-")
             .Replace("&mdash;", "-");
        return WsRx.Replace(t, " ").Trim();
    }

    private static string? Trim(string? s, int max)
        => string.IsNullOrWhiteSpace(s) ? null : (s.Length <= max ? s : s[..max]);
}

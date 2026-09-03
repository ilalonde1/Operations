#nullable enable
using System;
using System.Text.Json;

namespace Kor.Opportunities.Worker.Services.Research;

/// <summary>
/// Decides whether a research result may overwrite the organization it was run
/// for. Extracted from <see cref="BdResearchExecutorService"/> so it can be
/// tested directly — this is the check that stands between a plausible paragraph
/// and the destruction of a correct one.
///
/// Why it exists: on 2026-09-03 canonical 74300 held BOTH a Denver mixed-use
/// developer and a Victoria BC architecture practice (a bad --merge-dba merge).
/// A refresh was handed only the words "Continuum Partners, LLC", researched the
/// Denver firm, declared the record on file wrong, and replaced it in place.
/// Nothing errored, and IntelNarrative keeps no history, so the correct text was
/// simply gone.
/// </summary>
public static class ResearchIdentityGate
{
    /// <summary>
    /// Bare lower-case host, "www." stripped. Accepts a full URL or a bare
    /// domain, because CanonicalOrg.Website holds URLs and WebsiteDomain holds
    /// domains. Returns null for anything that is not a usable host.
    /// </summary>
    public static string? NormalizeHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var s = value.Trim();
        if (!s.Contains("://", StringComparison.Ordinal))
        {
            s = "https://" + s;
        }

        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal))
        {
            host = host[4..];
        }

        return string.IsNullOrWhiteSpace(host) ? null : host;
    }

    /// <summary>
    /// Evaluates a research result against the anchor we hold for the org.
    /// </summary>
    /// <param name="anchorHost">
    /// Normalized host from the org's Website/WebsiteDomain, or null when the org
    /// has no anchor (7,200 of 9,695 active orgs as at 2026-09-03).
    /// </param>
    /// <param name="resultJson">Raw research output.</param>
    /// <returns>
    /// Allow=false means the result must NOT be persisted. ResearchedHost is the
    /// host the researcher reported, used to backfill an anchor-less org.
    /// </returns>
    public static GateDecision Evaluate(string? anchorHost, string? resultJson)
    {
        if (string.IsNullOrWhiteSpace(resultJson))
        {
            return new GateDecision(true, null, null);
        }

        string? researchedHost = null;
        bool? matchesRecord = null;

        try
        {
            using var doc = JsonDocument.Parse(resultJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("entityWebsite", out var w) && w.ValueKind == JsonValueKind.String)
                {
                    researchedHost = NormalizeHost(w.GetString());
                }

                if (doc.RootElement.TryGetProperty("entityMatchesRecord", out var m)
                    && (m.ValueKind == JsonValueKind.True || m.ValueKind == JsonValueKind.False))
                {
                    matchesRecord = m.GetBoolean();
                }
            }
        }
        catch (JsonException)
        {
            // Unparseable output is the extractor's problem, not the gate's — it
            // will fail downstream on its own terms. The gate does not add a
            // second failure mode for malformed JSON.
            return new GateDecision(true, null, null);
        }

        // The researcher itself says it could not confirm the entity. Believe it.
        if (matchesRecord == false)
        {
            return new GateDecision(
                false,
                "the researcher reported it could not confirm this is the same organization",
                researchedHost);
        }

        // Hard stop: we hold an authoritative website and the researcher came back
        // with a different one. This is Continuum exactly, and the one case where
        // overwriting destroys correct data.
        if (anchorHost is not null
            && researchedHost is not null
            && !string.Equals(anchorHost, researchedHost, StringComparison.Ordinal))
        {
            return new GateDecision(
                false,
                $"researched entity '{researchedHost}' does not match the website on file '{anchorHost}'",
                researchedHost);
        }

        return new GateDecision(true, null, researchedHost);
    }

    public readonly record struct GateDecision(bool Allow, string? Reason, string? ResearchedHost);
}

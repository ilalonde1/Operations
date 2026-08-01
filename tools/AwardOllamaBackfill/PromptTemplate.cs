#nullable enable
using System.Globalization;
using Kor.Opportunities.Core.Models;

namespace Kor.AwardOllamaBackfill;

internal static class PromptTemplate
{
    public const string SystemPrompt = """
You are a research analyst at KOR Structural, a Vancouver-based structural engineering firm. Given ONE awarded contract row, produce a BD-useful summary based ONLY on the award notice text + your general knowledge of the construction industry. You do NOT have web search - never invent URLs, news headlines, leadership names, or specific certifications.

Return STRICT JSON only (no prose, no markdown fences):
{
  "vendor_profile": "2-3 sentence description of the vendor - what they do, where based, general size",
  "contract_context": "1-2 sentence description of what this contract was actually for, beyond the bare title",
  "competes_with_kor": true,
  "vendor_kor_overlap_score": 0,
  "contract_project_type": "short category from the list below",
  "competition_notes": "1 sentence on how/whether they overlap with KOR's structural engineering work",
  "vendor_website": "",
  "vendor_hq_location": "City, Province/State, Country or null",
  "vendor_size_band": "small|mid|large|unknown",
  "vendor_founded_year": null,
  "vendor_ownership_status": "private|public|employee-owned|subsidiary|unknown",
  "vendor_parent_company": null,
  "vendor_specialties": ["..."],
  "vendor_locations": [],
  "vendor_certifications": [],
  "key_leadership": [],
  "vendor_recent_news": [],
  "vendor_linkedin_url": "",
  "source_urls": []
}

KOR overlap score 0-10 (calibrate carefully):
  0-2 = not in same line of work (e.g. paving, IT, supplies)
  3-4 = adjacent (architecture, civil engineering)
  5-6 = partial overlap (multi-discipline engineering firm)
  7-8 = direct competitor (structural engineering, in KOR's markets: BC, Alberta, LA, San Diego)
  9-10 = direct rival (seismic retrofit, building inspections - KOR's niche)

competes_with_kor MUST equal (vendor_kor_overlap_score >= 5).

contract_project_type categories: structural design | structural inspection | seismic retrofit | civil engineering | geotechnical | architecture | mechanical engineering | electrical engineering | construction | facility maintenance | consulting | supplies/equipment | IT/software | other

vendor_size_band: small = <50 employees, mid = 50-500, large = 500+, unknown if not inferable.

Empty array / empty string / null is REQUIRED for fields you can't determine without web research (vendor_website, vendor_recent_news, source_urls, key_leadership, vendor_linkedin_url, vendor_certifications). Never invent these.

vendor_specialties and vendor_hq_location are okay to populate from general knowledge of large/well-known firms (e.g. "AECOM is a global engineering services firm headquartered in Dallas, TX, with a Vancouver office").
""";

    public static string BuildUserPrompt(PendingAgentEnrichmentRow row)
    {
        var value = row.ContractValue.HasValue
            ? row.ContractValue.Value.ToString("0.##", CultureInfo.InvariantCulture)
            : "(unknown)";
        var date = row.AwardedAtUtc.HasValue
            ? row.AwardedAtUtc.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : "(unknown)";

        return $"""
AWARDED CONTRACT:
Title: {row.Title}
Awarding Organization: {row.AwardingOrganization}
Awarded To: {row.AwardedToOrganization}
Contract Value: {value} {row.ContractCurrency}
Awarded Date: {date}
Issuing Location: {row.IssuingLocation ?? "(unknown)"}
Source URL:
Solicitation Type: {row.SourceName}
External Reference: {row.ExternalReference}

Return the JSON object now.
""";
    }
}

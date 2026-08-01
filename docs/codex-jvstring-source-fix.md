# Codex prompt — stop BdResearchImport from minting multi-entity "JV-string" orgs

One change in `tools/BdResearchImport/Program.cs`. No `dotnet build`/`test` (env hangs); Claude verifies + runs.

**Problem.** The honing import resolves proponent/owner/architect names verbatim, so research payloads with combined names ("BC Housing / Lookout Housing and Health Society", "Fraser Health / Infrastructure BC", "Perkins&Will / Schmidt Hammer Lassen Architects") mint a single canonical org per combined string. That fragments the org graph (one developer appears across many "X / Y" rows) and mis-attributes projects. Fix it at the front door: split multi-entity names and resolve to the **operator lead**, dropping funders/procurement-agents.

**Change.** Add a static helper and call it on proponent/owner/architect names immediately before they are passed to the local `ResolveAsync(resolver, options, stats, <name>, kind, source, ct)` calls (there are several — search for `ResolveAsync(resolver` and the `proponentName`/`buyerOrg`/`ownerName`/architect locals around lines ~1023, ~1169, ~1347, ~1518; wrap each name argument).

```csharp
// Splits a combined "A / B (& C)" entity string into its lead operator, dropping
// funders and procurement agents so the graph attributes to who actually builds.
private static string LeadOperator(string? name)
{
    if (string.IsNullOrWhiteSpace(name)) return name ?? string.Empty;
    // separators: " / ", " + ", " & " (keep names that merely contain a slash w/o spaces, e.g. "S.E.N.C.R.L./S.R.L.")
    var parts = System.Text.RegularExpressions.Regex
        .Split(name, @"\s+/\s+|\s+\+\s+|\s+&\s+")
        .Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();
    if (parts.Length <= 1) return name.Trim();

    static bool IsFunderOrAgent(string p)
    {
        var l = p.ToLowerInvariant();
        string[] kw = {
            "bc housing","bc builds","build canada homes","cmhc","canada mortgage",
            "province of","government of","ministry of","alberta infrastructure",
            "infrastructure bc","partnerships bc","mhpm","project managers",
            "city of","district of","township of","municipality of","regional district",
            "metro vancouver","translink"
        };
        return kw.Any(k => l.Contains(k));
    }
    static bool IsJunk(string p)
    {
        var l = p.ToLowerInvariant();
        return l.Contains("multiple") || l.Contains("unnamed") || l == "tbd" ||
               l.Contains("various") || l.Contains("et al") || l.Contains("mandate");
    }

    var lead = parts.FirstOrDefault(p => !IsFunderOrAgent(p) && !IsJunk(p));
    return (lead ?? parts[0]).Trim();
}
```

Use it like: `var proponentId = await ResolveAsync(resolver, options, stats, LeadOperator(proponentName), OrgKinds.Unknown, ProponentSource, ct)...`. Keep storing the **original** combined string in the `ProponentName`/`OwnerName` text field (so nothing is lost) — only the *canonical-org resolution* uses the lead. Apply the same wrap to the buyer/owner and architect name resolutions.

Note: health authorities (Fraser Health, Vancouver Coastal Health, Island Health, Interior/Northern Health, Covenant Health, Alberta Health Services) are intentionally NOT in the funder list — they are owners and should remain eligible as the lead. This heuristic is a forward guard; the existing 356 rows are being decomposed separately with per-string classification.

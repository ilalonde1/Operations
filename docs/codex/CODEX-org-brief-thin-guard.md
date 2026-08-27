# Codex prompt — org-brief "thin org / duplicate" guard (stop barren briefs at source)

One change in `Kor.Opportunities.Data/Briefs/SqlBriefDataStore.cs`, method `GetOrgBriefAsync(long canonicalOrgId, CancellationToken ct)` (~line 603). No `dotnet build`/`test` (env hangs); Claude verifies the build, Ian publishes the app.

**Problem.** A KOR org-brief PDF came out barren. Root cause: the brief was generated for a **sparse duplicate** canonical org (e.g. id 76409 "Greystar Development") while the company's real data lives on a different canonical (55110 "Greystar Real Estate Partners"). `GetOrgBriefAsync` faithfully returned near-empty data for the thin org → barren PDF. Both the Briefs window AND `RelationshipsView` call this same method, so the fix here fixes both surfaces.

**Goal.** When the requested org has essentially no data AND a clearly-richer same-company canonical exists, resolve the brief to that richer canonical (transparently noted), so the user never gets a silently-barren brief. If no richer canonical exists, surface a "minimal data" note so it's clear the org itself is thin (not a generator failure).

**Change (in `GetOrgBriefAsync`, after the org's data is gathered, before `return new OrgBriefData(...)`):**

1. Compute a thinness check: the org is "thin" if `korProjects == 0` AND `recentProjects.Count == 0` AND there are no contacts (reuse whatever contact/affiliation count the method already has; if none is queried, add a quick `SELECT COUNT(*) FROM opportunities.IntelPersonAffiliation WHERE CanonicalOrgId=@id AND RetiredAtUtc IS NULL`).

2. If thin, look for a strictly-richer same-brand canonical via **normalized-name prefix** (this is the safe "same company" signal — a brand variant, not a coincidental substring):
```sql
SELECT TOP 1 r.Id
FROM opportunities.CanonicalOrg t
JOIN opportunities.CanonicalOrg r
  ON r.RetiredAtUtc IS NULL AND r.Id <> t.Id
  AND (r.NormalizedName LIKE t.NormalizedName + '%' OR t.NormalizedName LIKE r.NormalizedName + '%')
WHERE t.Id = @id
ORDER BY (SELECT COUNT(*) FROM opportunities.MajorProjectsInventory m
          WHERE m.ProponentCanonicalOrgId=r.Id OR m.ArchitectCanonicalOrgId=r.Id
             OR m.GeneralContractorCanonicalOrgId=r.Id OR m.StructuralEngineerCanonicalOrgId=r.Id)
       + (SELECT COUNT(*) FROM opportunities.IntelPersonAffiliation a WHERE a.CanonicalOrgId=r.Id AND a.RetiredAtUtc IS NULL) DESC;
```
   Require the candidate to actually be richer (its project+contact count > 0); ignore it otherwise.

3. If a richer canonical is found, **re-run the brief for that id** (`return await GetOrgBriefAsync(richerId, ct)` — but guard against infinite recursion: only redirect once, e.g. pass a `bool alreadyResolved=false` overload or a local flag) and set a new nullable field on `OrgBriefData` — `string? ResolvedFromNote` — to e.g. `"Resolved to the primary record for this company (requested org #{canonicalOrgId} had minimal data)."`.

4. If thin and NO richer canonical, set `ResolvedFromNote = "This organization has minimal data in the graph — the brief reflects what we have."` (so a thin-but-genuine org is clearly flagged, not mistaken for a bug).

**DTO + renderers.** Add `string? ResolvedFromNote` to the `OrgBriefData` record (default null). In both `BriefGenerator.WriteOrgBrief` and `BriefPdfGenerator.WriteOrgBrief`, if `ResolvedFromNote` is non-null, render it as a small italic note line under the title. Keep it minimal — one line.

**Constraints.** Don't change the other brief methods. Keep the recursion guard. ASCII. This is read-only data-building (no DB writes).

After Codex confirms: Claude builds Kor.Opportunities.Data + the App to verify; Ian publishes the WPF app for it to take effect.

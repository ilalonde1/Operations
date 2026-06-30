# Codex: born-archive building-permit orgs at intake (close the `Unknown` warm-set leak)

## Goal

The building-permit import creates a CanonicalOrg for every permit Owner, Applicant, and Contractor — and these are overwhelmingly individuals (homeowners pulling permits) and micro single-permit holdcos. They land **live** under `Kind='Unknown'` and pile into the warm set (6,800+ had to be hand-culled 2026-06-22). Stop the leak at the front door: born-archive these rows at creation, exactly like the procurement firehoses already do. They stay queryable and **auto-resurrect** if ever referenced by real activity (award / MPI project role / intel), because the resolver only skips resurrection for `createArchived` re-touches — a non-firehose reference still unretires them.

## Pattern to follow (already in the codebase)

`CanonicalOrgResolver.ResolveAsync(rawName, kind, source, ct, allowCreate, minConfidenceForCreate, createArchived)` — the firehose call sites pass `createArchived: true`:
- `Kor.Opportunities.Data/Awards/SqlOpportunityAwardStore.cs` (~line 222, awarded-to vendor)
- `Kor.Opportunities.Data/Ingestion/Scraping/BcBidUnverifiedBidResultsScraper.cs` (~line 522, bidder)

## Change — one file

**`Kor.Opportunities.Data/Awards/VancouverOpenDataPermitAdapter.cs`**

There are three `_resolver.ResolveAsync(...)` calls (permit Owner ~line 136, Applicant ~line 152, Contractor ~line 168), each currently:

```csharp
await _resolver.ResolveAsync(
    upsert.OwnerName,          // / ApplicantName / ContractorName
    OrgKinds.Unknown,
    "BuildingPermit.Owner",    // / .Applicant / .Contractor
    ct).ConfigureAwait(false);
```

Add `createArchived: true` to **all three** calls (keep `allowCreate`/`minConfidenceForCreate` at their defaults), e.g.:

```csharp
await _resolver.ResolveAsync(
    upsert.OwnerName,
    OrgKinds.Unknown,
    "BuildingPermit.Owner",
    ct,
    createArchived: true).ConfigureAwait(false);
```

That's the entire change. The permit→canonical links (`SetOwnerCanonicalAsync` etc.) are unchanged — the rows still exist and link, they're just born archived (`RetiredAtUtc` set) instead of warm.

## Constraints

- Change ONLY `VancouverOpenDataPermitAdapter.cs`. Do not touch the resolver, the store, the MPI/award/news sites, or any other intake.
- Do NOT change the MPI proponent sites (`Bc/Ab/Ca*MajorProjectsInventoryProvider`, `BcMpiImporter`) — those are real, project-linked developers/owners and must stay warm.
- Do NOT run `dotnet build` or `dotnet test` (env hangs here — Claude verifies the build after).
- No destructive git operations.
- Do not alter `CanonicalOrgResolver.ResolveAsync`'s signature or default behavior.

## Notes (not for the diff)

- Historical cleanup already done: the 6,856 pre-existing `Unknown` permit/registry rows were archived 2026-06-22 (reversible); 19 real public buyers rescued. This prompt only stops *re-accumulation*.
- After Codex confirms the edit, Claude builds locally and (when Ian deploys the Worker) the `BuildingPermitsImportJob` will born-archive on the next run; verify with `SELECT COUNT(*) FROM opportunities.CanonicalOrg WHERE Kind='Unknown' AND RetiredAtUtc IS NULL` staying ~flat after a permit-import tick.

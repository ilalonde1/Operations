# Codex Deep Adversarial Audit — BD Entity Identity & the Destructive Refresh

## Why this exists

On 2026-09-03 Ian opened the Relationships view for **Continuum**, pressed
**Refresh now**, and watched a record that had described a Victoria, BC
architecture firm become a description of a Denver, Colorado developer — while
still showing the Victoria people. His words: *"I can't have my data fucked up."*

The investigation found three defects stacked on each other. None of them threw
an error. Every one reported success.

1. **The canonical row is two unrelated companies.** Org `74300 "Continuum
   Partners, LLC"` (Kind=Developer) carries four aliases. Two were written by the
   dedup tool: `Continuum Architecture Inc` and `Wil Wiens DBA: Continuum
   Architecture Inc`, both `Source='DedupeMerge'`, `ClassifiedBy='BdCanonicalDedup'`.
   On 2026-06-12 the `--merge-dba` path folded a Victoria BC architecture practice
   into a Denver mixed-use developer. The affiliations show the seam exactly:
   Kapuscinski, Pettigrew, Beintema, Wiens attached 2026-06-12; Falcone and Fair
   attached 2026-09-03.

2. **The refresh has no entity anchor.** Org 74300 has **no Website**. The
   FirmNarrative research keys off the DisplayName alone. Given the name
   "Continuum Partners, LLC" it found the Denver developer — correctly, for that
   string — then wrote a `History` narrative *asserting the previous record was
   populated for the wrong organization* and replaced it. The system did not fail.
   It confidently corrected itself onto the wrong half of a conflated row.

3. **The overwrite is destructive and unversioned.** `IntelNarrative` rows 8730
   (Current) and 8731 (Action) have `CreatedAtUtc = 2026-06-13 20:30:06` and
   `UpdatedAtUtc = 2026-09-03 11:52:37`. The upsert replaced `ParagraphText` in
   place on the same NaturalKey. No retired row, no prior version, no audit trail.
   The only reason the Victoria text is recoverable at all is the nightly full
   backup (`KorOpportunitiesDb`, last good 2026-09-02 17:01).

There is a predecessor audit, `CODEX-bd-apparatus-deep-audit.md`, but it is from
**2026-06 — roughly three months stale, and this module has moved since** (at
minimum migrations 289/290 landed, and the dedup tool has been fail-closed since
2026-07-17 without anyone noticing).

⚠ **Treat that document as a historical pointer, not a baseline.** Do not trust
its "known-fixed" list, do not assume its file inventory still matches the tree,
and do not skip a path because it says the path was cleared. **Enumerate this
module as it exists today first**, and where the old audit's picture no longer
matches reality, say so explicitly — that drift is itself a finding, because it
tells us how much of what we believe about this module is out of date.

What that audit is useful for is contrast: it hunted crashes, FK violations and
aborts — failures that announce themselves. **This class does not announce
itself.** It produces a plausible, well-written, confidently sourced paragraph
about the wrong company, and it overwrites the right one. That is worse than a
crash, because nothing surfaces it until a human who knows the firm happens to
read it.

**Your mindset: assume every identity decision in this module is wrong and prove
how.** For every finding give a concrete failure scenario — the exact rows, names
or interleaving that trigger it, and the resulting corruption. A finding without a
repro path is a question; mark it as such.

**Do NOT fix anything. Produce the findings report only.** We review, then batch
the fixes with validation.

---

## Measured ground truth (do not re-derive; verify only if you doubt it)

Queried live against `KorOpportunitiesDb` on `KOR-APP01\SQLEXPRESS`, 2026-09-03:

| Measure | Value |
|---|---|
| Orgs carrying an `X DBA: Y` alias absorbed by `DedupeMerge` | **3,914** |
| Orgs that have any `IntelNarrative` | **6,625** |
| …of those, orgs with **no Website** | **4,573 (69%)** |
| `IntelNarrative` rows total | **15,539** |

⚠ The 3,914 is a **candidate population, not a defect count** — most DBA merges are
probably correct. It is the population that contains the Continuum class, and
nobody has ever sampled it. Establishing what fraction is actually conflated is a
deliverable of this audit.

⚠ A fourth measure was taken and is **not trustworthy as stated**: 15,345 of 15,539
narrative rows have `UpdatedAtUtc > CreatedAtUtc`. That almost certainly reflects
the upsert bumping timestamps on every run rather than 15k meaning-changes. Do not
repeat it as a corruption count. **Determining whether content-change is
distinguishable from touch-change at all is itself a question for you** — if it is
not, say so plainly, because that means the blast radius of every past refresh is
unknowable from the data.

---

## Already established today — do NOT re-report

1. `BdCanonicalDedup` was **fail-closed and stale**: migrations 289 (`OrgFact`) and
   290 (`CrmTouchpoint`) added FKs to `CanonicalOrg` that were absent from
   `FkTargets`, so **every canonical merge has been blocked since 2026-07-17**.
   Fixed 2026-09-03 by adding both as repoint (not delete) targets. Uncommitted.
2. The similarity gate on `--pairs` works and rejected 4/4 Perkins&Will pairs;
   allowlist campaign files are the sanctioned bypass.
3. The fuzzy normalizer **strips `&` rather than folding it to `and`**, so
   "Perkins and Will" never matches "Perkins&Will". This mints duplicate shells.
   Confirm the blast radius of that specific behaviour — do not just restate it.
4. Perkins&Will was held as 5 canonical rows and was merged to 69688 on 2026-09-03.

---

## Scope — in tiers

### Tier 1 — identity: how a row comes to mean a company (audit EXHAUSTIVELY)

The question at every step is: **what evidence justified deciding these two names
are the same legal entity, and what happens when that evidence is absent?**

- `tools/BdCanonicalDedup/Program.cs` — especially the `--merge-dba` grouping
  (`Person DBA: Company` → post-DBA business name), survivor selection, the fuzzy
  similarity gate, `IsAllowlistedNonSimilar`, and the allowlist campaign loader.
  **The Continuum merge came through here. Reconstruct exactly how, and state
  which condition would have had to differ to prevent it.**
- `Kor.Opportunities.Data/Awards/CanonicalOrgResolver.cs` —
  `NormalizeForFuzzyMatch`, resolve-or-create. What creates a NEW canonical vs
  attaches to an existing one, and on what evidence?
- Org birth paths: the `proponent-drain` and `MajorProjectsInventory.Proponent`
  auto-new routes that created 74300 with confidence 70/80 and **no website**.
- `Kor.Opportunities.Data/Briefs/SqlBriefDataStore.cs` —
  `FindRicherSameBrandCanonicalAsync` / `RedirectSafe` / whole-word-prefix /
  corporate-token logic. This is a *second, independent* same-company heuristic
  with different rules from the dedup gate. **Two heuristics, no arbiter** — do
  they ever disagree, and what does a user see when they do?
- Person identity: `usp_ResolveOrCreateIntelPerson`, the email-SHA1 natural key,
  and affiliation creation. Continuum ended with 6 people from 2 companies on one
  org and nothing flagged it.

### Tier 2 — the refresh: how a row's meaning gets rewritten

- `tools/BdIntelExtract/Program.cs`, `IntelPersistenceService`,
  `IntelExtractorRegistry` and every `IIntelExtractor`.
- The **FirmNarrative** path specifically: what identifying evidence is passed to
  the research step? Name only, or name + website + region + kind? What happens
  when Website is null (4,573 orgs)? Is there any confidence floor, any
  disagreement check against existing content, any human gate?
- The upsert: NaturalKey construction, in-place `ParagraphText` replacement,
  `RetiredAtUtc` semantics. **Is there any path that preserves the prior text?**
- The `History` narrative type: what writes it, and can it assert "the prior
  record was wrong" without evidence? In the Continuum case that assertion is
  itself the corruption — it reads as provenance and is not.
- Who can trigger a refresh: the "Refresh now" button, the per-person "Refresh"
  buttons, `EnrichmentDispatchJob`, and any bulk path. **Is there a bulk refresh
  that could do this to thousands of orgs unattended? If so that is finding #1.**

### Tier 3 — consumers and blast radius (audit for: do they honour identity?)

- `RelationshipsView` and the WPF BD workspace surfaces, `SqlBdReportService`,
  `SqlPursuitBriefStore`, the MCP `/ask` read path.
- The JV-string canonicals (prior art: `CODEX-jvstring-source-fix.md`). Six rows
  whose DisplayName is a whole team sentence (e.g. `Herzog & de Meuron +
  Perkins&Will (project on hold — architect relationship terminated Dec 2024)`)
  still exist. Each hides a project. Is the source fix in place, and if so why do
  these persist?

---

## Questions the report must answer

1. **How did Continuum happen, mechanically?** Name the exact code path and the
   exact condition that allowed a Victoria architecture DBA to merge into a Denver
   developer. Cite file and line.
2. **Is there a bulk/unattended refresh path?** If a scheduled job can rewrite
   narratives for orgs with no website, say so first and loudest.
3. **How many of the 3,914 DBA-merge orgs are actually conflations?** Give a
   detection query we can run — something like: an org whose affiliated people's
   email domains disagree, or whose aliases resolve to different regions/kinds.
   A query that finds them all beats an estimate.
4. **Can a past refresh's damage be detected at all**, or is it invisible once
   `ParagraphText` is replaced? If invisible, what is the cheapest change that
   makes it visible going forward?
5. **Which of the two same-company heuristics is authoritative** — the dedup fuzzy
   gate or the brief-guard `RedirectSafe`? Show a case where they disagree.
6. **What else in this module decides identity that we have not listed?** The
   2026-08 estate audit's calibration finding was that *absence claims were wrong
   4 out of 4* because they grepped for a phrase instead of a capability. Look for
   the capability.
7. **What has changed in this module since 2026-06 that nobody tracked?** New
   migrations, new jobs, new extractors, renamed or deleted tools. The scope list
   below was written from a three-month-old map plus one day's investigation;
   assume it is incomplete and correct it.

---

## Database access (you will need it — the interesting evidence is in the data)

A developer Windows account has **no** rights on `KorOpportunitiesDb`; the app
connects with SQL auth. Get the connection string from the machine environment
variable `KOR_OPPORTUNITIES_OPPORTUNITIESDB` on **KOR-APP01**, read over the
remote registry from this workstation:

```powershell
$reg = [Microsoft.Win32.RegistryKey]::OpenRemoteBaseKey('LocalMachine','KOR-APP01')
$cs  = $reg.OpenSubKey('SYSTEM\CurrentControlSet\Control\Session Manager\Environment').GetValue('KOR_OPPORTUNITIES_OPPORTUNITIESDB')
```

Then connect with `System.Data.SqlClient` using `$cs`. **Never echo the string or
the password into output, a file, or this report.** Schema is `opportunities`.
Useful column names that are easy to get wrong: `CanonicalOrg.DisplayName` (not
`Name`), `IntelPerson.DisplayName` / `.Email`, `OrgAlias.RawName` (not `Alias`),
`IntelNarrative.ParagraphText`.

## Constraints

- **Read-only.** No `dotnet build`, no `dotnet test` (the env hangs). No DB
  writes, no migrations, no destructive operations, no service restarts.
- SELECT-only queries are welcome and encouraged — this is a live-data problem and
  a claim backed by a query beats a claim backed by reading code.
- ASCII output. No fixes, no patches — findings only.
- **State what your audit covered and what it did not**, and name at least one
  same-class fault your pass would NOT have caught. A broad claim over a narrow
  check is worse than no check, because the next reader stops looking.

## Deliverable

Findings report at `docs/codex/CODEX-BD-ENTITY-IDENTITY-AND-REFRESH-AUDIT-RESPONSE.md`.

Rank findings by **silent-corruption risk first** — a defect that writes plausible
wrong data outranks one that throws. For each: the failure scenario, the repro
path, the affected row count if you can query it, and whether it is live today.

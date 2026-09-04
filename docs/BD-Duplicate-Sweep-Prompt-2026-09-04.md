# Seed prompt — whole-database duplicate sweep

> **Run on 2026-09-04.** Results, the check, the kept reports and the proposed batches are in
> `docs/BD-Duplicate-Sweep-2026-09-04.md`. Two figures in this prompt did not survive contact:
> the affiliation "3,551 groups" counts retired rows (the live defect is 462 groups), and
> "721 orgs" outside the umbrellas measures as 749 with the same 306 groups.

**Paste everything below the line into a fresh Claude Code session in `C:\VIsual Studio Projects\Operations`.**

Filed here to match the repo's existing convention for session prompts
(`docs/BD-Audit-Prompt-2026-06-19.md`, `docs/BD-Fix-Prompt-2026-06-19.md`,
`docs/BD-Closeout-Prompt-2026-06-20.md`, `docs/BD-Cron-Cost-Audit-Prompt-2026-06-10.md`).
Every number in it was measured on 2026-09-04 against `KOR-APP01\SQLEXPRESS`.

---

## The job

One canonical org row is supposed to be one real-world company. Measure how far the platform is
from that, build the check that finds every violation at once, and only then fix. **Do not start
merging.**

Read `Kor.Opportunities.Data/CLAUDE.md` first — it opens with this exact sentence and with the
2026-09-03 Continuum incident, which is the way this task goes wrong.

## What is already known — do not re-derive it

Measured 2026-09-04. Reproduce these before trusting them, then move on.

| | Count |
|---|---|
| Live canonical orgs | **9,734** |
| …carrying a `WebsiteDomain` | **2,530** (26%) |
| …with a `Website` but no `WebsiteDomain` (half-anchored) | 7 |
| Same-domain groups (>1 live org on one domain) | **327 groups, 845 orgs** |
| …after excluding `.gov`/`.edu`/`canada.ca`/`alberta.ca` umbrellas | **306 groups, 721 orgs** |
| Rows whose `WebsiteDomain` is the literal string `'null'` | **11** |
| Same `FuzzyNormalizedName`, different Id | 2 groups, 4 orgs |
| Empty `FuzzyNormalizedName` (can never group) | 5 |
| Names containing `&` or ` and ` (normalizer blind spot) | **796** |
| Duplicate `(IntelPersonId, CanonicalOrgId)` affiliation pairs | **3,551 groups, 9,330 rows** |

**The headline is the second row, not the third.** 7,204 of 9,734 orgs have no domain anchor at
all, so no domain-based sweep can see three-quarters of the table. Any plan that starts with
"group by domain" is a plan for a quarter of the problem.

**The affiliation duplicates are historic, not new.** 3,493 of the 3,551 groups were last written
in **June 2026** — a bulk import, not the enrichment runs. 15 groups date from September 2026.
`Kor.Opportunities.Data/CLAUDE.md` says "461 such pairs existed platform-wide when first measured";
that figure is stale by an order of magnitude and should be corrected in that file.

## Live proof the class is real and still producing

From the Island permit work finished 2026-09-04:

- The 532 named applicants across five municipal files are **372 distinct strings but 345 real
  parties**. **50 strings sit in a group that is one company spelled more than one way** — "READ
  JONES CHRISTOFFERSEN LTD", "READ JONES CHRISTOFFERSEN LTD." and "READ JONES CHRISTOFFERSEN" are
  three strings and one firm; so are "NES ARCHITECTURE" / "NES ARCHITECTURE LTD.", three spellings
  of Alan Lowe Architect, and "HCMA ARCHITECTUE + DESIGN" (a typo in the city's own file).
- **RJC Engineers was itself split across two canonical rows** (74588 and 69014) until merged on
  2026-09-04. Our single most direct competitor, held twice.
- Perkins&Will was five fragments until merged to 69688 on 2026-09-03.

## The two ways this goes wrong — both have already happened

1. **Over-aggressive keys conflate unrelated firms.** `NormalizeAggressiveKey` strips `partners`,
   `llc`, `architecture` and `inc`, so *"Continuum Partners, LLC"* (a Denver mixed-use developer)
   and *"Continuum Architecture Inc"* (a Victoria architecture practice, founded 1910) both
   collapsed to `continuum`. `ChooseSurvivor` preferred Developer over Architect, and a later
   narrative refresh then overwrote the Victoria firm's record with the Denver firm's. Every step
   reported success. Full account: `docs/codex/CODEX-BD-ENTITY-IDENTITY-AND-REFRESH-AUDIT.md`.

2. **A shared domain is not a shared company.** `canada.ca` holds **32** live orgs that are 32
   different federal departments. `www2.gov.bc.ca` holds 22. `calstate.edu` and
   `capitalprojectsreport.ucop.edu` hold 7 each. These are correct as they stand. Meanwhile
   `henselphelps.com` (5), `mortenson.com` (5), `stantec.com` (5) and `balfourbeattyus.com` (4)
   are genuine duplicate shells — regional variants of one firm. **A sweep that cannot tell those
   apart will destroy the government hierarchy on its first run.**

## Order of work — this is the whole point

Repo rule 11 applies: on the second instance you stop fixing and build the thing that finds them
all. This is well past the second instance, so:

1. **Characterise, in one sentence per class.** There are at least five: same-domain-different-name,
   name-variant-same-company, `&`-vs-`and`, umbrella-domain false positives, duplicate affiliations.
   If a sentence will not come out for a class, that class is not understood yet.
2. **Write the check that fails on every instance at once**, as a test or a `tools/` gate — not a
   one-off query. It must state, in its own summary: what it compares, what it does not, and at
   least one same-class fault it would NOT catch. A broad name on a narrow check is worse than no
   check.
3. **Run `tools/BdIntegrityCheck` and keep the report.** It already carries
   `org_same_domain_different_names` and `org_merge_dead_survivor`. Diff before against after —
   that is the no-regression check and it needs no new tooling.
4. **Only then propose merges**, in reviewed batches, with the survivor named and justified per
   group.

## Hard constraints

- **`BdCanonicalDedup --commit` is on HOLD.** Do not run it against the live database without
  Ian saying so in the session. Dry-run output is what you produce.
- **Archive, never delete.** Retire the loser and record the merge in `CanonicalOrgMerge`.
- **Non-similar pairs go in the allowlist**, one CSV per batch, in
  `tools/BdCanonicalDedup/dedup-non-similar-allowlist.d/` — see `rjc-2026-09-04.csv` for the shape.
- **`BdCanonicalDedup` is fail-closed on schema** and that is working as designed: a new FK to
  `CanonicalOrg` in neither `FkTargets` nor `IntelDeleteTargets` blocks every merge. It caught
  migrations 289/290 having silently blocked merges for seven weeks. If it refuses, handle the FK.
- **Resolve by exact `DisplayName`, never `LIKE`.** A substring pass once matched "Chard" to
  *Richard & Co. Architecture* and "Seba" to *Sebastien Garon*.
- **`NormalizedName` is computed; `FuzzyNormalizedName` is not.** Set the fuzzy key explicitly on
  any insert or the row gets an empty one and groups with unrelated orgs.
- **Migrations live in this repo** at `Kor.Opportunities.Data/Schema/` — the last one applied is
  **307**. (`KOR.Drafter/db/` is the *other* project's migration folder; do not put these there.)
- **Ian runs Codex.** If the fix wants a Codex pass, write `docs/codex/CODEX-<TOPIC>.md`, hand it
  over, and stop.

## First three commands

```
dotnet run --project tools/BdIntegrityCheck                       # baseline report, keep it
dotnet run --project tools/BdCanonicalDedup                       # DRY RUN by default; writes
                                                                  # tools/BdCanonicalDedup/output/dedupe-plan.csv
python docs/island-pipeline/query-opportunities-db.py <file.sql>  # ad-hoc SQL against APP01
```

Both tools read the connection string from `KOR_OPPORTUNITIES_OPPORTUNITIESDB` when `--db` is
omitted. `BdCanonicalDedup` is dry-run unless `--commit` is passed, and `--commit` is on hold —
so the plan CSV is the deliverable. There is no `--report` flag; do not invent one.

## Definition of done

A check exists that fails on every instance of at least one named class; the integrity report is
clean of that class or the remaining rows are allowlisted with a reason; `Kor.Opportunities.Data/CLAUDE.md`
carries the corrected affiliation-duplicate figure; and nothing was merged that Ian did not see first.

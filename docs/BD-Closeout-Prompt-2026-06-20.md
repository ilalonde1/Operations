# BD Audit — Close-Out Prompt (2026-06-20): M1, M5-issued, M6

> Independent Codex session. Implements the **deferred** items from
> `docs/BD-Fix-Prompt-2026-06-19.md` now that Ian has made the decisions.
> Decisions are GIVEN below — do not re-defer them. Code + migrations only.

## Working rules
- **Code + migrations only. NO DB writes, NO `dotnet build`/`dotnet test`, NO
  destructive git/DB ops.** Write the migration files; the operator applies them.
- New migrations start at **255** in `Kor.Opportunities.Data/Schema/`. Each:
  `SET XACT_ABORT ON;` + `BEGIN TRAN`/`COMMIT`, informative `PRINT`s, idempotent,
  GO-separate any add-column-then-reference.
- Cite the exact code line/SQL object you change. Match existing patterns
  (the m251-254 migrations from the prior fix are good references).
- End with a changelog (finding → files → migration #) and note anything you
  could not do safely.

## M1 — IntelPerson identity-anchor NaturalKey (highest risk; dry-run first)
**Problem (verified):** `opportunities.IntelPerson.NaturalKey` =
`SHA1(NormalizeName(displayName))` (name-only), with `UQ_IntelPerson_NaturalKey`.
Two different people with the same name can't coexist. The CRM + AI contact graph
depends on this.

**Decision / scheme (GIVEN):** person NaturalKey = SHA1 of the **first non-empty**
of, in order:
1. `email` (trimmed, lowercased)
2. `LinkedinUrl` (trimmed, lowercased)
3. `NormalizeName(displayName) + '|org:' + <primary current affiliation CanonicalOrgId>`
   (the person's `IsCurrent=1` affiliation org; if several, the lowest CanonicalOrgId)
4. `NormalizeName(displayName)` (final fallback — preserves today's behavior when
   no email/linkedin/affiliation)

Implement in `Kor.Opportunities.Data/Intel/IntelNaturalKey.cs` (or a new person-key
helper) + the write path `MergePersonAsync` in `IntelPersistenceService.cs`, and
mirror in the hand-ingest contract (`reference_intelperson_ingest_contract`:
migrations 162/163 pattern) so new inserts use the new key.

**Backfill migration — MUST be safe:**
- First emit a **DRY-RUN report** (PRINT counts): how many active IntelPerson rows
  get a *changed* key, and crucially detect any **NEW collisions** — two distinct
  active rows that would map to the **same** new key.
- If new collisions exist, do NOT blindly update (would violate the unique
  constraint). Disambiguate deterministically (append `|id:<Id>` to the loser's
  key) OR leave those rows on their old key and PRINT them for review. Never
  produce a duplicate-key failure.
- **Affiliation keys are personId-based, so they are unaffected** — confirm this in
  a comment (don't touch `IntelPersonAffiliation` keys).
- Re-runnable.

## M5 — exclude issued/construction-stage from the pre-selection funnel
**Decision (GIVEN):** a building permit is **issued only after stamped structural
drawings exist** → the structural engineer is already selected. The CA funnel's
purpose is *pre-selection* leads, so `issued` and later (construction) stages are
past the SE window and must be excluded. **Keep** `filed/filing/triage/approved/
reinstated`; **exclude** `issued` (SF) and SD `Issued`/`Inspection Followup`
(in addition to the dead stages already gated in m254/the provider).

- Extend `IsTerminalPermitStage` (or add a `IsPostSelectionStage`) in
  `CaSocrataMajorProjectsInventoryProvider.cs` to also reject `issued` (SF) and the
  SD construction stages. Update the m254-style config `$where` to also exclude
  `issued`/`Issued`/`Inspection Followup`.
- Migration: soft-retire existing **active** rows where `SourceKey LIKE 'sf:%'` and
  `Stage='issued'`, and `SourceKey LIKE 'sdcity:%'` and `Stage IN ('Issued',
  'Inspection Followup')`, with a clear `RetiredReason` (reversible). Print the count.

## M6 — CA address = one project (de-inflate the funnel)
**Decision (GIVEN):** SF permits sharing the same address (`ProjectName`) are the
same building = one BD opportunity. **Survivor rule:** keep the permit with the
**lowest `SourceKey`** (earliest permit number) per address; soft-retire the rest
as same-building superseded permits (`RetiredReason`, reversible).

- Migration: for each `SourceKey LIKE 'sf:%'`, `RetiredAtUtc IS NULL` address with
  `COUNT(*) > 1` grouped by `ProjectName`, keep `MIN(SourceKey)`, retire the others.
  If a retired row has child Intel rows (IntelProject*/signals), they regenerate via
  enrichment on the survivor — do not hand-repoint; just soft-retire the MPI rows.
  Print the address count + rows retired (expect ~33 addresses / ~53 rows).

## Out of scope / already closed
- C1 pursuit-vs-relationship: CLOSED — relationship-level key shipped + verified.
- Do not touch FileSync/PdfToSafe; no new MCP tools.

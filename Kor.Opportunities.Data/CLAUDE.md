# The BD brain — working rules for this module

Loads on top of the root `CLAUDE.md` when you are working in here. Everything below was learned by
breaking it.

## The one thing to understand first

**One canonical org row is supposed to be one real-world company.** Almost every serious defect in
this module is a violation of that sentence, and none of them throw.

2026-09-03: canonical 74300 held **both** a Denver mixed-use developer **and** a Victoria BC
architecture practice. `NormalizeAggressiveKey` strips `partners` / `llc` / `architecture` / `inc`,
so *"Continuum Partners, LLC"* and *"Continuum Architecture Inc"* both collapsed to `continuum`;
`--merge-dba` supplied the post-DBA key; `ChooseSurvivor` preferred Developer (KindRank 2) over
Architect (3). Then a narrative refresh researched the Denver firm, declared the record on file
wrong, and replaced it. Every step reported success.

Read [`docs/codex/CODEX-BD-ENTITY-IDENTITY-AND-REFRESH-AUDIT.md`](../docs/codex/) and its RESPONSE
before changing anything about identity, merging or refresh.

## Gates that already exist — use them, don't reinvent

- **`tools/BdIntegrityCheck`** is the invariant suite. Identity conflation, source freshness,
  dangling org references, duplicate affiliations. **Run it before and after any data change and
  diff the two reports** — that is the no-regression check, and it needs no new tooling.
- **`ResearchIdentityGate`** (in the Worker) refuses to persist a narrative when the researched
  entity's website disagrees with the one on file. Anchor-less orgs are allowed through and get the
  discovered website written back, so they self-heal.
- **`BdCanonicalDedup`** is fail-closed on schema: a new FK to `CanonicalOrg` that is in neither
  `FkTargets` nor `IntelDeleteTargets` blocks **every** merge until you handle it. That is working as
  designed — it caught migrations 289/290 having silently blocked merges for seven weeks.
  ⛔ **Its DEFAULT mode has no similarity gate.** Without `--pairs` it groups by
  `NormalizeAggressiveKey` and commits every group; the fuzzy-name gate and the allowlist only
  guard `--pairs`. The 2026-09-04 dry run proposed re-merging 927758 *Continuum Architecture* into
  74300 *Continuum Partners* — the conflation split by hand the day before. **Only ever commit
  through `--pairs`**, and read `org_aggressive_key_collision` in the integrity report first.
- **`--pairs` has four gates and they are the review.** Name similarity (2026-05-30), plus three
  added 2026-09-04 after a hand review of a 110-pair batch found eleven bad merges the batch's own
  prose rule had missed: **both rows carrying a Deltek id** (two billing entities — never merge,
  not allowlist-overridable), **survivor is a branch row and the loser is not** (the merge is right,
  the direction is backwards — it would have left Prologis called *"Prologis — Vancouver BC (New
  Market Entry)"*), and **names asserting different countries** (*WSP USA* into *WSP Canada Inc.*).
  Rejects go to `output/rejected-pairs.csv` with a reason. ⛔ Do not hand a merge CSV to a human to
  eyeball — if a rule can be stated, it goes in `RunPairsMergeAsync`.

## Rules with scars behind them

- **Resolve orgs by EXACT `DisplayName`, never `LIKE`.** A substring pass made five firms look
  missing: "Chard" matched *Richard & Co. Architecture*, "Seba" matched *Sebastien Garon*.
- **`NormalizedName` is computed; `FuzzyNormalizedName` is not.** Set the fuzzy key explicitly on
  insert or the row gets an empty one and can group with unrelated orgs.
- **The fuzzy normalizer strips `&` rather than folding it to `and`**, so "Perkins and Will" never
  matches "Perkins&Will". That is how duplicate shells get minted.
- **`IntelNarrative` is versioned as of migration 297**, but only history written after that date
  exists. Anything overwritten before it is gone unless it is in a nightly backup.
- **A person moved between orgs can get a duplicate affiliation** — the person resolves correctly,
  the affiliation row does not dedupe. Measure it as `(IntelPersonId, CanonicalOrgId)` groups with
  **2+ ACTIVE rows**: 461 when first measured, **462 groups / 960 active rows on 2026-09-04**
  (356 last written June 2026 — the bulk import — 100 in July, 6 in September). A figure of
  "3,551 groups / 9,330 rows" was quoted the same day; it counted retired rows too, and 2,763 of
  those groups are one live row plus retired predecessors while 326 are wholly retired — churn
  history, not duplicates. `person_duplicate_active_affiliation` in `tools/BdIntegrityCheck` is
  the live count. Quote that, not either headline.
- **Two "same company" heuristics exist**: the dedup fuzzy gate (write time) and
  `SqlBriefDataStore.FindRicherSameBrandCanonicalAsync` (read time). The read path now defers to the
  write path. Do not add a third.

## The relevance gate is usually right

A source that ingests items and keeps none is normally correct, not broken — Island Health's
rejects are *"Island Health Taxi Services"* and *"Mobile Food Services"*, dropped for "no
building/structural/design signal". Check `opportunities.RelevanceGateRejects` before touching the
gate. The useful question when a buyer contributes nothing is **"where does that buyer post its
construction work instead?"** — for the health authorities it is the LMFM prequalified rosters and
Infrastructure BC, not open RSS.

## Do not treat tenders as pipeline

Tenders (BC Bid, Bonfire, bids&tenders, APC, CanadaBuys) tell you what is out for bid **now** — by
then the structural engineer was chosen months ago. The forward pipeline is a different thing, and
BC's provincial source for it was discontinued: see `reference` on the BC Major Projects Inventory
and `docs/KOR-EarlySignal-Ingestion-Design-2026-09-03.md`.

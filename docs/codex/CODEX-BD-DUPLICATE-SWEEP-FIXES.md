# Codex — Close the four leaks the whole-database duplicate sweep found

**DO NOT BUILD. DO NOT RUN TESTS. DO NOT RUN ANY TOOL AGAINST A DATABASE.** Make the edits,
add the tests as source, and ping when applied. Claude builds and runs the suite on the dev box;
Ian runs anything that touches `KOR-APP01`.

## Why this exists

On 2026-09-04 the whole-database duplicate sweep (`docs/BD-Duplicate-Sweep-2026-09-04.md`)
characterised every class of duplicate canonical org it could find and built the check that
fails on every instance at once — the *DUPLICATE CLASSES* section of `tools/BdIntegrityCheck`.
Four of the classes are still being **minted by code**, so merging the rows we have would be
bailing with the tap open. This brief closes the taps. It does not merge anything.

The check that must go green (or move rows from one class into a better-understood one) is
`dotnet run --project tools/BdIntegrityCheck`; the counts below are its 2026-09-04 baseline.

## Fix 1 — the fuzzy key strips a bare `&` instead of folding it

`CanonicalOrgResolver.NormalizeForFuzzyMatch` folds `" & "` (spaced) to `" and "`, then
`NormalizeName` strips a bare `&`. So `Perkins&Will` → `perkinswill` and `Perkins and Will` →
`perkinsandwill`: one firm, two keys, and canonical 271546 was minted exactly that way. Names
joined with `" + "` have the same problem (`hcma architecture + design`). The sweep's
`org_ampersand_fold_collision` shows **6 live groups / 12 orgs** the current key cannot see
(D'Ambrosio, Human Studio, Emily Carr, Proscenium, LPAS, SCB + Henning Larsen).

Change `NormalizeForFuzzyMatch` so every `&` and every spaced `+` becomes `" and "` **before**
the corporate-suffix pass, with any spacing (`\s*&\s*`, `\s\+\s`). Keep the existing spaced-form
line or fold it into the new one — the result must be the same key for `Perkins&Will`,
`Perkins & Will`, `Perkins and Will` and `Perkins + Will`.

Add the four spellings as a theory in `Kor.Opportunities.Data.Tests/CanonicalOrgResolverTests.cs`,
plus one case proving `AT&T` and `AT and T` now share a key (that is accepted; note it in the test
name so nobody "fixes" it back).

Non-obvious: every stored `FuzzyNormalizedName` computed from a name containing `&` or ` + `
changes. **Do not write a migration for that** — `BdCanonicalDedup --backfill-fuzzy-key` already
rewrites every key from the live normalizer and Claude runs it after the deploy. Say so in the
commit message. After the backfill the six groups will appear in `org_fuzzy_key_collision`
instead; that is the intended move, not a regression.

## Fix 2 — control characters survive intake and defeat the strict key

13 live buyers carry a line feed inside `DisplayName` (`Attorney General<LF>Procurement Services
Branch`). The computed `NormalizedName` column strips spaces but not CR/LF/TAB, so nine of them
have a one-line twin the strict key cannot see — 9 of the 15 groups in the dedup tool's own dry
run. Sweep check: `org_name_control_chars`.

In `CanonicalOrgResolver.StripIntakeNoise` / `TidyIntakeName`, collapse any run of whitespace —
including CR, LF, TAB and NBSP — to one space, so no such name can be created again. Add a test
with an embedded `\n`.

For the 13 existing rows write migration **308** in `Kor.Opportunities.Data/Schema/` (the last
applied is 307; `KOR.Drafter/db/` is the other project's folder, not this one). It replaces
CR/LF/TAB in `DisplayName` with a single space **only where the cleaned `NormalizedName` does not
already belong to another live row**. Check first whether `NormalizedName` carries a unique index;
if it does, an unguarded UPDATE fails on the nine twins, and if it does not, the UPDATE would
silently create nine exact duplicates. Either way the nine twins are a **merge**, not a rename,
and go through `BdCanonicalDedup --pairs` in the sweep's batch, not through this migration. The
migration must leave them untouched and say so in a comment.

## Fix 3 — the dedup tool's default mode has no similarity gate, and nothing remembers a split

`BdCanonicalDedup` without `--pairs` groups by `NormalizeAggressiveKey` and, with `--commit`,
merges every group. The fuzzy-name gate and the allowlist (`IsAllowlistedNonSimilar`) only guard
`RunPairsMergeAsync`. On 2026-09-04 the default dry run proposed re-merging **927758 Continuum
Architecture Inc into 74300 Continuum Partners, LLC** — the conflation Ian had split by hand the
day before. Sweep check: `org_aggressive_key_collision`, which labels that group
*aggressive-only, CROSS-KIND*.

Two changes in `tools/BdCanonicalDedup/Program.cs`:

1. **Apply the same gate to the default path.** In the commit loop over `BuildGroups` output,
   refuse (log and skip, counted as failed) any group whose members do not all share a
   `NormalizeForFuzzyMatch` key unless every loser→survivor pair is allowlisted. Mirror the
   `[REJECT]` line and `AppendRejectedPair` the pairs path already uses so the two paths report
   the same shape.
2. **A never-merge list.** Add `dedup-never-merge.csv` beside `dedup-non-similar-allowlist.csv`,
   same loader shape (`LoserId,SurvivorId,Reason`, `#` comments, copied to output like the
   allowlist `Content` items in the csproj), consulted by **both** paths before any pair is
   planned or committed, in either direction. Seed it with
   `927758,74300,Continuum Architecture (Victoria) split from Continuum Partners (Denver) 2026-09-03`.
   The split ledger is the thing that was missing: `CanonicalOrgMerge` records what was joined
   and nothing records what was deliberately taken apart.

Non-obvious: the frozen-anchor skip in `BuildGroups` (two `KorClient`/`KorStructural` rows in
one group) must stay exactly as it is — that is a separate, correct refusal.

## Fix 4 — there is no supported way to create a canonical org by hand

The Island who's-who session created 15+ canonical rows on 2026-09-04 by direct SQL. Their
`FuzzyNormalizedName` was hand-computed as the strict key (`hutchinsoncontractingltd` — the
suffix not stripped) or left empty, so the write-time gate could not see them, and four of them
duplicated rows that already existed: Sense Engineering (20284 ↔ 927808), CitySpaces (4905 ↔
927792), Christine Lintott (70603 ↔ 927759), Seba Construction (927761 ↔ 927807). Sweep checks:
`org_fuzzy_key_stale` (19, 15 of them 927xxx) and `org_fuzzy_key_collision` (4 groups).

Add a `--create` verb to `BdCanonicalDedup` (or a sibling `tools/` console if the dedup tool is
the wrong home — argue it in the commit message, not here) that takes `--kind`, `--name` and
optional `--website`, and goes through `CanonicalOrgResolver.ResolveAsync` with `allowCreate`,
so it **attaches to an existing row when the resolver finds one and creates otherwise**, with
both `FuzzyNormalizedName` and `WebsiteDomain` set by the same code the ingestion uses. Print
which happened and the Id. Document it in `Kor.Opportunities.Data/CLAUDE.md` under the rule that
already says the fuzzy key is not computed — the rule should now end with "use `--create`".

## Out of scope — do not do these

- Merging anything, running `--backfill-fuzzy-key`, or applying migration 308. Those are Ian's.
- Changing `NormalizeAggressiveKey`'s strip list. The sweep tolerates it now that Fix 3 gates it.
- Touching `SqlBriefDataStore.FindRicherSameBrandCanonicalAsync` — it defers to the write path
  already and must not become a third heuristic.
- Any edit to the DUPLICATE CLASSES section of `tools/BdIntegrityCheck` except updating the
  ACCEPTANCE lines if a fix changes which check a known instance lands in.

Ping when applied, with the list of files touched.

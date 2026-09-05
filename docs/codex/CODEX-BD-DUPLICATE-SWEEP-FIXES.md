# Codex — Close the four leaks the whole-database duplicate sweep found

**DO NOT BUILD. DO NOT RUN TESTS. DO NOT RUN ANY TOOL AGAINST A DATABASE.** Make the edits,
add the tests as source, and ping when applied. Claude builds and runs the suite on the dev box;
Ian runs anything that touches `KOR-APP01`.

## Why this exists

On 2026-09-04 the whole-database duplicate sweep (`docs/BD-Duplicate-Sweep-2026-09-04.md`)
characterised every class of duplicate canonical org it could find, built the check that fails on
every instance at once — the *DUPLICATE CLASSES* section of `tools/BdIntegrityCheck` — and then
merged 130 rows through the gated `--pairs` path. Four of the classes are still being **minted by
code**, so the table will silt up again. This brief closes the taps. It does not merge anything.

The check that must go green, or move rows from one class into a better-understood one, is
`dotnet run --project tools/BdIntegrityCheck`. The counts below are from its run after the merges
(report stamp 20260905-040741, kept in `docs/bd-duplicate-sweep-2026-09-04/`).

## Fix 1 — the fuzzy key strips a bare `&` instead of folding it

`CanonicalOrgResolver.NormalizeForFuzzyMatch` folds `" & "` (spaced) to `" and "`, then
`NormalizeName` strips a bare `&`. So `Perkins&Will` → `perkinswill` and `Perkins and Will` →
`perkinsandwill`: one firm, two keys, and canonical 271546 was minted exactly that way. Names
joined with `" + "` have the same problem (`hcma architecture + design`). The sweep's
`org_ampersand_fold_collision` still shows **5 live groups / 10 orgs** the current key cannot see:
Human Studio, Emily Carr University, Proscenium, LPAS, SCB + Henning Larsen.

Change `NormalizeForFuzzyMatch` so every `&` and every spaced `+` becomes `" and "` **before**
the corporate-suffix pass, with any spacing (`\s*&\s*`, `\s\+\s`). Keep the existing spaced-form
line or fold it into the new one — the result must be the same key for `Perkins&Will`,
`Perkins & Will`, `Perkins and Will` and `Perkins + Will`.

Add the four spellings as a theory in `Kor.Opportunities.Data.Tests/CanonicalOrgResolverTests.cs`,
plus one case proving `AT&T` and `AT and T` now share a key (that is accepted; note it in the test
name so nobody "fixes" it back).

Non-obvious: every stored `FuzzyNormalizedName` computed from a name containing `&` or ` + `
changes. **Do not write a migration for that, and do not point anyone at `--backfill-fuzzy-key`**
— it rewrites all 893k rows and silently undoes the deliberate `mcw` override on 71528. After the
deploy, `org_fuzzy_key_stale` will list exactly the affected rows with an `ExpectedFuzzy` column;
Claude repairs those rows from that column, skipping 71528, the way
`docs/bd-duplicate-sweep-2026-09-04/fix-stale-fuzzy-keys-2026-09-04.sql` did. Say so in the
commit message. The five groups then surface in `org_fuzzy_key_collision`; that is the intended
move, not a regression.

## Fix 2 — control characters survive intake and defeat the strict key

Buyers arrive with a line feed inside `DisplayName` (`Attorney General<LF>Procurement Services
Branch`). The computed `NormalizedName` column strips spaces but not CR/LF/TAB, so such a row can
never match its one-line twin and every intake mints another; nine twins were merged on
2026-09-04 and **4 rows remain** (75103, 272014, 473331, 902252), none with a live twin. Sweep
check: `org_name_control_chars`.

In `CanonicalOrgResolver.StripIntakeNoise` / `TidyIntakeName`, collapse any run of whitespace —
including CR, LF, TAB and NBSP — to one space, so no such name can be created again. Add a test
with an embedded `\n`.

For the existing rows write migration **309** in `Kor.Opportunities.Data/Schema/` (308 is taken
by the mid-Island sources; `KOR.Drafter/db/` is the other project's folder, not this one). It
replaces CR/LF/TAB in `DisplayName` with a single space **only where the cleaned `NormalizedName`
does not already belong to another live row** — `UX_CanonicalOrg_LiveNormalizedName` is a unique
index, so an unguarded UPDATE fails on any twin that has appeared since. A row it has to skip is a
merge, not a rename, and goes through `BdCanonicalDedup --pairs`; the migration must leave it
untouched and say so in a comment.

## Fix 3 — the dedup tool's default mode has no similarity gate, and nothing remembers a split

`BdCanonicalDedup` without `--pairs` groups by `NormalizeAggressiveKey` and, with `--commit`,
merges every group. All four gates — name similarity plus the three added in commit `b37c8422`
(both rows Deltek-anchored, branch-into-parent direction reversed, names asserting different
countries) — live in `RunPairsMergeAsync` and guard only `--pairs`. On 2026-09-04 the default dry
run proposed re-merging **927758 Continuum Architecture Inc into 74300 Continuum Partners, LLC**
— the conflation Ian had split by hand the day before. It still would: `org_aggressive_key_collision`
lists that group as *aggressive-only, CROSS-KIND* today, next to one honest twin (812 / 689488,
Ministry of Forests Southern Engineering, differing by an em dash).

Two changes in `tools/BdCanonicalDedup/Program.cs`:

1. **Apply the same four gates to the default path.** Pull the per-pair gate sequence out of
   `RunPairsMergeAsync` into one function both paths call, and in the commit loop over
   `BuildGroups` output refuse (log and skip, counted as failed) any group in which a
   loser→survivor pair fails a gate. Same `[REJECT]` line, same `AppendRejectedPair`, so the two
   paths report the same shape.
2. **A never-merge list.** Add `dedup-never-merge.csv` beside `dedup-non-similar-allowlist.csv`,
   same loader shape (`LoserId,SurvivorId,Reason`, `#` comments, copied to output like the
   allowlist `Content` items in the csproj), consulted by **both** paths before any pair is
   planned or committed, in either direction. Seed it with
   `927758,74300,Continuum Architecture (Victoria) split from Continuum Partners (Denver) 2026-09-03`.
   The split ledger is the thing that was missing: `CanonicalOrgMerge` records what was joined
   and nothing records what was deliberately taken apart.

Non-obvious: the frozen-anchor skip in `BuildGroups` (two `KorClient`/`KorStructural` rows in
one group) must stay exactly as it is — that is a separate, correct refusal. And the README beside
the tool already documents the four gates and this hazard; update its warning paragraph when the
default path is gated, do not duplicate it.

## Fix 4 — there is no supported way to create a canonical org by hand

Migration 307 (the Island who's-who) inserted 41 canonical rows by SQL. Their
`FuzzyNormalizedName` was hand-computed as the strict key (`hutchinsoncontractingltd` — the
suffix not stripped) or left empty, so the write-time gate could not see them, and four of them
duplicated rows that already existed: Sense Engineering, CitySpaces, Christine Lintott, Seba
Construction. Those four were merged and the 16 bad keys repaired on 2026-09-04
(`org_fuzzy_key_stale` is down to the one deliberate override). The leak is the method, and it
will be used again.

Add a `--create` verb to `BdCanonicalDedup` (or a sibling `tools/` console if the dedup tool is
the wrong home — argue it in the commit message, not here) that takes `--kind`, `--name` and
optional `--website`, and goes through `CanonicalOrgResolver.ResolveAsync` with `allowCreate`,
so it **attaches to an existing row when the resolver finds one and creates otherwise**, with
both `FuzzyNormalizedName` and `WebsiteDomain` set by the same code the ingestion uses. Print
which happened and the Id. Document it in `Kor.Opportunities.Data/CLAUDE.md` under the rule that
already says the fuzzy key is not computed — the rule should now end with "use `--create`".

## Out of scope — do not do these

- Merging anything, repairing keys, or applying migration 309. Those are Ian's.
- Changing `NormalizeAggressiveKey`'s strip list. The sweep tolerates it once Fix 3 gates it.
- Touching `SqlBriefDataStore.FindRicherSameBrandCanonicalAsync` — it defers to the write path
  already and must not become a third heuristic.
- Any edit to the DUPLICATE CLASSES section of `tools/BdIntegrityCheck` except updating the
  ACCEPTANCE lines if a fix changes which check a known instance lands in.

Ping when applied, with the list of files touched.

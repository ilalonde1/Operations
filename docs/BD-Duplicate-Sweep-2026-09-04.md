# BD duplicate sweep — 2026-09-04

Run from `docs/BD-Duplicate-Sweep-Prompt-2026-09-04.md`. **Nothing was merged and nothing was
written to the database.** The deliverables are a check that fails on every instance of each
duplicate class at once, the before/after integrity reports, and reviewed merge batches as
dry-run output for Ian.

Evidence folder: `docs/bd-duplicate-sweep-2026-09-04/` — both integrity reports, one CSV per
class with its full population, the dedup tool's default dry run, and the batch dry runs.

## 1. The counts, reproduced

Every figure in the prompt was re-measured against `KOR-APP01\SQLEXPRESS` before anything else.

| Measure | Prompt | Measured | Note |
|---|---|---|---|
| Live canonical orgs | 9,734 | 9,734 | |
| …with a `WebsiteDomain` | 2,530 | 2,530 | 26 %; 7,204 rows have no anchor at all |
| …Website but no domain | 7 | 7 | |
| Same-domain groups / orgs | 327 / 845 | 327 / 845 | one "group" is the literal domain `'null'` |
| …outside gov/edu umbrellas | 306 / 721 | 306 / **749** | same groups, the prompt's org count was off |
| `WebsiteDomain = 'null'` | 11 | 11 | |
| Same fuzzy key, different Id | 2 / 4 | 2 / 4 | 4 / 8 once the key is *recomputed* (see §4) |
| Empty fuzzy key | 5 | 5 | |
| Names with `&` or ` and ` | 796 | 796 | |
| Duplicate affiliation groups | **3,551 / 9,330** | 3,551 / 9,330 | **counts retired rows** |
| …with 2+ **active** rows | — | **462 / 960** | the live defect; 356 last written June, 100 July, 6 Sept |

The affiliation headline was wrong in kind, not in arithmetic: 2,763 of the 3,551 groups are one
live row plus retired predecessors and 326 are wholly retired — churn history. The 461 in
`Kor.Opportunities.Data/CLAUDE.md` was the active count all along (it is 462 now). That file is
corrected; quote `person_duplicate_active_affiliation` from the integrity report, not either
headline.

## 2. The classes, one sentence each

1. **Same domain, different names.** One real firm is held as several rows that share a
   `WebsiteDomain` — regional studios, legal-entity variants, an initialism against its own
   words — and every dossier sees a fraction of what we hold. 327 groups.
2. **Umbrella domains are not duplicates.** A public body's domain is shared by its departments,
   ministries and campuses, which are correctly separate rows; a sweep that cannot tell an
   umbrella from a shell destroys the government hierarchy. 94 of the 327.
3. **Wrong anchor on a public domain.** A commercial org carries the domain of the public body
   whose project page mentioned it — not a duplicate, a wrong anchor that `ResearchIdentityGate`
   will now defend as truth. 68 rows, most of them invisible to a same-domain check because they
   sit alone on their domain.
4. **Name variants the write-time key cannot fold.** Two spellings of one firm produce different
   `FuzzyNormalizedName`s — a bare `&` against ` and `, ` + ` against either, a stripped corporate
   suffix, a region tag, an embedded line feed — so the gate that should have attached the second
   reference created a row instead. 6 ampersand groups, 13 line-feed names, 120 same-Kind prefix
   pairs.
5. **Stored key disagrees with the normalizer.** A row whose `FuzzyNormalizedName` is empty or
   hand-computed is invisible to the gate, and the next reference to that firm mints a twin. 19
   rows, 15 of them inserted by hand on 2026-09-04; 4 twins already exist.
6. **Over-aggressive key.** `NormalizeAggressiveKey` strips inc/ltd/co/architects/partners/group,
   so unrelated firms collapse to one stem and the dedup tool's default mode, which has no
   similarity gate, merges them. 15 groups today, two of them cross-Kind — including the Continuum
   pair split by hand the day before.
7. **Duplicate active affiliations.** The same person is affiliated to the same org by more than
   one active row; a retired predecessor is churn, not a duplicate. 462 groups.

## 3. The check

The DUPLICATE CLASSES section of `tools/BdIntegrityCheck` (extended, not a new tool — it is the
invariant suite the data CLAUDE.md names). It loads the live org table once and runs the
platform's **own** normalizers over it — `NormalizeForFuzzyMatch`, `NormalizeAggressiveKey`,
`ExtractWebsiteDomain` — because a SQL re-implementation would be a third same-company heuristic.
Every check writes its full population to `<out>/<key>-<stamp>.csv`; the report shows a sample,
the count line is the claim.

| Key | Sev | Count | What it is |
|---|---|---|---|
| `org_name_control_chars` | WARN | 13 | CR/LF/TAB inside a name |
| `org_fuzzy_key_stale` | WARN | 19 | stored fuzzy key ≠ normalizer (empty counts) |
| `org_website_anchor_malformed` | WARN | 25 | `'null'`, half-anchored, domain ≠ website |
| `org_fuzzy_key_collision` | WARN | 4 g / 8 | the gate's own key, recomputed, collides |
| `org_ampersand_fold_collision` | WARN | 6 g / 12 | differential: same normalizer, `&`/`+` folded first |
| `org_aggressive_key_collision` | WARN | 15 g / 30 | what `--commit` would merge today, classified |
| `org_same_domain_umbrella` | INFO | 94 g / 286 | not a defect; counted so the exclusion is visible |
| `org_same_domain_shell_brand_match` | WARN | 85 g / 199 | S1: every name carries the domain's brand |
| `org_same_domain_shell_review` | WARN | 147 g / 349 | R1a 23, R1b 45, R2 11, S2 35, R3 33 groups |
| `org_commercial_on_public_domain` | WARN | 68 | the wrong-anchor class, singletons included |
| `org_name_prefix_same_kind` | WARN | 120 | whole-word prefix, same Kind, commercial |

The same-domain tiers were sized in SQL **before** the code was written and the code reproduces
them exactly (U 94, S1 85, review 147). The report carries its own coverage statement: what it
covers, what it does not (retired rows; typos and renames on the 74 % of rows with no domain;
whether two same-domain rows are subsidiaries a BD user wants apart), one same-class fault it
would not catch (RJC before its merge lands in S2, not S1 — `readjoneschristoffersen` does not
contain `rjc`), and three acceptance instances that were verified on the first run: Continuum
74300/927758 as *aggressive-only CROSS-KIND*, `stantec.com` heading S1, Sense Engineering
20284/927808 as a fuzzy collision.

**Before/after.** `integrity-report-BEFORE-20260904-204736.txt` against
`integrity-report-AFTER-20260904-211740.txt`: every SQL invariant above the new section has the
same count; the only line-level differences are live drift (a Bonfire run count, the order of an
unordered TOP 5 sample). Errors 1 before and after — the pre-existing `BIDSTEND-26-067` key
collision, not identity.

## 4. What it found that the prompt did not know

- **The dedup tool's default mode would undo the Continuum split.** Without `--pairs` it groups
  by the aggressive key and commits every group; the similarity gate and the allowlist only guard
  `--pairs`. Its 2026-09-04 dry run (`bdcanonicaldedup-default-dryrun-plan-20260904.csv`)
  proposes `927758 → 74300`. Recorded in the data CLAUDE.md; Codex fix 3 below.
- **The Island who's-who session minted duplicates the same day.** 15 rows in the 927xxx range
  were inserted by SQL with a hand-computed key (`hutchinsoncontractingltd` — the strict key,
  suffix not stripped) or none. Four duplicate existing rows: Sense Engineering 20284 ↔ 927808,
  CitySpaces 4905 ↔ 927792, Christine Lintott 70603 ↔ 927759, Seba 927761 ↔ 927807. There is no
  supported way to create a canonical org by hand; Codex fix 4.
- **Control characters.** 9 of the dedup tool's 15 groups are `Ministry of X<LF>Branch` next to
  the one-line spelling. The computed `NormalizedName` strips spaces, not line feeds.
- **Wrong anchors have a pattern.** Of the 68, the 39xxx rows are procurement vendors carrying the
  *buyer's* municipal domain (Canadian Hearing Services on parksville.ca) and the 54xxx/55xxx
  rows are project proponents carrying the municipality's or a US city's `.gov` domain (Dokie Wind
  Energy on rdos.bc.ca, Villa Capri Enterprises on kingcounty.gov). Cause not traced in code this
  session; the shape says the org inherited the source page's website.
- **`org_fuzzy_key_stale` includes one deliberate override.** 71528 MCW Group of Companies stores
  `mcw`, not `mcwgroupofcompanies`. `--backfill-fuzzy-key` would silently undo that; check before
  running it, and know that it rewrites all 893k rows, retired included.
- **31,636 active `BuildingPermit.ApplicantCanonicalOrgId` rows point at retired orgs** (Owner
  31,632, Contractor 16,085). Not duplicates and out of scope here, but it is the largest stale-link
  population in the report and the Island permit work reads through it.

## 5. Proposed batches — Ian decides, nothing has run

### Batch 0 — hygiene, no merge, one UPDATE each (not run)

```sql
-- 0a  the literal string 'null' as an anchor (11 rows, all Buyer)
UPDATE opportunities.CanonicalOrg SET Website = NULL, WebsiteDomain = NULL
WHERE RetiredAtUtc IS NULL AND LOWER(WebsiteDomain) = 'null' AND (Website IS NULL OR LOWER(Website) = 'null');

-- 0b  half-anchored: Website set, WebsiteDomain empty (7 rows; domain = ExtractWebsiteDomain(Website))
UPDATE opportunities.CanonicalOrg SET WebsiteDomain = 'continuumpartners.com'   WHERE Id = 74300  AND WebsiteDomain IS NULL;
UPDATE opportunities.CanonicalOrg SET WebsiteDomain = 'dialogdesign.ca'         WHERE Id = 109620 AND WebsiteDomain IS NULL;
UPDATE opportunities.CanonicalOrg SET WebsiteDomain = 'kasian.com'              WHERE Id = 113392 AND WebsiteDomain IS NULL;
UPDATE opportunities.CanonicalOrg SET WebsiteDomain = 'hksinc.com'              WHERE Id = 271662 AND WebsiteDomain IS NULL;
UPDATE opportunities.CanonicalOrg SET WebsiteDomain = 'namdargroup.com'         WHERE Id = 658682 AND WebsiteDomain IS NULL;
UPDATE opportunities.CanonicalOrg SET WebsiteDomain = 'lintottarchitect.ca'     WHERE Id = 927759 AND WebsiteDomain IS NULL;
UPDATE opportunities.CanonicalOrg SET WebsiteDomain = 'knappettindustries.com'  WHERE Id = 927760 AND WebsiteDomain IS NULL;
-- note: after 0b, Kasian 113392 joins kasian.com with 70543 and DIALOG BC 109620 joins dialogdesign.ca
-- (already 4 rows) — both become S1/S2 rows on the next report, which is the point.

-- 0c  domain set, Website empty (5 rows): give the row a Website so the anchor reads both ways;
--     54107 Newmark Group @ www2.gov.bc.ca is a wrong anchor and belongs to 0e instead.
UPDATE opportunities.CanonicalOrg SET Website = 'https://' + WebsiteDomain + '/'
WHERE Id IN (71528, 504241, 927798, 927800, 927803) AND (Website IS NULL OR Website = '');

-- 0d  71236 Highstreet Ventures: Website buyhighstreet.ca, domain gohighstreet.ca. A person picks.

-- 0e  wrong anchors (org_commercial_on_public_domain, 68 rows). Clear the domain on the 57 that
--     are plainly wrong and let the next research write the right one back. KEEP the six that
--     are a body's own development arm on its parent's domain (54997 Petroglyph, 54986 Takaya,
--     54999 Yos, 55001 Tsuma-as, 38947 La Caisse/CDPQ, 53741 NOVA Gas); re-Kind 72211 PHSA and
--     54955 BC Ministry of Health to Buyer instead (they are public bodies mis-Kinded 'Client');
--     retire the three placeholders 68721 'owner (confirm)', 68728 'developer (confirm)',
--     77820 'Lake Country … (unnamed developer)'.
UPDATE opportunities.CanonicalOrg SET Website = NULL, WebsiteDomain = NULL
WHERE RetiredAtUtc IS NULL AND Id IN (SELECT Id FROM <the 68-row CSV>)
  AND Id NOT IN (54997, 54986, 54999, 55001, 38947, 53741, 72211, 54955, 68721, 68728, 77820);

-- 0f  the four line-feed names with NO one-line twin (75103, 272014, 473331, 902252)
UPDATE opportunities.CanonicalOrg
SET DisplayName = REPLACE(REPLACE(REPLACE(DisplayName, CHAR(13), ' '), CHAR(10), ' '), CHAR(9), ' ')
WHERE Id IN (75103, 272014, 473331, 902252);

-- 0g  stale fuzzy keys (19): BdCanonicalDedup --backfill-fuzzy-key, AFTER deciding 71528 'mcw'.
```

### Batch 1 — same-domain shells, brand-matched, Kind-compatible (dry-run validated)

Files, in the repo's own convention for a reviewed merge (the RJC pair is the model):

- `tools/BdCanonicalDedup/shell-brand-match-merge-2026-09-04.csv` — **110 pairs, 83 groups**
- `tools/BdCanonicalDedup/dedup-non-similar-allowlist.d/shell-brand-match-2026-09-04.csv` — the
  same pairs with a reason each and an advisory `RenameSurvivorTo` column
- `tools/BdCanonicalDedup/control-char-twins-merge-2026-09-04.csv` and its allowlist — the
  **9 line-feed twins**, loser = the `<LF>` row (0 intel in every case)

Rule for inclusion, stated so it can be argued with: tier S1 (same domain, no public-sector
member, no JV-shaped name, every name carries the domain's brand label) **and** the Kinds are one
business line — same Kind, or the survivor is the frozen KOR client anchor, or the loser is a
Client/Unknown/Vendor/Subcontractor, or both are Competitor/Architect. Survivor = frozen Kind,
else Deltek/Clendor anchor, else richest by people + awards + narratives + KOR jobs, else lowest
Id; the tool upgrades the survivor's Kind to the best in the pair on commit.

Dry run (`batch1-pairs-dryrun-stdout-20260904.txt`): **110 `[DRY-RUN]` lines, 0 rejected, 0
failed** — every pair resolves to live rows and clears the similarity gate through the allowlist.
The nine twins likewise, 9 / 0 / 0.

Held back into `docs/bd-duplicate-sweep-2026-09-04/batch2-held-back-2026-09-04.csv` (4 pairs):
AECOM Hunt (GC) → AECOM Canada (Competitor) and the two Skanska USA construction offices → Skanska
USA (Developer), because a BD user may want the GC and the design/development roles kept apart;
and Townline Homes Inc. → Townline Group of Companies, **both KorClient — two Deltek billing
entities, never merged**.

Names to clean after the merge (advisory column, 16 rows): where a loser held the plain brand and
the survivor a region or parenthetical tag, e.g. `DCI Engineers — Seattle` → `DCI Engineers`,
`Prologis — Vancouver BC (New Market Entry)` → `Prologis`. Never suggested for a Deltek-anchored
survivor. Two survivors need a hand rename the rule cannot see: 167 `Onni Contracting
(California), Inc.` absorbing Onni Group, and 75698 `Truman (Truman Homes)`.

### Batch 2 — needs a person, one list per reason

- **Fuzzy-key collisions (4)** — duplicates the platform can prove; the survivor's Kind needs a
  look (CitySpaces is a planning consultancy, not an Architect). Merge via `--pairs`.
- **Ampersand fold (6)** — D'Ambrosio, Human Studio, Emily Carr, Proscenium, LPAS, SCB + Henning
  Larsen. Merge via `--pairs` with an allowlist reason, or wait for Codex fix 1 and let them
  surface as fuzzy collisions.
- **S2, the initialism shape (35 groups / 79 orgs)** — Intracorp ×3, Omicron, WestStone,
  Balfour Beatty ×4, RNT, Westbank ×3, Concert, Bohlin Cywinski Jackson, Lincoln Property Company,
  MMP, JCK, Tahltan. Most are true duplicates; each needs the same read RJC got. **Exclude**
  Domus Homes / Domus Projects and Frame Properties / OctoberNine Capital — frozen KorClient
  pairs.
- **R1a, mis-Kinded twins of a public body (23 groups)** — BC Hydro, YVR, NAIT, Port of
  Vancouver, Medicine Hat, Royal BC Museum, CMLC, and the First Nations whose development corp
  and band sit on one domain (those last are subsidiaries: keep, re-Kind at most).
- **Prefix pairs (120)** — high-value duplicates hiding here: McElhanney 75649 ↔ 272396 (234 and
  205 intel split), Kasian 113392 ↔ 70543, Group2 271551 ↔ 8538, Glotman Simpson, Swinerton ×5,
  Gensler Canada, a **third** Sense Engineering row 240237 (Alberta). Anchor the short row with a
  website first and it becomes an S1 row on the next report.
- **Duplicate active affiliations (462 groups / 960 rows)** — keep the earliest active row per
  (person, org), retire the rest with a dated reason. Not an org merge; a separate small job.

### Never merge

Umbrella groups (94), R1b wrong anchors or subsidiaries (45 groups), R2 JV-shaped rows (EllisDon
Kinetic, Bogdonov Pao / CIMA+), any pair with a frozen loser, and any cross-Kind aggressive-only
group — that last one is the Continuum shape and there are two live (Continuum, Seba).

## 6. Fixes that want code

`docs/codex/CODEX-BD-DUPLICATE-SWEEP-FIXES.md` — four taps still open: fold `&`/`+` in the
fuzzy key, strip control characters at intake plus migration 308 for the 13 rows, a similarity
gate and a never-merge ledger on the dedup tool's default path (seeded with the Continuum split),
and a `--create` verb so nobody inserts a canonical org by SQL again. Ian runs Codex.

## 7. Files

Changed: `tools/BdIntegrityCheck/Program.cs`, `tools/BdIntegrityCheck/BdIntegrityCheck.csproj`
(project reference to `Kor.Opportunities.Data`), `Kor.Opportunities.Data/CLAUDE.md` (affiliation
figure; default-mode warning), `docs/BD-Duplicate-Sweep-Prompt-2026-09-04.md` (result pointer).
Added: this file, the evidence folder, the four batch CSVs, the Codex brief. Build is clean under
warnings-as-errors. Not committed.

## 8. Definition of done

- A check that fails on every instance of a named class: **yes, eleven of them**, in the
  invariant suite, with a coverage statement and verified acceptance instances.
- Integrity report clean of a class, or remaining rows allowlisted with a reason: **not yet** —
  no data was changed; batches 0 and 1 are the path, and 1 is dry-run validated with every pair
  carrying its reason in the allowlist file.
- `Kor.Opportunities.Data/CLAUDE.md` carries the corrected affiliation figure: **yes**.
- Nothing merged that Ian did not see first: **nothing merged at all**.

---

## 9. Review of batch 1 — 2026-09-04, after the sweep

Ian's instruction on being handed the batch: *"I don't want to manually go through csvs!!!!"*
That is the right call, and it exposed a defect in section 5's handover rather than in the sweep.

All 110 pairs were read as names, Kinds, Deltek anchors and intel weights — not as ids.
**Eleven are wrong, and all eleven are mechanical**, so they are now refused by the tool instead
of being asked of a reader. Batch 1's own "Never merge" note had already caught Townline's two
Deltek rows by hand; the same shape occurs five times in batch 1 and was let through, which is the
proof that the eye is the wrong instrument here.

Three gates were added to `RunPairsMergeAsync`, beside the existing similarity gate and rejecting
to the same `rejected-pairs.csv`:

| Gate | Rejects | Why |
|---|---|---|
| **Both rows carry a Deltek id** | 5 | Two entities we invoice separately. Merging destroys the split and the loser's id is gone. Not allowlist-overridable — decide which billing entity is real in Deltek first. |
| **Survivor is a branch row, loser is not** | 5 | A merge retires the loser's name. These leave the global REIT called *"Prologis — Vancouver BC (New Market Entry)"* and all 25 of DCI's people under *"DCI Engineers — Seattle"*. Right merge, backwards direction. |
| **Names assert different countries** | 1 | *WSP USA Buildings* into *WSP Canada Inc.* is the `canada.ca` mistake in miniature: one brand, two real legal entities. |

The eleven, verified by running the **unmodified** batch file through the rebuilt tool:

- **Both Deltek** — Amacon (CL00653 → CL00009, and the loser has 5 KOR jobs to the survivor's 1),
  Bucci (76D8169F → CL00059), Cressey (7091c33a → CL00099, 117 jobs), Greystar (f84e05e9 →
  25F5A4E6), Tridecca (CL00604 → b01baade).
- **Branch survivor** — DCI Engineers → DCI Engineers — Seattle · SGH → SGH — San Diego ·
  Lendlease → Lendlease (Americas) · Onni Group → Onni Contracting (California) · Prologis →
  Prologis — Vancouver BC (New Market Entry).
- **Cross-border** — WSP USA Buildings → WSP Canada Inc.

**The remaining 99 are correct and were read individually.** They are regional-office shells of one
firm on one corporate domain — *Gensler San Diego* → *Gensler*, *Hensel Phelps — Seattle* →
*Hensel Phelps Construction Co.*, *KPFF Portland* → *KPFF*, and so on.

### What these gates do NOT catch — stated so nobody reads the name and stops looking

- **Branch into branch, where no parent row exists.** `76850 Sundt Construction (California) →
  77205 Sundt Construction San Diego` passes, because both names carry a qualifier and the gate
  only fires when the survivor has one and the loser does not. The merge is right; the surviving
  row then needs renaming to *Sundt Construction*. The loser holds 0 people, so this is cosmetic.
- **`BranchQualifiers` is a literal list, not a place-name test.** A branch in a city not on the
  list is invisible to the gate. Add to the list when a real merge is refused for the wrong reason,
  and expect to add to it.
- **The Deltek gate is blind to a shared billing entity under two ids.** It refuses the merge; it
  cannot tell you which id Deltek considers current.
- **None of the three gates says anything about whether two rows are the same company.** That is
  still the similarity gate's job and still the allowlist's job. These only catch pairs that are
  the same company and would still merge *wrongly*.

### What Ian actually has to do

Nothing with a CSV. `--pairs` on the unmodified file rejects the eleven and merges the ninety-nine:

    dotnet run --project tools/BdCanonicalDedup -- --pairs tools/BdCanonicalDedup/shell-brand-match-merge-2026-09-04.csv --commit

The eleven land in `tools/BdCanonicalDedup/output/rejected-pairs.csv` with a reason each, and the
five branch-survivor pairs can be resubmitted flipped once someone decides the survivor's name.

---

## 10. Executed — 2026-09-04, on Ian's instruction ("You run it")

**109 merges committed.** Live `CanonicalOrg` rows: **9,734 → 9,625.**

| Batch | Proposed | Merged | Refused by a gate |
|---|---|---|---|
| Batch 1 — same-domain brand-matched shells | 110 | **99** | 11 |
| Batch 1b — control-character twins | 9 | **9** | 0 |
| Sundt cleanup (found during the run) | 1 | **1** | 0 |

The eleven refusals are the eleven from section 9; both rows of each are still live and untouched.
Batch 1 was run twice — the first pass was killed at a two-minute timeout after 79 groups. Each
group commits in its own transaction, so the re-run simply skipped the 79 as `org row not found`
and finished the remaining 20. That is the design working; it is not a partial-write hazard.

### Integrity report, before against after

Nothing increased. Every movement is a decrease:

| Check | Before | After |
|---|---|---|
| `org_same_domain_shell_brand_match` | 85 groups | **14** |
| `org_same_domain_different_names` | 845 orgs | **675** |
| `org_aggressive_key_collision` | 15 | **6** |
| `org_name_control_chars` | 13 | **4** |
| `org_name_prefix_same_kind` | 120 | **113** |
| `org_conflated_people_domains` | 93 | 92 |
| `org_thin_unsafe_redirect` | 77 | 75 |
| `person_duplicate_active_affiliation` | 462 | **458** |
| Errors | 1 | 1 |

The single ERROR is unchanged and unrelated: the known `BIDSTEND` OpportunityKey prefix collision.
Reports kept as `integrity-BEFORE-commit-20260904.txt` and `integrity-AFTER-commit-20260904.txt`.

The 14 groups still reported by `org_same_domain_shell_brand_match` are the eleven refusals plus
Townline (already on the never-merge list) and AECOM Hunt → AECOM Canada Ltd. They are real
duplicates that need a decision about direction, not a merge.

### ⚠ Two things this run established that the prompt got wrong

**1. `BdCanonicalDedup` DELETES the loser row. It does not retire it.** The seed prompt asserted
"archive, never delete — retire the loser and record the merge in `CanonicalOrgMerge`". Only the
second half is true. Checked across the whole ledger: **306 of 306 merged loser rows are gone**
(145 in June, 64 in July, 97 in September), so this is long-standing behaviour, not new. The ledger
keeps `MergedFromCanonicalOrgId → MergedIntoCanonicalOrgId`, so any external reference still
resolves; what is lost is the loser row's own `DisplayName`, domain and notes.
Recovery path: the nightly full backup of `KorOpportunitiesDb` finished **2026-09-03 17:01**, which
predates every merge today. Recovery model is SIMPLE, so there is no point-in-time restore.

**2. The domain-anchor blind spot is not theoretical.** Renaming the Sundt survivor to
"Sundt Construction" failed on `UX_CanonicalOrg_LiveNormalizedName`, which is how row **75158** was
found: a plain "Sundt Construction" with four people and **no `WebsiteDomain`**. It was invisible to
every same-domain check in the suite. 74% of `CanonicalOrg` is in that state. Merged the branch row
into it (gate-clean direction, allowlisted in `sundt-2026-09-04.csv`) and set the anchor by hand —
one row, `sundt.com`, eight people.

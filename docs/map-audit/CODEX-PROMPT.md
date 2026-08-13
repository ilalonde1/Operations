# Codex brief — adversarial audit of the KOR project-map pipeline

## Goal

Find the defects that survive in `docs/map-audit/`. This code decides what
KOR Structural claims to have built, on a public website and inside signed fee
proposals sent to named clients. A wrong pin is a false statement about our
work. Assume defects are present and go looking for them.

Read `docs/map-audit/README.md` first — it describes the pipeline, and lists
eight defects already found and fixed. Those are context, not the assignment.
The assignment is what is still there.

## Scope

- `live-theme/functions.php` — `kor_regenerate_map_geojson()` and the
  `kor_map_*` helpers. This is exported from the live WordPress theme; it is not
  in version control anywhere else.
- `live-theme/custom-mapbox.js` — the portfolio map, the bio maps, the region
  bar, the `?kor_at=` and `?kor_region=` deep links.
- `live-theme/page-projects.php` — the derived banner count.
- `KorMapSyncRunner.cs` — Deltek to WordPress.
- `builders/dedupe_rule.py`, `builders/mapfilter.py` — a second implementation
  of the clustering rules.
- `builders/build_map.py`, `builders/build_proposal.py`, `builders/jim_list.py` —
  the printed figure and the project lists.

## What to prioritise

1. **The two clustering implementations must agree.** `kor_map_cluster()` in
   PHP and `pair_up()` in `dedupe_rule.py` implement the same rules twice, in
   two languages. Find inputs where they disagree — different normalisation,
   different distance handling, different tie-breaks, different conflict guards.
   Divergence here puts one number on the website and a different number in a
   client's document.

2. **Merges that should not happen.** Construct project names and coordinates
   that weld two genuinely different buildings into one pin. The conflict guard
   covers north/south/east/west and ordinals one-to-four. What about Phase 5,
   Tower B, Building 2, II vs 2, "Ph 2", "Bldg C", "West Tower" vs "Tower West"?
   Is the cluster-wide conflict check actually cluster-wide, or does insertion
   order let a pair slip in?

3. **Merges that should happen and do not.** Hyphenated address ranges
   ("2037-2061 East Broadway") fail `kor_map_norm_addr()` because the house
   number regex stops at the hyphen — two records for one building survived
   because of it. Find the rest of that class: unit prefixes, "&" addresses,
   suite numbers, PO boxes, French/accented characters, `&#038;` entities.

4. **The floor and the exclusions.** Can a record be dropped that should be
   kept, or kept that should be dropped? Check the exemption logic hard: curated
   records, `era = PRIOR`, records with no job number, records whose job number
   is absent from the billed data. What happens when `billed` is `"0"` versus
   empty versus missing — are those three distinguished, and should they be?

5. **`era` and attribution.** Verify the sync cannot overwrite an existing
   `era`. Verify nothing presents a `PRIOR` record as KOR's own work on any
   surface. Verify an untagged record is never counted as either.

6. **The counts.** `kor_map_project_count` (portfolio) and `kor_map_pin_count`
   (what the map plots) are deliberately different. Confirm each is computed
   from what its label claims, that the region-button counts agree with the pins
   actually drawn, and that the figure legend agrees with the dots rendered.

7. **Leakage.** No Deltek job number, credential, DSN, internal fee or client
   name should be reachable from the browser. Check the GeoJSON properties, the
   popup markup, the localized script data and the REST responses.

8. **`KorMapSyncRunner.cs`.** The SQL, the WBS-base grouping, the
   never-blank-an-existing-value rule, decimal handling on `BilledMeta`,
   behaviour when Deltek returns nulls or a job disappears from the result set.

## Constraints

- **Read-only review. Change no files, and run no build, test or deploy.**
- Do not run destructive git operations. Do not commit, reset, clean or
  checkout.
- Do not attempt to reach korstructural.com, Deltek or KOR-APP01. Everything
  needed is in `docs/map-audit/`.
- The exported theme files are a snapshot. Do not try to edit them "in place" —
  they are not the deployed copy.
- Deltek job `31010-01` really does carry a $750B billing rate. It is a data
  error being fixed at source; do not propose code that special-cases it.

## Output

For each finding: the file and line, what breaks, **a concrete input that
triggers it** (a project name, an address, a coordinate pair), and the smallest
correct fix. Separate what is genuinely wrong from what is merely unusual, and
rank by whether it could put a false claim in front of a client.

Then re-read your own findings adversarially before you answer: for each one,
try to prove it wrong. State plainly which ones survived that and which did not,
and say which parts of the code you could not reach a firm conclusion about.

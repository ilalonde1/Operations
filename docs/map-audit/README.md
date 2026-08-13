# KOR project map — what this is, and what it has to get right

A snapshot of every piece of the project-map pipeline, staged for review on
2026-08-12. The PHP and JS are **exported from the live korstructural.com theme**
(they are edited through the WordPress theme editor and live nowhere else — this
directory is the only place they can be read as source).

## What the system is for

One dataset drives four surfaces:

| surface | shows | consumer |
|---|---|---|
| `/projects/` map | KOR's portfolio, clustered | prospective clients |
| `/projects/` banner | a single project count | prospective clients |
| bio pages (`#kor-biomap`) | one engineer's own record | prospective clients |
| fee proposals | a printed figure + project list | **named clients, in writing** |

The last row is why this matters. A wrong pin is embarrassing on a website and
indefensible in a signed fee proposal.

## The pipeline

```
Deltek (ODBC, read-only, KOR-APP01)
   -> KorMapSyncRunner.cs          creates/updates Location posts, writes
                                   region / era / people / wbs1 / billed
   -> WordPress `location` CPT     + hand-imported portfolio records, hand
                                   corrections, and an `excluded` flag
   -> kor_regenerate_map_geojson() groups, merges, floors, emits GeoJSON
   -> kor-map-data.json            one file, fetched by every map
   -> custom-mapbox.js             portfolio map filters `small`; bio maps do not
   -> build_map.py / build_proposal.py   the printed figure and project lists
```

## What went wrong, in order, and what each fix cost

Recorded because every one of these was a *semantic* error that a build, a
linter and an HTTP health check all passed cleanly.

1. **Duplicate pins.** Grouping by Deltek WBS base cannot join a curated
   WordPress project post to a Deltek record for the same building — different
   origins, no shared job number. Jim DesRoches: *"it looks like we had eight
   projects up here when we only have three."*
   Fix: `kor_map_cluster()` — three high-precision arms (equal name, equal
   house-number+street, one name containing the other at close range).

2. **Over-merging.** The first clustering attempt used shared "distinctive"
   tokens plus distance, and union-find. Street names (`Hornby`, `Telford`) and
   generic words (`museum`, `tower`) leaked through, welding seven unrelated
   Burnaby buildings into one. Union-find then defeated the north/south guard by
   transitivity: *The Grande North* and *The Grande South* each merged with the
   bare *The Grande* and so landed together, though the rule refuses that pair
   outright.
   Fix: no union-find. A candidate must clear `kor_map_conflicting()` against
   **every** record already in the cluster.

3. **Trivial jobs presented as projects.** The only test was "somebody charged
   time to it", so a $100 vault-lid repair sat beside a 42-storey tower.
   Fix: a $25,000 floor on billable labour (`tkDetail.BillExt`, **not**
   `PR.Fee` — Fee is blank on 30% of jobs).

4. **The floor silently halved every engineer's bio map.** It was deleting
   records from the shared file. Kevin Wurmlinger went 94 -> 35.
   Fix: mark (`small: 1`), do not delete. The portfolio map filters; bio maps
   show everything.

5. **Unattributed buildings drawn as ours.** The figure paints anything not
   `PRIOR` in KOR orange but counts only `era == 'KOR'` — 22 orange dots under a
   legend reading 20.

6. **A mis-geocoded pin in the wrong city.** "Vista Lane Apartments" has the
   address "San Ysidro, San Diego, CA"; with no street number the geocoder
   returned the downtown centroid, so a San Ysidro job appeared on a figure
   whose entire claim is *this work is near your block*.

7. **Pursuits KOR never won.** This is the one that reached the client. Deltek
   holds **no won/lost flag**. A lost pursuit still carries proposal and
   early-design time, so the dollar floor keeps it. Invoicing looked like a
   clean proxy and is not: we invoice proposal work, so `4th & Ash` (2 invoices)
   and `Courthouse North Block` (3 invoices, *"lost to GS"*) both survive it.
   **There is no derivable signal.** Jim marked all 75 California rows
   Keep/Delete by hand; those decisions are the authority, carried as an
   `excluded` post-meta and read by `jim_list.py`.

8. **`era` overwrite, caught before it shipped.** The sync stamped `era = "KOR"`
   whenever a patch had more than one field. Harmless while patches were rare —
   but `billed` differs on the first run for every matched pin, which would have
   rewritten all 31 of Jim's pre-KOR towers to KOR in a single pass.

## Invariants — these are the things that must not break

- A building appears **once**. Two records for one address is the original bug.
- Two genuinely different buildings never merge. `The Grande North` and
  `The Grande South` must remain two pins.
- A record marked `excluded` never appears on any surface.
- A record with `era = PRIOR` is **never** presented as KOR's own work, and the
  sync never overwrites `era` on a record that already has one.
- The bio maps show an engineer's whole record; only the portfolio map filters.
- The banner count and the map count are different numbers **on purpose**
  (portfolio vs. selection) and the map says so on the page.
- No Deltek connection, credential, DSN or job number ever reaches the browser.
- Nothing appears in a fee proposal that KOR was not paid to do.

## Known data defect, not a code bug

Deltek job `31010-01` ("Little Italy State St & Hawthorn St") carries a billing
rate of **$250,000,000,000/hour** on three Nov-2021 rows — $750B of `BillExt`
against a correct $39.53 cost rate. Any report summing `BillExt` is wrong by
that much. Being fixed in Deltek; do not code around it.

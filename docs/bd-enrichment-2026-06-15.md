# BD Brain — Enrichment Pass (overnight 2026-06-15)

Goal: deepen the BD data for Vancouver/Lower Mainland, Vancouver Island, Okanagan,
and Alberta — make it a one-of-a-kind tool, data at the center.

## SHIPPED tonight (committed + applied + verified, zero data-quality risk)

1. **Region taxonomy normalized** (migration 141, applied). MPI `RegionName` had ~100
   free-text variants + a source bug leaking lat/long coordinates into the field on
   ~40 AB rows. Collapsed BC+AB to a canonical set (BC: Lower Mainland / Vancouver Island
   / Okanagan/Interior / Northern BC; AB: Calgary Metro / Edmonton Metro / Southern /
   Central Alberta); junk + coordinate-leak → NULL. 6,850 rows normalized; distinct
   labels 100+ → 8. Regional rollups/dashboards/gap-maps are now sliceable.

2. **Differentiator views** (migration 142, applied + verified):
   - `vArchitectIncumbentSE` / `vGcIncumbentSE` — the **displacement map**: per architect/GC,
     the competitor structural engineers they pair with (ranked by shared projects) + how
     many of that firm's projects already used KOR (`KorSharedProjects`). "Who do we
     displace, where do we already have a foot in." Live sample: DIALOG↔Fast+Epp,
     OMB↔RJC, Arcadis/PUBLIC↔WHM — all KorFootIn=0 (pure displacement targets).
   - `vRegionBdRollup` — clean per-region project/org-by-role counts.

3. **EmailFilerv2 publish fix** (csproj) — repointed stale NuGet `packages\` paths.

## DIAGNOSIS — where the data is thin (grounded, not guessed)

Enrichment coverage of project-linked orgs by region:
| Region | Orgs | Briefed | People | **Email** | Signals |
|---|---|---|---|---|---|
| Lower Mainland | 356 | 79% | 67% | **18%** | 68% |
| Vancouver Island | 140 | 76% | 69% | **20%** | 64% |
| Okanagan/Interior | 69 | 77% | 61% | **14%** | 62% |
| Alberta | 159 | 81% | 74% | **22%** | 78% |

Two dominant gaps, in priority order:
1. **Structural-engineer-per-project edge: 3.6% populated** (63 of 1,763 active BC/AB
   projects). This is THE highest-value datapoint for a structural firm (it powers the
   displacement map) and it's almost empty. The data is NOT sitting un-linked — IntelWork
   has ~119 SE-role rows but none tied to a project — so it's a research gap.
2. **Decision-maker contactability: ~80% of project-linked selectors have no email/LinkedIn.**
   `vDossierCompletenessPeople` pinpoints **1,251 selection-authorities on PURSUE work**
   missing contact. Can't action a pursuit without a way to reach the selector.
Plus: **Okanagan is the thinnest region** (most breadth headroom); ~510 BC projects still
have no region label (backfill from municipality).

## ENRICHMENT PLAN — primed, ready to run (web-research = Sonnet drains, staggered)

NOT blind-run overnight on purpose: these write to the core data asset and contact-finding
is hallucination-prone; a quality-gated run protects the moat. Existing drains/streams to use
(no new folders): `contact-finder` / `contact-enrichment`, `okanagan-people`, `vanisland-people`,
`people`, `project-teams` / team-awards (for the SE edge), `GVRD-Comprehensive`,
`Island-Okanagan-Ecosystem`, `AlbertaTrip-*` / `AlbertaDeepening`.

Recommended order:
1. **SE-per-project backfill** (project-teams / team-awards research) — fill the architect+SE+GC
   edge on PURSUE/high-value MPI projects, region by region. Biggest differentiator unlock;
   feeds the displacement map directly. Verifiable (the SE on a real project is a findable fact).
2. **Selector contactability** (`contact-finder` off `vDossierCompletenessPeople`, 1,251 targets,
   prioritized by importance) — strict cite-the-source/no-guess prompt; the hardened ingest +
   a post-run merge audit (per the standing dedup/honing audit rule) catch problems.
3. **Regional breadth** — Okanagan first (`okanagan-people` + Island-Okanagan-Ecosystem),
   then Island people depth, then GVRD/Alberta pipeline freshness.

Run pattern: bounded batches via Sonnet agents, ingest through the (now-hardened) pipeline,
then `BdIntelExtract --commit` + an audit. Gate batch 2+ on batch 1 quality.

## Follow-ups (code, my lane)
- Backfill `RegionName` from municipality for the ~510 unspecified BC projects.
- Add a write-path `RegionNormalizer` so region labels can't re-drift (source fix).
- Trace + fix the importer that leaked coordinates into `RegionName` (AB MPI provider).
- Owner capital-timing view + warm-path/relationship view (needs Deltek relationship data) —
  the remaining "one-of-a-kind" differentiators, deferred to a focused pass.

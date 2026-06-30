# BD Enrichment — Architect → Incumbent-SE → Key-People Spine
**Autonomous session • 2026-06-21 • Claude (Opus)**

## Why this, not permits
Building permits are a *lagging* signal for structural-engineer pursuit — by permit stage the SE seat is locked (`feedback_open_seat_stage_gate`). The primary enrichment target is the **relationship graph of who selects the SE**: architects (prime consultants) first, then developers, then the decision-makers inside them. KOR wins as the architect's structural sub, so knowing **which SE each architect already teams with** is the displacement map. Permit work is shelved (scoping preserved in `docs/codex-permit-adapters-block3.md`) — though see §7, permits do have one narrow real use here.

## The gap, quantified (live DB)
| Metric | Value |
|---|---|
| Active architects (CanonicalOrg) | 1,144 |
| Active MPI projects | 2,374 |
| …with an architect linked | 453 |
| …with **both architect AND SE linked** | **72 (3.0%)** ← the #1 gap |
| Architect FirmNarrative enrichment | 130 done / 11 in-progress / **5,336 queued** |
| Person enrichment (`PersonBrief`) ever completed | **1** |

The org-enrichment pipeline isn't absent — it's **backlogged**, and the people layer is gated behind it (FirmNarrative discovers the key people that PersonBrief then deep-enriches).

## What I did (autonomous, reversible)

### 1. Re-prioritized the enrichment backlog — warm SE-selectors to the front
The poller claims `Pending` FIFO by `RequestedAtUtc`. The 552 warm SE-selectors were already queued but **buried** mid-backlog (avg claim position ~2,400 → ~2 days out). Re-stamped them to the front, tiered:
- **P1 — 155 architects-of-record** (live MPI pipeline) → stamped first
- **P2 — 65 architects** with KOR history / Deltek link
- **P3 — 332 warm developers** (proponents / KOR / Deltek)

Zero added cost (reorder only; all were already queued + approved). At the measured ~50–65/hr drain they enrich **overnight**. `output/reprioritize-warm-selectors.ps1`.

### 2. Researched the incumbent-SE edge for the top 20 architects-of-record
Sonnet fan-out, BC core + CA initiative firms, **sourced credits only — no guessing**. 17 returned (3 still running: Gensler, W.T. Leung, Focus). Results staged below; **I did not auto-write any competitor edges** (data-integrity / review-gated, per `feedback_clean_at_source`).

## The displacement map (synthesis)

**BC high-rise / mixed-use residential → Glotman Simpson is the entrenched incumbent.**
GBL, Henriquez (deep, Westbank-wide), Ankenman Marchand, Musson Cattell Mackey, Revery (towers). Confirms `project_bc_competitor_dominance_map` (Glotman = hi-rise resi). KOR is a pure challenger here — entry needs a *developer* relationship to get on the team pre-RFP, not an architect cold-approach.

**BC civic / institutional / recreation → RJC + Fast+Epp split the lane.**
hcma, SHAPE, Acton Ostry, Revery (aquatics). RJC owns wood community-centres; Fast+Epp owns aquatics/mass-timber. KOR wedge = heritage/seismic differentiation on projects where neither is locked.

**California → national seismic elites are incumbent.**
AC Martin → Thornton Tomasetti / Brandow & Johnston / Englekirk. Johnson Fain → Englekirk / Nabih Youssef. SCB → MKA / KPFF / Thornton Tomasetti. KOR (CA challenger) realistically displaces on **podium / mid-rise residential**, not trophy high-rises — matches `project_california_bd_initiative` (KOR = challenger vs KPFF default).

**Standout — Yamamoto Architecture (#69660): EXISTING KOR relationship.**
KOR is a confirmed SE partner (Georgia Tower 46-storey Wesgroup + a 280-unit Wesgroup project), splitting work with Nemetz. Yamamoto has **4 active MPI projects at "Planned" stage with no SE assigned** — early-stage + warm relationship = **the hottest open-seat targets in this whole set**:
- #2684 7510 Cambie Street Condos · #2951 Rockford Condominium · #3382 520-590 West 29th & Ash · #3393 6486 Chester Street

## Staged architect→SE edges (HELD for your review — not written)
`SE#` = existing canonical org id; on approval these link with no new org creation.

| Architect | Incumbent SE(s) | Conf | SE canonical |
|---|---|---|---|
| GBL #54190 | Glotman Simpson; Fast+Epp | High | #38926; #7345 |
| hcma #8799 | RJC; Fast+Epp | High | #69014; #7345 |
| James Cheng #69676 | Jones Kwong Kishi → DIALOG | High | #74185 |
| Henriquez #29895 | Glotman Simpson (deep) | High | #38926 |
| SHAPE #69164 | RJC; WSP | High | #69014; #18657 |
| Ankenman Marchand #54302 | Glotman Simpson | High | #38926 |
| **Yamamoto #69660** | **KOR Structural** (existing); Nemetz | High | **#38918**; #69522 |
| Musson Cattell #69589 | Glotman Simpson | High | #38926 |
| Revery #38974 | Fast+Epp; Glotman Simpson | High | #7345; #38926 |
| AC Martin #70108 | Thornton Tomasetti; Brandow; Englekirk | High | #68746; #38919; #38924 |
| Acton Ostry | Fast+Epp; RJC; Equilibrium | High | #7345; #69014; #38925 |
| Johnson Fain #68634 | Englekirk; Nabih Youssef | High/Med | #38924; #38931 |
| SCB #75180 | MKA; KPFF; Thornton Tomasetti | High | #68742; #38927; #68746 |
| Studio One #75595 | — none published — | — | permit lookup |
| Ciccozzi #54553 | — none published — | — | permit lookup |
| Chernoff Thompson #4771 | — none published — | — | permit lookup |
| Wensley #54454 | — none published — | — | permit lookup |

Full evidence + named key-people in `output/se-edge-findings.json`.

## §7 — Where permits DO matter (the one real use)
Four firms (Studio One, Ciccozzi, Chernoff Thompson, Wensley) publish **zero consultant credits** — their structural EOR is only discoverable on the **building/development permit record**. So permit data's genuine value isn't seat-finding; it's **back-filling the incumbent-SE edge for credit-less architects**. If we revisit Block 3, point it at the permit *applicant/structural-of-record* fields for exactly these firms.

## Data-hygiene found (for a dedup pass)
- **RJC duplicate:** #69014 "RJC Engineers" vs #74588 "Read Jones Christoffersen Ltd." (same firm)
- **KPFF duplicate:** #38927 "KPFF Consulting Engineers" vs #76327 "KPFF"

## Status / what needs your decision on return
- **Running now:** warm-architect + developer FirmNarrative enrichment draining front-of-queue (~50–65/hr; ~552 over the night). People auto-discovered as orgs complete.
- **For your approval:** (a) write the staged architect→SE edges above; (b) treat the 4 Yamamoto "Planned" projects as priority warm open-seat pursuits; (c) merge the RJC + KPFF dups; (d) whether to PersonBrief-enrich the named principals once their firms' org enrichment lands.
- **Pending research:** Gensler, W.T. Leung, Focus (3 agents finishing) — I'll append.

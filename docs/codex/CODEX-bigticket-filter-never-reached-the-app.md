# CODEX — 2,024 Signals rows are already known to be small work, and are shown anyway

**One defect. Do not touch the scorer — it shipped today (`df46c1f0`) and is deployed. Do
not touch `StructuralRelevanceGate`. Do not change the Signals/Bids/Mine split. Do not wire
a source. Do not write a second big-ticket predicate — see the warning below, which is the
whole point of this brief.**

## The defect, measured 2026-10-09

The Signals segment holds **5,795** application rows. Against
`opportunities.vIslandApplications`:

| | Rows |
|---|---:|
| Classified by the view as **small work** (`IsBigTicket = 0`) | **2,024** |
| Classified as big-ticket (`IsBigTicket = 1`) | 1,279 |
| **Not reached by the view at all** | **2,492** |

**So 2,024 rows the system has already decided are tenant improvements, decks, sheds, signs,
single lots and demolitions are sitting in the default early-signal screen** — and a further
2,492 have never been assessed because the view's `WHERE` clause names twelve Island sources
and nothing else:

| Unreached source | Rows |
|---|---:|
| Surrey_DevelopmentApplications | 910 |
| Calgary_LandUseRedesignation | 864 |
| Coquitlam_DevelopmentApplications | 404 |
| MapleRidge_DevelopmentApplications | 122 |
| Vancouver_DevelopmentApplications | 100 |
| Kamloops_PlanningApplications | 46 |
| Esquimalt_DevelopmentApplications | 29 |
| PortMoody_DevelopmentApplications | 17 |

⭐ **And the predicate reaches no user at all.** `IsBigTicket` is referenced in exactly two
places outside the schema: `tools/BdOpportunityDedup`'s header comment, and
`tools/ScoringDiff`, built today. **Nothing in `Kor.Operations.App` reads it.** The work was
done, it is correct, and it has never been in front of anybody.

The visible consequence, on the screenshot taken after today's hub change: the first screen a
regional manager sees is a wall of `RESIDENTIAL BUILDING PERMIT — <address>` from Saanich.
The rename made the screen honest. It did not make it useful.

## ⛔ Do not write a second predicate. This repo has already paid for that.

`Kor.Opportunities.Data/Schema/316_IslandBigTicketView.sql` exists as a VIEW, and its own
header says why:

> *"The filter Rory asked for ('weed out single family homes, TI stuff') was written twice in
> one session, as a CASE with buckets and as a NOT(...) predicate, and the two disagreed:
> 1,130 vs 1,156 rows. **One of them shipped in a dossier.** The predicate lives here now so a
> count in the app, a count in a dossier and a count in an ad-hoc query cannot drift apart
> again."*

`tools/ScoringDiff/Program.cs:5` is the behaviour to copy — *"Uses vIslandApplications.
IsBigTicket verbatim; does not duplicate its predicate."* Codex wrote that today. Do the same.

**Generalise the existing view. Do not add a parallel classifier in C#, in the hub, or in a
new view.**

## ⚠ Read the view's own stated limits before widening it

Migration 316 already documents what it cannot do, and widening it does not fix any of these:

- It cannot rank by value — only Langford publishes one.
- It reads Title and the newest observation Description. **An application whose scope text is
  empty passes the filter by default.** Sooke is the extreme case: every row is
  `PLANNING — <address>` with an empty description.
- **"A multi-unit building described only as 'RESIDENTIAL BUILDING PERMIT' is removed with the
  single lots, because the source gives nothing to tell them apart. Saanich is where that
  bites hardest."**

That last one is why the next section exists.

## ⛔ Nothing may be silently dropped. This is a radar.

Ian, on what the platform is for: *"an early signal radar so we can see who is moving on what
so we can then form relationships."* The measure is **recall**, not precision — and the view's
own warning says it removes genuine multi-unit buildings whose description is uninformative.

So **small work is collapsed, never hidden**: the count must be on screen and one click must
reveal it. A user must be able to see "2,024 smaller items hidden" and open them. A filter
that silently removes 35% of the segment has traded one wrong default for a worse one.

Whether the default is *big-ticket first* or *small collapsed* is yours to choose — say which
and why. **Not negotiable: the number is visible and the rows are one click away.**

## The change

1. **Generalise migration 316's view** so it classifies every application-kind source, not
   twelve named Island ones. The source list is the thing to remove; the predicate is the
   thing to keep.
2. **Surface `IsBigTicket` in the hub** — as a column, a filter, and a default — reusing the
   view. The hub reads through `IOpportunityHubStore` / `SqlOpportunityStore.Hub.cs`, added
   today.
3. **Say what happens to the 2,492 currently unreached rows.** Calgary's 864 are land-use
   redesignations — arguably all big-ticket by definition, since a rezoning is a change of
   buildable form. Decide it explicitly and state the reasoning; do not let them fall into a
   default by accident.

## ⭐ A free cross-check you should run

The scorer learned planning vocabulary today. On the Island cohort the two signals should
broadly agree: **`IsBigTicket = 0` rows should score low, `IsBigTicket = 1` rows high.**
Measured now, Saanich multi-unit/rezoning averages **36.3** against single-family **13.0**, so
they already do.

**Report the disagreements** — `IsBigTicket = 0` rows scoring above the big-ticket mean, and
`IsBigTicket = 1` rows scoring below the small mean. Those rows are where one of the two is
wrong, and they are the most useful output of this change. Do not "fix" either side to make
them agree; report them.

## The rule that must hold afterwards

**One predicate decides what is small work, and the app, a dossier and an ad-hoc query all
read the same one.** That is migration 316's own rule, and the reason it is a view.

And: **no row leaves the user's reach.** 5,795 Signals rows today; after this change the
default plus one click must still reach all 5,795, with the hidden count stated on screen.

## ⚠ Required in the answer, per rule 11

The generalised view must carry, in its own header as migration 316 does: what it covers, what
it does not, and **at least one same-class fault it would not catch**. 316's existing example
to improve on, not copy: *"a tenant improvement described only as 'interior renovation'
carries none of the words above and stays in."*

## Shape of the expected answer

- The generalised view, and what changed from 316 beyond removing the source list.
- Row counts: big-ticket / small / unreachable, per source, before and after.
- The decision on Calgary's 864, with reasoning.
- The scorer-vs-predicate disagreement list.
- Evidence the app was RUN — a screenshot of Signals with the count visible and the reveal
  working. ⛔ Do NOT run the full `Kor.Operations.App.Tests` suite; it wedges from a
  non-interactive session. Targeted `--filter` only.
- `dotnet test --filter "Speed!=Slow"` on `Kor.Opportunities.Data.Tests` (245 tests, ~5s).
- Test names.

## Reproduce

```sql
-- the headline: what Signals holds, and what the view already knows about it
SELECT DISTINCT o.Id, s.Name AS SourceName
INTO #sig
FROM opportunities.Opportunities o
JOIN opportunities.OpportunityObservations ob ON ob.OpportunityId = o.Id
JOIN opportunities.OpportunitySources s ON s.Id = ob.OpportunitySourceId
LEFT JOIN opportunities.OpportunitySourceMappings m
       ON m.OpportunitySourceId = s.Id AND m.[Key] = 'bd.kind'
WHERE o.DismissedAtUtc IS NULL
  AND (s.SourceType IN (20,21,22)
       OR (s.SourceType IN (1,2,3,5,99) AND m.ValueJson = 'application'));

SELECT COUNT_BIG(DISTINCT g.Id) AS SignalsRows,
       COUNT_BIG(DISTINCT CASE WHEN v.OpportunityId IS NOT NULL THEN g.Id END) AS Classified,
       COUNT_BIG(DISTINCT CASE WHEN v.OpportunityId IS NULL THEN g.Id END)     AS Unreached
FROM #sig g
LEFT JOIN (SELECT DISTINCT OpportunityId FROM opportunities.vIslandApplications) v
       ON v.OpportunityId = g.Id;
```

## Out of scope, explicitly

- **The scorer.** Shipped and deployed today. This filter is a read-layer concern; do not
  encode "small" as a score penalty.
- **The Signals/Bids/Mine split, the Kind column, `bd.kind`.** Shipped today.
- **Sooke, and any source with empty descriptions.** The view cannot classify what the
  publisher does not say; note it, do not invent a workaround.
- **`PrimePipeline`**, the other view over `Opportunities`. Not an applications surface.
- **Pursuits / Pursuit Monitor / BD Tracking naming, the ~19 no-op buttons, empty states,
  Reports on the rail.** All real, all separately briefed or queued.

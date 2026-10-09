# CODEX — the BD hub calls 3,914 development applications "All tenders"

**One defect. Do not rename anything in the FIND/PURSUE/KNOW/TOOLS rail except the one
label named below. Do not touch Pursuits / Pursuit Monitor / BD Tracking. Do not fix the
no-op buttons, the empty states or the em-dashes — those are separate briefs. Do not change
any SQL, any provider, or any relevance-gate behaviour.**

## Start here: read the prior art, it already parked this

`docs/audit-2026-08/modules/08-bd-app-surface.md` (20 August, 871 lines) audited this exact
surface **as a user interface, not only as code**. Read its §5(d), §5(h), §5(i) and §9 before
touching anything. Its to-do register item **#25 is this brief's subject**, sized `L` and
tagged `LATER`:

> *"Rationalise folder-vs-navigation naming; add Reports to the BD rail. Reports is
> currently unreachable from the workspace."*

Verified still true 2026-10-09: `BdReportsWindow` is constructed only from
`HomeWindow.xaml.cs:201`, and nothing in `BdWorkspaceWindow` reaches it. **That half is NOT
in scope here** — it is listed so you do not think it was missed.

That audit is about demo risk. **This brief is about a different thing it did not cover: the
top-level label is factually wrong.**

## The defect, measured 2026-10-09

`Kor.Operations.App/BusinessDevelopment/Workspace/OpportunitiesHubView.xaml` is a two-button
segmented control, and those two buttons are the entire top-level choice a user gets:

```
Content="To grab"        -> BazaarView        (un-claimed, relevance-ranked slice)
Content="All tenders"    -> OpportunitiesView (everything)
```

`OpportunitiesHubView.xaml.cs:13` says it plainly in its own comment: *"…the same
`opportunities.Opportunities` table; 'To grab' is just the un-claimed slice."*

Live composition of that table (`DismissedAtUtc IS NULL`, counted, not sampled):

| What it actually is | Rows |
|---|---:|
| **Early-signal applications and rezonings** (SourceType 20/21/22) | **3,914** |
| Tenders / RFPs (BcBid, Bonfire, bids&tenders, APC, MERX, CanadaBuys…) | 2,827 |
| GenericJson / GenericCsv, incl. Calgary's 864 land-use redesignations | 2,083 |
| GraphEmail + Manual | 152 |

**The largest single category — 44% of the pool, and the one the platform exists for — is
behind a button that says those rows are tenders.** `Kor.Opportunities.Data/CLAUDE.md` is
explicit that this is a category error: *"Do not treat tenders as pipeline. Tenders tell you
what is out for bid NOW — by then the structural engineer was chosen months ago."* The data
layer knows the difference. The interface asserts they are the same thing.

## Why this is the first UX change and not a cosmetic one

The two buttons split on **claim state**. What decides the user's next action is **kind**:

| Kind | The user's actual job | Time pressure |
|---|---|---|
| Early signal (application, rezoning) | find the developer and the architect, get a meeting | none, but it **decays** — a 2017 rezoning is cold |
| Tender / RFP | decide whether to bid, and by when | a hard deadline |

Those are different jobs, and the current split hides the distinction entirely while
offering **no time axis at all**. The filters on `OpportunitiesView.xaml` are Filter text,
Status, Tier, Province and "Prime-Consultant RFPs only" — nothing answers *"what is new this
week"* or *"what closes soonest"*.

Measured consequence: **1 of 8,007 opportunities has an owner.** Adoption, not data, is the
problem, and five regional managers are the audience.

## The change

**Replace the claim-state split with a kind split, in that one hub.** Three segments:

| Segment | Shows | Default sort |
|---|---|---|
| **Signals** | SourceType 20, 21, 22, and the municipal GenericJson feeds | newest filed first |
| **Bids** | the tender/award source types | closing soonest first |
| **Mine** | whatever the current user has claimed, any kind | most recently touched |

`To grab` stops being a top-level door and becomes a **filter available inside Signals and
Bids** — because "has anyone taken this" is a second question, after "what kind of thing is
this". `BazaarView` and `OpportunitiesView` both continue to exist and both keep working;
this is about which door the user is offered and what the door says.

**Those three words are the recommendation, with the reasoning above. If you ship different
ones, say why in the response.** Ian owns the final vocabulary and will veto on review.

⛔ **Derive the kind from `OpportunitySourceType`, not from a hand-maintained name list.**
That is the mistake `tools/BdSourceCoverage/coverage_gap.py` made with its hardcoded
`HAVE_APPLICATIONS` literal, which went stale the moment two sources were wired and cost a
whole duplicated measurement pass. A new source type must land somewhere predictable without
anyone editing the UI — say explicitly in your response which bucket an unrecognised
`SourceType` falls into and why.

## The rule that must hold afterwards

**No label in this hub may describe a row as something it is not.** A user must be able to
tell, without clicking and without training, whether they are looking at something somebody
has applied to build or something somebody is asking for bids on.

And the regression rule: **every row currently reachable stays reachable.** 8,976 rows are
visible today; after this change the three segments plus their filters must still reach all
of them. A kind split that silently drops the 152 GraphEmail/Manual rows has replaced a
wrong label with missing data, which is worse.

## ⚠ Verification bar — this is WPF, so RUN IT

**A screenshot or a described click-path of the running app, not a build log.** This repo's
standing rule is that WPF is verified by running it; a compile proves nothing about a label.

- `dotnet build` green, and `OrphanedPublicTypeTests` still passes — it is the gate that
  catches a view left with no consumer, and this change moves consumers around.
- ⛔ **Do NOT run the full `Kor.Operations.App.Tests` suite.** It wedges indefinitely from a
  non-interactive session — testhost alive at ~0% CPU, 29+ minutes on 2026-06-09 — because
  some tests wait on a UI dispatcher that can never appear. Use
  `dotnet test <csproj> --no-build --filter "FullyQualifiedName~<Class>"`.
- State the row count behind each of the three segments, and that they sum to the total.

## Shape of the expected answer

- The three segment labels as shipped, and the `SourceType` → segment mapping, including the
  unrecognised-type rule.
- Before/after row counts per segment, summing to the live total.
- Evidence the app was RUN: what you clicked and what appeared.
- What you did NOT change, listed — particularly that `BazaarView` and `OpportunitiesView`
  are both still reachable and still functional.
- Test names, and the filtered command you used.

## Reproduce

```sql
-- the composition the "All tenders" label is asserting over
WITH x AS (
  SELECT DISTINCT o.Id, s.SourceType
  FROM opportunities.Opportunities o
  JOIN opportunities.OpportunityObservations ob ON ob.OpportunityId = o.Id
  JOIN opportunities.OpportunitySources s ON s.Id = ob.OpportunitySourceId
  WHERE o.DismissedAtUtc IS NULL)
SELECT SourceType, COUNT_BIG(*) AS Opportunities
FROM x GROUP BY SourceType ORDER BY COUNT_BIG(*) DESC;
```

Source types are declared in `Kor.Opportunities.Core/Models/OpportunitySource.cs` with a
comment on each explaining what it carries — 20/21/22 say "EARLY signal" in their own
comments. Use that enum as the authority.

## Out of scope, explicitly

- **The FIND/PURSUE/KNOW/TOOLS rail.** Only the `Opportunities` nav button's label may change
  if the hub's name changes; nothing else in `BdWorkspaceWindow.xaml`.
- **Pursuits / Pursuit Monitor / BD Tracking** — three names for one concept, and BD Tracking
  is filed under KNOW. Real, next brief.
- **~19 controls that silently `return;` with no selection**, including the primary "Start
  Pursuit" button. `HasSelected` already exists on the view models and is bound ONLY to
  detail-panel `Visibility`, never to a button's `IsEnabled` — verified 2026-10-09. Audit
  item #12. Separate brief.
- **Two buttons referenced by names they do not have**, still live and verified:
  `OpportunitiesViewModel.cs:454` tells the user to click *"New Opportunity"* (the button
  says **New**) and `OpportunitiesView.xaml:374` says click *'Promote to Pursuit'* (the
  button says **Start Pursuit**). Audit item #17. Separate brief.
- **Empty states** — `RelationshipsView` opens two-thirds blank, `CrmViewModel.IsLoading` is
  set but bound by nothing (verified: the only `IsLoading` binding in `Crm/*.xaml` belongs to
  `BdTrackingViewModel`). Audit item #19. Separate brief.
- **Reports unreachable from the BD rail.** Audit item #25's other half.
- **Anything in `Future Projects` / MPI**, and anything that changes what is ingested.

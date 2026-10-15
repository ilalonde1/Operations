# BD Director 9a — the window and two panes

Applied 2026-10-14. **Source edits and regression sources only; nothing was built or run.**
No tests, application/model calls, render harness, database tools, migrations, deployments,
emails or service actions were run. Source searches, source review and whitespace checks
were performed. `git diff --check` passed. No executed pre-fix failure, post-fix pass or
visual verification is claimed.

Prior art: searched `tools/`, `docs/codex/` and `../KOR.Drafter/db/tools/` for Director/render
work, and the App for actor resolution. Read the existing Home/COO window wiring, pursuit
board/store/dialog, staff picker, analytics service, attribution store, schema definitions,
date converter, render harness and regression classes. Matched brief 8's response format.
The brief supplies the production measurements; they were accepted, not remeasured.

## Applied changes

- **Piece 1 — window, tile, gate:** Added `BdDirectorWindow`, its view model and view rows.
  The window has the required title, live/people/as-of header, refresh and load-on-Loaded
  behavior. Its actor is resolved once in the existing precedence (signed-in identity,
  configured override, Windows user), then shared by telemetry and reassignment. The
  existing board was not edited. Added the `BdDirector` role/config key, collapsed Home
  tile, security-failure slot, click handler and ordering immediately after COO Card.
  Registered the store as singleton and window/view model as transients.
- **Piece 2 — store:** Added two read-only methods. People aggregate by
  `COALESCE(s.Mailbox, e.OwnerStaffId)` in SQL, with all directory spellings included in
  `OwnerKeys`. Unknown owners fall back to their stored key. Activities and stage moves
  are counted per pursuit before summing; screen opens use the person's keys once,
  independently of pursuit counts. The funnel reads stage medians, overdue undecided
  pursuits, missing-deadline coverage and lost-detail completeness. Appended the requested
  telemetry-consumer documentation sentence.
- **Piece 3 — people:** Added the exact caption and nine columns, ordered by live count
  descending, with no rank, targets or colour grading. Selecting a person filters the
  existing pursuit-store list by all their owner keys and live stages. Dates use the
  shared formatter. Reassign uses the existing roster/dialog and store method, passing
  the original stored owner key, and reloads both panes on success. Refusals use the
  existing sentences; a duplicate in-flight request is ignored. Open resolves a fresh
  workspace from DI and navigates to that pursuit. No reassignment email was added.
- **Piece 4 — funnel:** Added the exact caption, Drafting/Submitted stage table, medians,
  analytics-sourced outcomes and separate backfill wins, loss-detail sentence, overdue
  undecided worklist and source/wins/fee table. Stage counts and outcomes come from
  `CrmAnalyticsService.Compute` over the engagement store. Source rows are passed through
  from `IBdAttributionStore.GetSummaryAsync`. The unused buyer-type analytics dimension
  receives an empty opportunity dictionary; the new SQL owns the requested deadline list.
- **Piece 5 — clarity:** Window dates use `BdDateDisplay`/`BdDay`. Added the Director folder
  to the date ratchet and kept its ceiling at 64. The new folder has no inline date-format
  literals. Replaced the specified opportunity-hub tooltip. No ComboBox was added; the
  existing reassignment dialog remains the picker.
- **Piece 6 — harness and gates:** Added `RenderDirector(dir)` at 1680×980, writing
  `director.png`, using mocked stores and the existing adorner-aware rendering helper.
  The render-count minimum is now six. Added the Director regression class and the
  security-failure assertion without deleting or weakening existing gates.

## Gates authored, not executed

| Gate | Expected versus actual |
|---|---|
| `Home_UsesDirectorRole_AndFailsClosed` | Role check, visibility assignment/fallback, card ordering, DI resolution and Ian's configured membership. |
| `Window_UsesPlainWords_AndBothExactCaptions` | Exact captions and visible XAML/view-model literals without the listed developer words. |
| `Store_ReadsTheSpecifiedPopulations_AndGroupsByResolvedPerson` | Live stages, staff/mailbox identity grouping, warmth/open joins, current-stage median fallback, deadline coverage, lost-source exclusion and no write keywords. |
| `OpportunityTooltip_HasNoOldScreenName` | Required tooltip and no Bazaar in the hub XAML. |
| `Director_IsInsideDateRatchet_AndContributesZero` | Director folder included, ceiling still 64, zero inline formats and the required converter declaration. |
| `Drill_IncludesAllOwnerSpellings_OnlyInLiveStages` | Both case-insensitive aliases included; closed pursuits and other owners excluded. |
| `Funnel_UsesAnalyticsSegmentation_AndScorecardSourceRows` | Live stage total, separately counted Deltek wins, decided/undecided wording, unchanged attribution row. |
| `Reassign_PassesStoredOwner_AndReloadsBothPanes` | Original owner key reaches the write; people, funnel, drill and attribution are reloaded. |
| `Reassign_RefusalsUseExistingSentences` | Existing wording for owner changed, duplicate target and closed pursuit, without a successful-move reload. |
| `Reassign_InFlightClickIsIgnored_WithoutClaimingAnOwnerChange` | One write for overlapping clicks; ignored click leaves the message unchanged. |
| `HomeTileVisibilityStateTests` addition | Director collapses on a security lookup failure. |
| Existing date ratchet and render harness | Expanded coverage, unchanged date ceiling, additional mocked Director render. |

For the brief's grouping gate, **the SQL GROUP BY option was used**: the store already
returns one row per person. The separate view-model test checks alias-aware drill filtering.

## Fixture discrepancies and limits

The brief refers to an OwnerStaffId table of per-owner overdue/no-next-action counts, but
that table is absent. It supplies totals (24/74), Omar's 5/19 and Ian's 0/1. The other six
splits are explicitly marked **illustrative** in the fixture, preserving those totals and
the three future actions. The supplied eight live counts, zero recent activity/moves,
101 untouched, per-person buyer-mail counts and requested John Bryson/Ian opens are used.
The missing breakdown was requested during implementation; no database was queried to
fill it. These six illustrative splits must not be reported as measured per-person facts.

The stage split, medians, three deadline examples, no-deadline count, pursuit titles/dates
and source fees are layout examples. The requested second source row is a zero-win mock
placeholder. The actual attribution SQL returns winning sources only, so that placeholder
is not a production-shaped source aggregate; adding a second winning source would conflict
with the requested zero decided live pursuits. The real UI does not synthesize source rows.

The wording gate checks visible literals, not type/namespace identifiers or text returned
by stores. SQL text gates do not execute SQL or prove cardinalities, and the class summary
states a same-class miss: an added join could multiply counts while retaining the checked
clauses. Mocks cannot prove query performance, binding correctness, clipping, dialog behavior
or real authorization. Reads span existing independent stores, not one database snapshot.
The layout and PNG have not been rendered or inspected in this session.

Claude owns the Debug build, filtered regression execution (including negative controls),
rendering/PNG review and local deployment. Ian has no execution step in this handoff.
No commit was made.

## Files touched

- `Kor.Operations.App/BusinessDevelopment/Director/BdDirectorWindow.xaml` (new)
- `Kor.Operations.App/BusinessDevelopment/Director/BdDirectorWindow.xaml.cs` (new)
- `Kor.Operations.App/BusinessDevelopment/Director/BdDirectorViewModel.cs` (new)
- `Kor.Operations.App/BusinessDevelopment/Director/BdDirectorPersonRow.cs` (new)
- `Kor.Opportunities.Data/Crm/IBdDirectorStore.cs` (new)
- `Kor.Opportunities.Data/Crm/SqlBdDirectorStore.cs` (new)
- `Kor.Opportunities.Data/Crm/SqlBdUiOpenStore.cs`
- `Kor.Operations.App/HomeWindow.xaml`
- `Kor.Operations.App/HomeWindow.xaml.cs`
- `Kor.Operations.App/HomeTileVisibilityState.cs`
- `Kor.Operations.App/Services/KnownRoles.cs`
- `Kor.Operations.App/App.config`
- `Kor.Operations.App/CompositionModules/OpportunitiesModule.cs`
- `Kor.Operations.App/BusinessDevelopment/Workspace/OpportunitiesHubView.xaml`
- `Kor.Operations.App/Kor.Transmittals.App.Tests/HomeTileVisibilityStateTests.cs`
- `Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdPresentationRegressionTests.cs`
- `Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdScreensRenderTests.cs`
- `Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdDirectorRegressionTests.cs` (new)
- `docs/codex/CODEX-BD-DIRECTOR-9A-THE-WINDOW-AND-TWO-PANES-RESPONSE.md` (this report)

The seven files already untracked at session start, including the brief, were preserved.

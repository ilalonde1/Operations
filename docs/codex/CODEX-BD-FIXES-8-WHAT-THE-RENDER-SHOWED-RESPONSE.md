# BD fixes 8 — what the render showed

Applied 2026-10-14. **Source edits and regression sources only; nothing was built or run.**
No tests, application/model calls, render harness, database tools, migrations, deployments
or service actions were run. Read-only source searches and diff review were performed;
`git diff --check` passed. There is no executed pre-fix failure or post-fix pass claim.

Prior art: searched `docs/codex` for brief 6's response, the existing BusinessDevelopment
regression classes and render fixtures for their gate patterns, and `Schema/34*.sql` for
rollback conventions. Read migration 346 for the report/apply boundary, migration 343 for
the undo-table extended property, and the existing dedup writer. The brief's live-database
measurements were accepted as supplied, not remeasured.

## Applied changes

- **Fix 1:** `Placeholder` now handles TextBox and ComboBox using the same attached
  property. ComboBox hints require no selection, no editable text and no keyboard focus;
  selection, focus, load and editable text changes refresh them. The adorner uses Control
  font/padding/border values, and `RefreshAll` visits both input types. The activity contact
  picker says “Who it was with (optional)” and its tooltip says “pursuit.”
- **Fix 2:** Updated the specified Pursuits tooltips, load messages and analytics headline.
  Outcomes say “recorded here”; earlier Deltek wins appear last and say they are kept out
  of the rate. Pursuit health consistently says “owned live pursuits,” retaining its owner
  and Drafting/Submitted filters. The requested manager-context opening line is updated;
  the remaining AI-context exports and the KNOW Engagements page are untouched.
- **Fix 3:** Added `BdDateDisplay` and `BdDayConverter`, and replaced the twelve specified
  inline date formats. Dates use invariant local `MMM d, yyyy`; activity timestamps add
  `h:mm tt`. The dashboard converter preserves “as of,” and source postings preserve
  “(ingested).” Last touch is widened from 140 to 155 pixels for render review. Existing
  date assertions are updated; calendar-date fixtures use local timestamps so the fixed
  expected day remains valid across time zones.
- **Fix 4:** Both pursuit pages can display `CanonicalOrg.DisplayName` without a linked
  RFP. Four CRM SELECT paths append the read-only `BuyerOrgName` projection. Write OUTPUT
  lists and ordinals 0–29 are unchanged; the reader checks column names before reading
  the optional projection. Row replacement preserves the previous buyer through the
  optional constructor fallback; both save paths use that replacement. A brand-new
  BD-tracking pursuit shows its buyer after the next Refresh. The North Langford render
  fixture carries its buyer, and the health fixtures are ordered 501, 504, 502, 503.
- **Fix 5:** Dashboard Name and Buyer cells now trim with character ellipses and show their
  full values in tooltips. Fit display names the tier and score, spells out rejection,
  preserves unknown tier 4 as “Tier 4,” and handles absent tiers/scores.
- **Fix 6:** The dedup writer and removed-record fixtures use the supplied plain wording.
  Added unapplied migration `347_PlainDismissalReasons.sql`: explicit SQLCMD Apply value,
  report before mutation, dry-run return, three old-value predicates, one apply transaction,
  undo rows written before updates, and a Rollback extended property containing the
  restoring UPDATE. It reports each rule's proposed/actual changes and remaining reasons
  containing `OpportunityKey`. Undo is guarded against overwriting later reason edits;
  conflicting undo rows or changed report candidates abort the transaction.

## Gates authored, not executed

| Gate | EXPECTED versus ACTUAL comparison |
|---|---|
| `Pursuits_EveryInlineInputHasAPlaceholder` | Scans TextBox and ComboBox elements; text boxes need hints, combo boxes need a selection attribute or hint. The old blank contact picker is rejected. |
| `Placeholder_IsAnAttachedPropertyOnTextBoxAndComboBox` | Source handles both types, selection/editable text events and focus suppression, using a Control adorner. Replaces the old TextBox-only gate. |
| `PursuitPages_SayPursuit_AndLiveMeansOneThing` | Literal XAML wording plus mocked `LoadAsync` headlines/statuses: five pursuits and one $41,000 win, separately reported Deltek backfill, undecided/singular/empty cases, and owned-live board labels. Currency and percentage expectations use the current culture. |
| `BdDateDisplay_RendersOneFormat` | Shared formatter output versus independently formatted local values, including UTC/local/unspecified DateTime inputs. |
| `NonDeadlineDates_OnHarnessedScreens_GoThroughBdDateDisplay` | Six source files contain none of the forbidden inline formats; dashboard binding uses the converter. |
| `InlineDateFormats_OnBdScreens_OnlyGoDown` | Remaining literal occurrences stay at or below the counted baseline, with per-file counts in failures. |
| `BdPursuitBuyerRegressionTests` | Board SQL fallback, four SELECT projections, unchanged write OUTPUT scope, non-throwing optional-column lookup, and row buyer precedence/fallback. |
| `NewRfps_LongNamesTrimWithEllipsis` | Name and Buyer column XAML sets ellipsis trimming and matching tooltips. |
| `FitDisplay_NamesTheTierAndTheScore` | High/Medium/Low, rejection, undefined tier, null tier, and missing scores produce the required words. |
| `DismissalReasons_AreNotDeveloperSentences` | Writer uses plain wording; migration contains the Apply guard, three distinguishing predicates, undo table and Rollback property. |

Existing posting-date and removed-banner assertions were updated, not deleted. The cold-meaning
and deadline-display gates remain. The render harness summary now explicitly states that
hand-ordered fixtures cannot prove SQL ordering and that impossible fixture values render
as though real.

## Count discrepancy and limits

The source inventory found **64 occurrences in 23 files**, not the brief's 63, after the twelve
specified replacements. The command used was `rg --count-matches 'yyyy-MM-dd|MMM d, yyyy'`
over the three specified directories, limited to `.cs`/`.xaml` and excluding
`OpportunityDeadlineDisplay.cs` and `BdDateDisplay.cs`; the per-file counts were summed.
The ratchet therefore uses **64**. These remaining sites were counted, not edited.

The wording gate skips comments and markup-extension values: `SelectedEngagementStageDisplay`
is a binding identifier, not visible wording. Text supplied by bindings, item labels such as
`ActivityTypeOptions`, and strings outside the named view-model properties can escape.

These are source and pure-property gates, not visual or database evidence. An off-box adorner,
a null bound ComboBox selection, clipped dates or titles, a wrong SQL join key, null names on
merged organizations, an incorrect ORDER BY, or invalid migration SQL can escape. Culture date
formats such as `ToString("d")` are outside the literal-format scan. Actual layouts, live buyer
rows, and the supplied migration counts (421 / 1 / 20; expected remaining `OpportunityKey`
reasons zero after apply) still require Claude's checks.

Claude owns the Debug build, filtered regression execution including negative controls,
five-screen rendering and PNG review, migration dry run/apply/check, and local deployment.
Ian has no execution step in this handoff. No commit was made.

## Files touched

- `Kor.Operations.App/Controls/Placeholder.cs`
- `Kor.Operations.App/Crm/CrmView.xaml`
- `Kor.Operations.App/Crm/CrmViewModel.cs`
- `Kor.Operations.App/Crm/CrmEngagementRowView.cs`
- `Kor.Operations.App/Crm/CrmActivityRowView.cs`
- `Kor.Operations.App/Crm/CrmOutcomeDialog.xaml`
- `Kor.Operations.App/Opportunities/OpportunitiesView.xaml`
- `Kor.Operations.App/Opportunities/OpportunityDetailViewModel.cs`
- `Kor.Operations.App/BusinessDevelopment/BdDateDisplay.cs` (new)
- `Kor.Operations.App/Converters/BdDayConverter.cs` (new)
- `Kor.Operations.App/BusinessDevelopment/Workspace/OverwatchView.xaml`
- `Kor.Operations.App/BusinessDevelopment/Workspace/OverwatchViewModel.cs`
- `Kor.Operations.App/BusinessDevelopment/Workspace/OverwatchRowView.cs`
- `Kor.Operations.App/BusinessDevelopment/Workspace/BdWorkspaceWindow.xaml`
- `Kor.Operations.App/BusinessDevelopment/Workspace/DashboardView.xaml`
- `Kor.Opportunities.Core/Models/CrmEngagement.cs`
- `Kor.Opportunities.Data/Crm/SqlCrmEngagementStore.cs`
- `Kor.Opportunities.Data/Crm/SqlPursuitOverwatchStore.cs`
- `Kor.Opportunities.Data/Schema/347_PlainDismissalReasons.sql` (new)
- `tools/BdOpportunityDedup/Program.cs`
- `Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdScreensRenderTests.cs`
- `Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdCrmClarityRegressionTests.cs`
- `Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdPresentationRegressionTests.cs`
- `Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/OpportunityDetailRegressionTests.cs`
- `Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdDashboardClarityRegressionTests.cs`
- `Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdPursuitBuyerRegressionTests.cs` (new)
- `docs/codex/CODEX-BD-FIXES-8-WHAT-THE-RENDER-SHOWED-RESPONSE.md` (this report)

The four files already untracked at session start, including the brief, were preserved.

# Codex — Make the BD screens show what the record holds, in words the reader can use

**DO NOT BUILD. DO NOT RUN TESTS. DO NOT RUN ANY TOOL AGAINST A DATABASE.** Make the edits,
write the migration as a file, add the regression gates as source, and ping when applied.
Claude builds, runs the filtered test classes, renders the five screens and reads the PNGs,
applies the migration (dry run, apply, check) and deploys the local install. Ian runs nothing.

## Why this exists

On 2026-10-14 the render harness (`BdScreensRenderTests`) was run and its five PNGs were read.
Six things were wrong that the string gates had passed. They share one shape: **a screen
shows something other than what the record holds, or names it in a word the reader cannot
use.** CLAUDE.md rule 11 applies to two of them directly: a gate named wider than what it
checks ("every inline input", checked on TextBox only) and a harness fixture that the store
could never return.

Measured on the live database and in the source on 2026-10-14 at commit `9bc9bb22`. Use these
as given; do not remeasure, do not quantify beyond them.

| Fact | Value |
|---|---|
| Live pursuits (`Stage` Drafting=1 or Submitted=3) | 101, and 101 of 101 have an owner |
| …with no linked RFP (`OpportunityId IS NULL`) | 100 of 101; 100 of 100 have `BuyerCanonicalOrgId`, and 100 of 100 resolve to `opportunities.CanonicalOrg.DisplayName` |
| ComboBoxes in the four harnessed XAML files | 5; one (`ActivityContactBox`) has no default and no label |
| Inline date formats on the three harnessed screens | 12 sites in 6 files (the table in Fix 3) |
| Inline date formats elsewhere under `App/Crm`, `App/BusinessDevelopment/Workspace`, `App/Opportunities` | 63 more sites in 23 files (the ratchet in Fix 3) |
| Dismissed rows whose reason is a developer's sentence | 442: `BdOpportunityDedup` 421 (one exact template), `migration-327` 1, `migration-321` 20 |
| `RelevanceTier` values on prime RFPs | 0, 1, 2, 3 and **4** (3 rows). The enum ends at `High = 3` and never had a fourth member |

Line numbers below are as of `9bc9bb22`.

## Fix 1 — the contact picker is a blank box

`Kor.Operations.App/Crm/CrmView.xaml` line 361: `ActivityContactBox` is a ComboBox with no
`SelectedIndex` and no placeholder, so the Pursuits activity-log row renders an empty box between
the date picker and Notes. `Kor.Operations.App/Controls/Placeholder.cs` handles `TextBox` only:
`OnTextChanged` returns for anything else, and `Refresh`, `Show`, `Remove`, `Find`, `RefreshAll`
and `PlaceholderAdorner` are typed `TextBox`. The gate
`BdCrmClarityRegressionTests.Pursuits_EveryInlineInputHasAPlaceholder` matches `<TextBox` only.

- `Placeholder` supports `ComboBox` as well as `TextBox`, same attached property. For a ComboBox
  the hint shows while `SelectedIndex < 0`, and (`!IsEditable` or `Text` is empty), and
  `!IsKeyboardFocusWithin`. Hook `Loaded`, `SelectionChanged`, `GotKeyboardFocus`,
  `LostKeyboardFocus`, and for the editable case `AddHandler(TextBoxBase.TextChangedEvent, …)`.
  The adorner takes a `Control` (font, padding, border read from `Control`). `RefreshAll` visits
  both types. TextBox behaviour stays exactly as it is.
- `ActivityContactBox` gets `controls:Placeholder.Text="Who it was with (optional)"`. Its
  tooltip (line 363) becomes `Who it was with (optional — this pursuit's contacts).` so the line
  is touched once (Fix 2 owns the word).
- Gate: rewrite `Pursuits_EveryInlineInputHasAPlaceholder` to match `<TextBox` **and**
  `<ComboBox` elements in `CrmView.xaml`. A ComboBox passes if it carries `SelectedIndex=`,
  `SelectedItem=`, `SelectedValue=` or `controls:Placeholder.Text=`. The TextBox assertion
  stays. Rename `Placeholder_IsAnAttachedPropertyOnTextBox` to
  `…OnTextBoxAndComboBox` and assert the source handles both types and still hides while
  focused (keep an assertion on `IsKeyboardFocusWithin`). The class summary states: XAML text
  only; it cannot see an adorner drawn off-box; a ComboBox whose bound selection is null at
  runtime would render blank and escape.

## Fix 2 — Pursuits and Pursuit health say "pursuit", and "live" means one thing

The grid is titled Pursuits (since 2026-10-13); the strip under it says "5 engagement(s)", the
footer says "Loaded 5 engagements", five tooltips say "engagement", and "live" means active stage
in the `Live 4` chip but "not Deltek backfill" in `live: 1W / 0L`. The pursuit-health board says
"Every live pursuit" but its store returns only pursuits with an owner; the headline says
"active" and the footer "owned active".

Exact replacements, user-visible strings only:

`Kor.Operations.App/Crm/CrmViewModel.cs`
- line 340 `"— (no live outcomes yet)"` → `"— (none decided yet)"`
- lines 341-343, the backfill clause → `$"  •  {_analytics.BackfillWins} earlier Deltek wins ({fee}) kept out of the rate"`
- line 347 → `$"{n} pursuit{(n == 1 ? "" : "s")}  •  won {won} / lost {lost} recorded here  •  win rate {rate}{avgWonFee}{backfill}"`
  (backfill moves to the end; `n` is `TotalEngagements`)
- line 386 `"Loading engagements…"` → `"Loading pursuits…"`
- line 449 → `"No pursuits yet — promote an opportunity from the Opportunities window to start one."`
- line 450 → `$"Loaded {n} pursuit{(n == 1 ? "" : "s")}."`
- lines 1011-1016 are the AI-context text export, not a screen: leave them.

`Kor.Operations.App/Crm/CrmView.xaml` tooltips at lines 30, 46, 48, 56: the word "engagement"
→ "pursuit" (line 363 is done in Fix 1).
`Kor.Operations.App/Crm/CrmOutcomeDialog.xaml` line 58: "edit the engagement later" → "edit the pursuit later".
`Kor.Operations.App/Opportunities/OpportunitiesView.xaml` line 53 →
`Create a pursuit from the selected opportunity, or open the existing one.`; line 391 →
`Open the Pursuits window scoped to this pursuit.`
`Kor.Operations.App/Crm/BdTrackingView.xaml` is the KNOW group's Engagements page, a different
thing by design (comment at `CrmView.xaml` line 142): **do not touch it.**

Pursuit health keeps its owner filter (`SqlPursuitOverwatchStore.BoardSql`,
`e.OwnerStaffId IS NOT NULL`) and says so, with one word set: *live* = stage Drafting or
Submitted, *owned* = has an owner.
- `Kor.Operations.App/BusinessDevelopment/Workspace/OverwatchView.xaml` line 36 →
  `Every owned live pursuit, coldest first`
- `Kor.Operations.App/BusinessDevelopment/Workspace/OverwatchViewModel.cs` line 80 →
  `{n} owned live pursuit(s)` (same plural rule); lines 108-109 → `No owned live pursuits.` /
  `Loaded {n} owned live pursuits.`; line 189 → `Owned live pursuits (manager board): {n}.`
- `Kor.Operations.App/BusinessDevelopment/Workspace/BdWorkspaceWindow.xaml` line 95 →
  `Manager board — every owned live pursuit across the team, coldest first.`

Gate, in `BdCrmClarityRegressionTests`, new test `PursuitPages_SayPursuit_AndLiveMeansOneThing`:
- In `CrmView.xaml`, `CrmOutcomeDialog.xaml`, `OpportunitiesView.xaml`: no `Content=`,
  `ToolTip=`, `Text=` or `Header=` attribute value contains "engagement" (case-insensitive;
  regex over attribute values, the way the placeholder test scans elements).
- `CrmViewModel.AnalyticsHeadline`: the `Analytics` setter is private, so build the view model
  as `BdScreensRenderTests.RenderPursuits` does (mocked `ICrmEngagementStore` returning
  fixtures, mocked `IOpportunityStore`) and call `LoadAsync`. With 4 live pursuits and 1 Won
  at `ProposedFee` 41000 the headline is exactly
  `5 pursuits  •  won 1 / lost 0 recorded here  •  win rate 100%  •  avg won fee $41,000`
  (currency via `CultureInfo.CurrentCulture`, as today; assert the parts, not the symbol).
  Add one Won row with `ExternalSource = "Deltek.CustomProposal"` and assert
  `1 earlier Deltek wins` and `kept out of the rate` appear and "engagement" and "live" do not.
- `OverwatchViewModel` after `LoadAsync` on a mocked `IPursuitOverwatchStore` with 4 rows:
  `Headline` starts with `4 owned live pursuits`; `StatusMessage` is
  `Loaded 4 owned live pursuits.`; `OverwatchView.xaml` contains
  `Every owned live pursuit, coldest first`; `BdWorkspaceWindow.xaml` contains
  `every owned live pursuit`.
- `PursuitHealth_SaysWhatColdMeans` keeps passing ("coldest first" is still there).
- Summary states: XAML and two view-model strings only; item labels such as
  `ActivityTypeOptions`, and strings built outside `AnalyticsHeadline`/`StatusMessage`/`Headline`,
  are not checked; identifiers and comments containing "engagement" are not UI and not checked.

## Fix 3 — one way to write a date on these screens

The RFP window shows `Sep 21, 2026` in its header and `2026-09-21` in its source-postings grid.
Pursuit health is all ISO. Pursuits shows `Sep 9, 2026`. Deadlines already go through
`OpportunityDeadlineDisplay` (pinned by `BdPresentationRegressionTests.DeadlineBindingsUseSharedDisplay`);
every other date is formatted inline.

Add `Kor.Operations.App/BusinessDevelopment/BdDateDisplay.cs` (static, beside `StaffDisplay.cs`):
- `string Day(DateTimeOffset value)` → local time, `"MMM d, yyyy"`, `CultureInfo.InvariantCulture` → `Sep 9, 2026`.
- `string Day(DateTime value)` → same; `Kind.Utc` is converted to local, other kinds are taken as local.
- `string DayAndTime(DateTimeOffset value)` → local time, `"MMM d, yyyy h:mm tt"` → `Sep 9, 2026 12:17 PM`.

Use them at these 12 sites. They are every inline date on the three harnessed screens.

| File | Line | Today | Becomes |
|---|---|---|---|
| `Crm/CrmEngagementRowView.cs` | 56 | `OpenedDisplay` yyyy-MM-dd | `Day` |
| `Crm/CrmEngagementRowView.cs` | 58 | `UpdatedDisplay` inline "MMM d, yyyy" | `Day` |
| `Crm/CrmActivityRowView.cs` | 16 | `OccurredDisplay` "yyyy-MM-dd HH:mm" | `DayAndTime` |
| `Crm/CrmViewModel.cs` | 377 | Deltek line "last opened yyyy-MM-dd" | `Day` |
| `Crm/CrmViewModel.cs` | 656 | warmth "(as of yyyy-MM-dd)" | `Day` |
| `Crm/CrmViewModel.cs` | 688 | grab/claim history lines | `Day` |
| `BusinessDevelopment/Workspace/OverwatchRowView.cs` | 85 | `LastTouchDisplay` | `Day` + ` (source)` |
| `BusinessDevelopment/Workspace/OverwatchRowView.cs` | 87 | `OpenedDisplay` | `Day` |
| `Opportunities/OpportunityDetailViewModel.cs` | 79 | removed-banner date | `Day` |
| `Opportunities/OpportunityDetailViewModel.cs` | 90 | `PostedDisplay` | `Day` |
| `Opportunities/OpportunityDetailViewModel.cs` | 183 | source-posting `Posted` (+ ` (ingested)`) | `Day`, keep the suffix |
| `BusinessDevelopment/Workspace/DashboardView.xaml` | 372 | `StringFormat=as of {0:yyyy-MM-dd}` on `RefreshedAtUtc` | a converter |

The dashboard item is `PriorityActionRow` (`Kor.Opportunities.Data/Intel/IntelReadModels.cs`
line 86, a Data-project record), so the XAML site uses a converter: add
`Kor.Operations.App/Converters/BdDayConverter.cs` (`IValueConverter`, `DateTimeOffset` or
`DateTime` → `BdDateDisplay.Day`, anything else → `""`), register it in
`DashboardView.xaml`'s `UserControl.Resources` (line 19; the `converters:` namespace pattern is
`PMTools/WorkloadMeetingWindow.xaml` line 27), and bind
`Text="{Binding RefreshedAtUtc, Converter={StaticResource BdDay}, StringFormat=as of {0}}"`.

Existing assertions to update, not delete: `OpportunityDetailRegressionTests` line 48
`"2026-10-05"` → `"Oct 5, 2026"`; line 66 `"2026-10-07 (ingested)"` → `"Oct 7, 2026 (ingested)"`.
`OverwatchView.xaml`'s Last touch column width was set for `2026-09-09 (activity)`;
`Sep 9, 2026 (activity)` is one character longer. Widen it if needed; Claude reads the PNG.

Gate, in `BdPresentationRegressionTests` beside `DeadlineBindingsUseSharedDisplay`:
- `BdDateDisplay_RendersOneFormat`: for `2026-09-09T19:17:00Z`, `Day` equals the same instant
  converted with `.ToLocalTime()` and formatted `"MMM d, yyyy"` invariant (compute the expected
  from the same conversion so the test holds in any zone); `DayAndTime` likewise with
  `"MMM d, yyyy h:mm tt"`.
- `NonDeadlineDates_OnHarnessedScreens_GoThroughBdDateDisplay`: the six files in the table
  contain no `"yyyy-MM-dd`, no `:yyyy-MM-dd` and no `"MMM d, yyyy` (source-file scan, as the
  clarity tests' `Source()` helper does).
- `InlineDateFormats_OnBdScreens_OnlyGoDown`: count occurrences of `yyyy-MM-dd` and
  `MMM d, yyyy` in `.cs` and `.xaml` files under `Kor.Operations.App/Crm`,
  `Kor.Operations.App/BusinessDevelopment/Workspace` and `Kor.Operations.App/Opportunities`,
  excluding `BdDateDisplay.cs` and `OpportunityDeadlineDisplay.cs` (its `Stored` format is
  deliberate and pinned at line 164 of this class). Assert `count <= 63`, and the failure
  message names each file with its count. If your count after the edits is not 63, write the
  number you counted; Claude re-counts with `grep -rn` and the two must agree.
- Summary states: literals only; `StringFormat={}{0:d}` or `ToString("d")` culture formats
  would escape; nothing is rendered; the other 63 sites are counted, not fixed.

## Fix 4 — a pursuit with no RFP shows no buyer, on both pages

`CrmEngagementRowView.Buyer` (line 36) is `Opportunity?.BuyerName ?? ""`.
`SqlPursuitOverwatchStore.BoardSql` (line 105) is `COALESCE(o.BuyerName, N'')`. 100 of 101 live
pursuits have no RFP, so both pages show a blank buyer for 100 of 101 rows, while every one of
them has a `BuyerCanonicalOrgId` that resolves to a name. Table `opportunities.CanonicalOrg`,
key `Id`, name column `DisplayName`; `Kor.Opportunities.Data/MajorProjects/SqlPursuitBriefStore.cs`
line 111 shows the join shape.

- `BoardSql`: add `LEFT JOIN opportunities.CanonicalOrg co ON co.Id = e.BuyerCanonicalOrgId`
  and make the column `COALESCE(o.BuyerName, co.DisplayName, N'') AS Buyer`. Ordinals unchanged.
- `Kor.Opportunities.Core/Models/CrmEngagement.cs`: add `public string? BuyerOrgName { get; init; }`
  with a doc comment: a read-only projection of `CanonicalOrg.DisplayName`, populated by the
  SELECT paths only, never written.
- `Kor.Opportunities.Data/Crm/SqlCrmEngagementStore.cs`: a second constant
  `ListColumns = AllColumns + ", (SELECT co.DisplayName FROM opportunities.CanonicalOrg co WHERE co.Id = BuyerCanonicalOrgId) AS BuyerOrgName"`,
  used by the four `SELECT {AllColumns} FROM opportunities.CrmEngagements` statements (lines
  49, 70, 85, 220). The INSERT and UPDATE `OUTPUT inserted.…` lists (lines 128-136, 283-291) are
  untouched and do not return it. `MapReader` reads `BuyerOrgName` only when the reader has a
  column of that name (loop `0..FieldCount-1` comparing `GetName(i)`; never `GetOrdinal`, which
  throws); otherwise null. Ordinals 0-29 read exactly as today.
- `CrmEngagementRowView`: `Buyer` → `Opportunity?.BuyerName ?? Engagement.BuyerOrgName ?? ""`.
  Where `CrmViewModel` rebuilds a row from a store write (line 939, and the insert path), the
  returned engagement has no `BuyerOrgName`; pass the previous row's value through an optional
  third constructor parameter `string? buyerOrgName = null`, used as a fallback before `""`. A
  brand-new BD-tracking pursuit shows its buyer after the next Refresh; say so in the summary.
- `BdScreensRenderTests`: the Pursuits fixture's North Langford engagement gets
  `BuyerOrgName = "School District 62 (Sooke)"` (the store would now return it). The Overwatch
  fixture rows are reordered as `BoardSql`'s ORDER BY would return them, ascending by the fused
  touch floored at `OpenedAtUtc`: 501 (touched 35 d ago), 504 (never touched, opened 15 d ago),
  502 (12 d), 503 (1 d). Today the PNG reads 35, 12, 1, 15 under a title that says coldest
  first. The class summary gains two sentences: row ORDER is the store's, fixtures are
  hand-ordered as the store's ORDER BY would return them, and a wrong ORDER BY is a fault this
  harness would NOT catch; a fixture value the store could not return would render as if real.

Gate, new class `BdPursuitBuyerRegressionTests`:
- `BoardSql` contains `LEFT JOIN opportunities.CanonicalOrg` and `COALESCE(o.BuyerName, co.DisplayName`.
- `SqlCrmEngagementStore` source: the list SQL contains `AS BuyerOrgName`; the text between
  `OUTPUT` and `VALUES` does not; `MapReader` does not contain `GetOrdinal("BuyerOrgName")`.
- `CrmEngagementRowView`: no Opportunity and `BuyerOrgName = "X"` → `Buyer == "X"`; Opportunity
  with `BuyerName = "Y"` → `"Y"`; neither → `""`; the constructor fallback parameter is used
  when the engagement's own value is null.
- Summary states: no SQL is executed; a wrong join key, or a `DisplayName` that is null for a
  merged org, would escape; Claude proves the live rows on the running app and in the PNGs.

## Fix 5 — the dashboard cuts a title mid-word; the RFP window's fit is a bare number

- `Kor.Operations.App/BusinessDevelopment/Workspace/DashboardView.xaml` line 268 (`Name`) and
  269 (`Buyer`): add a `DataGridTextColumn.ElementStyle` with `TextTrimming="CharacterEllipsis"`
  and a `ToolTip` bound to the same property, the way `Crm/ClientIntelligenceWindow.xaml` line
  270 does. The Closing soon list already trims (line 212).
- `Kor.Operations.App/Opportunities/OpportunityDetailViewModel.cs` `FitDisplay` (lines 105-107):
  `High`/`Medium`/`Low` → `{Tier} fit (score {n})`, e.g. `Low fit (score 8)`; `HardReject` →
  `Rejected by the relevance gate (score {n})`; a value `Enum.IsDefined` rejects (the database
  holds 4 on 3 rows) → `Tier {int} (score {n})`; null tier → `not scored`; a tier with no score
  → the words without the parenthesis. Score printed with `"0"` invariant, as today.
- Gates: `BdDashboardClarityRegressionTests.NewRfps_LongNamesTrimWithEllipsis` (the Name
  column's ElementStyle sets `TextTrimming` to `CharacterEllipsis`);
  `OpportunityDetailRegressionTests.FitDisplay_NamesTheTierAndTheScore` (the five cases above).
  Summaries state: XAML text and a pure property; the rendered width is only seen in the PNG.

## Fix 6 — the dismissal reason is a developer's sentence

The removed-record banner prints `DismissedReason` verbatim. The dedup tool writes
`Same RFP as X; held separately because OpportunityKey carries the source prefix. Observations and child rows merged onto that record.`
(`tools/BdOpportunityDedup/Program.cs` line 283). 421 rows carry exactly that template, 1 row
(`migration-327`, key `BIDSTEND-2026-RFP-IP-061`) a cousin beginning
`Same RFP as APCALLBU-AB-2026-06377 (`, and 20 rows (`migration-321`) begin
`Duplicate of the same application under its DP file number; created when the OpportunityKey derivation changed`.

- Writer, line 283 → `N'Duplicate of ' + @keepKey + N', the record kept. Its postings and history were merged there.'`
- Migration `Kor.Opportunities.Data/Schema/347_PlainDismissalReasons.sql` in the shape of 346:
  header with HOW TO RUN, `-v Apply=0|1` that fails closed when omitted (no SQLCMD default),
  `SET NOCOUNT/XACT_ABORT/QUOTED_IDENTIFIER/ANSI_NULLS`, report first, `RETURN` on dry run, one
  transaction on apply. Idempotent on the VALUE: each UPDATE's WHERE matches only the old
  wording, so a second run changes 0 rows. Undo: create
  `opportunities.DismissalReasonRewrite347 (OpportunityId bigint primary key, OldReason nvarchar(max), NewReason nvarchar(max), RewrittenAtUtc datetimeoffset not null)`
  if absent, fill it before the UPDATEs, and add an extended property `Rollback` on that table
  whose value is the UPDATE that restores `OldReason` from it. Three rewrites:
  1. `DismissedBy = N'BdOpportunityDedup' AND DismissedReason LIKE N'Same RFP as %; held separately because OpportunityKey carries the source prefix. Observations and child rows merged onto that record.'`
     → `N'Duplicate of ' + SUBSTRING(DismissedReason, 13, CHARINDEX(N';', DismissedReason) - 13) + N', the record kept. Its postings and history were merged there.'`
     (expected 421; `LEN('Same RFP as ') = 12`).
  2. `DismissedBy = N'migration-327' AND OpportunityKey = N'BIDSTEND-2026-RFP-IP-061' AND DismissedReason LIKE N'Same RFP as APCALLBU-AB-2026-06377 (%'`
     → `N'Duplicate of APCALLBU-AB-2026-06377 (West Campus Recreation IPD, structural consultant), the record kept. Its postings, deadline and buyer were merged there.'`
     (expected 1).
  3. `DismissedBy = N'migration-321' AND DismissedReason LIKE N'Duplicate of the same application under its DP file number; created when the OpportunityKey derivation changed%'`
     → `N'Duplicate of the same Vancouver application under its DP file number. A second record was created on 2026-09-10 when the ingest changed how it keys applications.'`
     (expected 20).
  Report, in both modes: rows each rule would change or did change, and rows still containing
  `OpportunityKey` in `DismissedReason` (expected 0 after apply).
- Fixtures that carry the old wording, so the PNG and the gate show production's words:
  `BdScreensRenderTests` line 235 and `OpportunityDetailRegressionTests` line 81 →
  `Duplicate of BCBID-233225, the record kept. Its postings and history were merged there.`;
  the assertion at `OpportunityDetailRegressionTests` line 86 →
  `StartsWith("Removed from the pipeline Oct 8, 2026 by BdOpportunityDedup: Duplicate of BCBID-233225"`.
- Gate, `OpportunityDetailRegressionTests.DismissalReasons_AreNotDeveloperSentences`: the dedup
  tool's `Program.cs` contains no `OpportunityKey carries` and contains
  `Duplicate of ' + @keepKey`; the migration file contains `$(Apply)`, the three predicates'
  distinguishing strings, `DismissalReasonRewrite347` and `Rollback`. Summary states: text
  gates only; the row counts are proven when Claude runs the file with `Apply=0` and then `1`.

## What every fix must come with

- A regression gate, EXPECTED vs ACTUAL, failing before and passing after, whose summary states
  what it covers, what it does not, and one same-class fault it would miss (CLAUDE.md rule 11).
  Gates live in `Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/`; internals
  of the App are visible to that project; Moq 4.20, xUnit 2.9.
- An existing gate that pins behaviour this brief changes is rewritten to the new behaviour,
  never deleted.
- `using System.IO;` in any file that reads files: the UseWPF projects lack the implicit using.
  Guard nullable derefs in tests. The repo's recurring Codex misses are exactly these two.
- `git diff --check` clean. One response file beside this brief, named
  `CODEX-BD-FIXES-8-WHAT-THE-RENDER-SHOWED-RESPONSE.md`, in the shape of brief 6's response:
  applied changes per fix, gates authored, limits, files touched. Say plainly that nothing was
  built or run.

## Out of scope — do not do these

- The other 63 inline date sites (the ratchet counts them).
- The 42 ComboBoxes on BD screens other than the four harnessed files.
- `BdTrackingView.xaml` (the Engagements page) and its view model.
- The AI-context text exports in `CrmViewModel.cs` (lines 1000-1020) and
  `OverwatchViewModel.cs` beyond line 189.
- Why 3 prime RFPs carry tier 4 (a data question Claude owns).
- Retiring the two live duplicate pairs still listed (the dedup tool's job, not the UI's).
- Any dashboard column other than Name and Buyer.

## The bound — files you will read (about 330 KB)

```
Kor.Operations.App/Controls/Placeholder.cs                                    5 KB
Kor.Operations.App/Crm/CrmView.xaml                                          31 KB
Kor.Operations.App/Crm/CrmViewModel.cs                                       48 KB
Kor.Operations.App/Crm/CrmEngagementRowView.cs                                4 KB
Kor.Operations.App/Crm/CrmActivityRowView.cs
Kor.Operations.App/Crm/CrmOutcomeDialog.xaml                                  3 KB
Kor.Operations.App/Crm/CrmAnalyticsService.cs
Kor.Operations.App/Opportunities/OpportunitiesView.xaml                      34 KB
Kor.Operations.App/Opportunities/OpportunityDetailViewModel.cs               10 KB
Kor.Operations.App/BusinessDevelopment/StaffDisplay.cs                        1 KB
Kor.Operations.App/BusinessDevelopment/Workspace/OverwatchView.xaml           7 KB
Kor.Operations.App/BusinessDevelopment/Workspace/OverwatchViewModel.cs        8 KB
Kor.Operations.App/BusinessDevelopment/Workspace/OverwatchRowView.cs          5 KB
Kor.Operations.App/BusinessDevelopment/Workspace/BdWorkspaceWindow.xaml       9 KB
Kor.Operations.App/BusinessDevelopment/Workspace/DashboardView.xaml          25 KB
Kor.Operations.App/Converters/BoolToCollapsedConverter.cs
Kor.Opportunities.Core/Models/CrmEngagement.cs
Kor.Opportunities.Data/Crm/SqlCrmEngagementStore.cs                          23 KB
Kor.Opportunities.Data/Crm/SqlPursuitOverwatchStore.cs                       12 KB
Kor.Opportunities.Data/Intel/IntelReadModels.cs                              (PriorityActionRow only)
Kor.Opportunities.Data/Schema/346_PrimePipelineCurrency.sql                   5 KB  (the shape)
tools/BdOpportunityDedup/Program.cs                                          19 KB
Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdScreensRenderTests.cs            20 KB
Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdCrmClarityRegressionTests.cs      3 KB
Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdPresentationRegressionTests.cs   16 KB
Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/OpportunityDetailRegressionTests.cs 7 KB
Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdDashboardClarityRegressionTests.cs 7 KB
```

## Files you will create

```
Kor.Operations.App/BusinessDevelopment/BdDateDisplay.cs
Kor.Operations.App/Converters/BdDayConverter.cs
Kor.Opportunities.Data/Schema/347_PlainDismissalReasons.sql
Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdPursuitBuyerRegressionTests.cs
docs/codex/CODEX-BD-FIXES-8-WHAT-THE-RENDER-SHOWED-RESPONSE.md
```

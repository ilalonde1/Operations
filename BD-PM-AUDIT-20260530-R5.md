# BD-PM-AUDIT-20260530-R5.md

## Round 40 verification  per-fix
- R4-T2.001: closed. `WorkloadMeetingPanelViewModel.RefreshPriorityProjects()` now snapshots existing enriched names when called without an enricher at `Kor.Operations.App/PMTools/WorkloadMeetingPanelViewModel.cs:163`, rejects the `ProjectName == Wbs1` fallback as non-enriched at `WorkloadMeetingPanelViewModel.cs:181`, and applies preserved `ProjectName` / `PmName` into the rebuilt rows at `WorkloadMeetingPanelViewModel.cs:198`. In the post-save path, `UpsertPriorityFromUiAsync()` still rebuilds `CurrentProjects` and calls `RefreshPriorityProjects()` last at `WorkloadMeetingPanelViewModel.cs:303`; that final call now preserves the capacity window's prior enrichment instead of clobbering it. Duplicate `Wbs1` is guarded by the store schema's `UQ_WorkloadMeetingProjects_MeetingWbs1` at `Kor.Operations.Data/SqlWorkloadMeetingStore.cs:74`, and `GetProjectsForMeetingAsync()` round-trips `Notes` at `SqlWorkloadMeetingStore.cs:170`.
- R4-T2.002: closed. `PriorityComboBox_SelectionChanged()` captures `attemptedPriority` before awaiting at `Kor.Operations.App/PMTools/PmCapacityWindow.xaml.cs:426`, and on failure skips the revert if `row.MeetingPriority` has moved on at `PmCapacityWindow.xaml.cs:435`. That covers the rapid P1/P2 cases: a stale failed P1 no longer overwrites a later P2 success, both-fail eventually reverts only the still-current P2 attempt, and P1-success/P2-fail leaves the row at the persisted P1 value. The revert still runs under `_isSyncingMeetingPriorities` at `PmCapacityWindow.xaml.cs:442`, so the programmatic row mutation does not re-enter the ComboBox save path.

## Round 40 NEW findings (introduced by the fixes themselves)
### T1 (Critical / High)
- No new T1 findings.

### T2 (Medium)
- No new T2 findings.

### T3 (Low)
- No new T3 findings.

## Regression check
- Round 39: clean.
- T1.001: `PmCapacityWindow` still stores singleton-VM handlers in fields at `Kor.Operations.App/PMTools/PmCapacityWindow.xaml.cs:32` and unsubscribes/nulls them in `Window_Closing` at `PmCapacityWindow.xaml.cs:258`.
- T2.003: `OperationsApp.OnExit` still disposes the DI root via `(_services as IDisposable)?.Dispose()` at `Kor.Operations.App/App.xaml.cs:135`.
- T2.004: `PmToolsChooserWindow.OpenOrActivate<T>()` still scans `Application.Current.Windows.OfType<T>()` at `Kor.Operations.App/PMTools/PmToolsChooserWindow.xaml.cs:56`.
- T3.001: `PmCapacityWindow.xaml` remains free of `Button.Accent`, `Button.Danger`, and `SortHeaderButton` definitions/references; `rg` returned no hits in that file.

- Round 38: clean.
- `AppModule` still registers `WorkloadMeetingPanelViewModel` and `PmToolsViewModel` as singletons at `Kor.Operations.App/CompositionModules/AppModule.cs:100` and `AppModule.cs:107`.
- `AppModule` still registers `PmToolsChooserWindow`, `WorkloadMeetingWindow`, and `PmCapacityWindow` as transients at `Kor.Operations.App/CompositionModules/AppModule.cs:166`.
- `HomeWindow.OpenPMTools_Click` still resolves `PMTools.PmToolsChooserWindow`, not the retired legacy window, at `Kor.Operations.App/HomeWindow.xaml.cs:146`.
- `PmCapacityWindow.xaml` still has the five-row inner capacity grid and `ScrollViewer Grid.Row="4"` at `Kor.Operations.App/PMTools/PmCapacityWindow.xaml:87` and `PmCapacityWindow.xaml:163`.

- Round 37: clean.
- SqlCrmEngagementStore: `AllColumns` still includes `BuyerCanonicalOrgId`, `Region`, `ProposalsSubmittedCad`, `ProposalsAcceptedCad`, and `PotentialProjects` at `Kor.Opportunities.Data/Crm/SqlCrmEngagementStore.cs:20`.
- BdCanonicalDedup: `FkTargets` still includes `new("CrmEngagements", "BuyerCanonicalOrgId")` at `tools/BdCanonicalDedup/Program.cs:75`.
- BdResearchImport: `ImportBdTrackingAsync` still calls `DeleteBdTrackingChildrenAsync` at `tools/BdResearchImport/Program.cs:4019`.
- BdTrackingViewModel: linked-project SQL still filters `m.RetiredAtUtc IS NULL` at `Kor.Operations.App/Crm/BdTrackingViewModel.cs:379`.
- CompetitionInfoView: async-void `View_Loaded` remains wrapped in try/catch at `Kor.Operations.App/Opportunities/CompetitionInfoView.xaml.cs:41`.
- OpportunitiesViewModel: outer detail-load catch still logs via Serilog at `Kor.Operations.App/Opportunities/OpportunitiesViewModel.cs:599`.
- extract.py: paths are still computed from `__file__` at `tools/BdTrackingImport/extract.py:45`.
- Additional R2/R3 anchors also remain in place: `FilteredEngagements` is still a materialized `ObservableCollection` at `BdTrackingViewModel.cs:81`, grid activity/contact lookups still use `OUTER APPLY` at `BdTrackingViewModel.cs:220`, main BD-tracking load failures still log at `BdTrackingViewModel.cs:286`, cross-link duplicate-key races are still caught at `tools/BdResearchImport/Program.cs:4234`, and filter status still updates via `UpdateFilteredStatus()` at `BdTrackingViewModel.cs:127`.

## Summary
- Round 40 fixes closed: 2 / 2
- New findings: T1=0, T2=0, T3=0

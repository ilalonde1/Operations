# BD-PM-AUDIT-20260530-R4.md

## Round 39 verification  per-fix
- T1.001: closed. `PmCapacityWindow` now stores the singleton-VM event handlers in fields at `Kor.Operations.App/PMTools/PmCapacityWindow.xaml.cs:32` and unsubscribes/nulls both at `Kor.Operations.App/PMTools/PmCapacityWindow.xaml.cs:258`; the lambdas do not reference the handler fields, so nulling is not a re-entrancy hazard.
- T2.001: new sub-issue (see below). The meeting VM now owns `PriorityProjects` and populates it on load/selection, but the interaction with the capacity window's enrichment pass is ordered incorrectly after a priority save.
- T2.002: new sub-issue (see below). The save-failure path now reverts the visible row on a failed save, but the revert is not guarded against overlapping saves for the same `Wbs1`.
- T2.003: closed. `OperationsApp.OnExit` disposes the DI root at `Kor.Operations.App/App.xaml.cs:135`, and `WorkloadMeetingPanelViewModel.Dispose()` is sync-bounded to `_disposeCts.Cancel()`/`Dispose()` at `Kor.Operations.App/PMTools/WorkloadMeetingPanelViewModel.cs:741`.
- T2.004: closed. `PmToolsChooserWindow.OpenOrActivate<T>()` scans `Application.Current.Windows.OfType<T>()`, activates an existing child window, or shows exactly one transient child before closing the chooser at `Kor.Operations.App/PMTools/PmToolsChooserWindow.xaml.cs:56`.
- T3.001: closed. `PmCapacityWindow.xaml` no longer defines or references `Button.Accent`, `Button.Danger`, or `SortHeaderButton`; `rg` against that file returns no `StaticResource` or `x:Key` hits for those keys.

## Round 39 NEW findings (introduced by the fixes themselves)
### T1 (Critical / High)
- No new T1 findings.

### T2 (Medium)
- [T2.001] `Kor.Operations.App/PMTools/WorkloadMeetingPanelViewModel.cs:266`  Capacity-enriched priority rows are overwritten after every successful priority save. **Why it bites:** `UpsertPriorityFromUiAsync` clears and re-adds `CurrentProjects`, which fires the capacity window's `CurrentProjects.CollectionChanged` handler during the mutation; that handler calls `RefreshPriorityProjects(enrich)` from `PmCapacityWindow.SyncMeetingPrioritiesToRows()`. After all collection events have fired, the VM then calls `RefreshPriorityProjects()` with no enricher, so the final `PriorityProjects` rows fall back to `ProjectName = p.Wbs1` and empty `PmName`. **Evidence:** `WorkloadMeetingPanelViewModel.cs:266-275`:
  ```csharp
  await _dispatcher.InvokeAsync(() =>
  {
      CurrentProjects.Clear();
      foreach (var project in projects) CurrentProjects.Add(project);
      RefreshPriorityProjects();
  });
  ```
  and `PmCapacityWindow.xaml.cs:306-309`:
  ```csharp
  _meetingPanel.RefreshPriorityProjects(wbs1 =>
      projectLookup.TryGetValue(wbs1, out var proj)
          ? ((string?)proj.Name, (string?)proj.Pm)
          : (null, null));
  ```
  **Fix:** Rebuild `CurrentProjects` without exposing intermediate collection events to the enrichment path, or raise one explicit post-refresh signal after the VM projection and let the capacity window enrich then. A simpler surgical fix is to make `RefreshPriorityProjects` preserve existing enriched names when no enricher is supplied.

- [T2.002] `Kor.Operations.App/PMTools/PmCapacityWindow.xaml.cs:424`  Priority save failure revert can clobber a later successful rapid change. **Why it bites:** the handler snapshots `previousPriority`, awaits an unversioned save, and blindly writes the old value back on failure. If the user changes the same project from P1 to P2 while the P1 save is still in flight, a late P1 failure can set `row.MeetingPriority` back to the pre-P1 value even after the P2 save succeeded and refreshed `CurrentProjects`. **Evidence:** `PmCapacityWindow.xaml.cs:424-431`:
  ```csharp
  var previousPriority = _meetingPanel.CurrentProjects
      .FirstOrDefault(p => string.Equals(p.Wbs1, row.Wbs1, StringComparison.OrdinalIgnoreCase))?.Priority ?? 0;

  var ok = await _meetingPanel.UpsertPriorityFromUiAsync(row.Wbs1, priority);
  if (!ok)
  {
      _isSyncingMeetingPriorities = true;
      try { row.MeetingPriority = previousPriority; }
  ```
  **Fix:** Add a per-row save generation or compare the row's current `MeetingPriority` to the failed attempted value before reverting. If the row has moved on, log the failed older save but do not mutate the UI.

### T3 (Low)
- No new T3 findings.

## Round 38 regression check
- All structural moves in place: clean.
- `WorkloadMeetingWindow` still uses the singleton meeting VM as `DataContext` and flushes with `ForceSaveAllAsync` on close without disposing it at `Kor.Operations.App/PMTools/WorkloadMeetingWindow.xaml.cs:32` and `Kor.Operations.App/PMTools/WorkloadMeetingWindow.xaml.cs:164`.
- `PmCapacityWindow.xaml` still has the inner five-row capacity grid with `ScrollViewer Grid.Row="4"` at `Kor.Operations.App/PMTools/PmCapacityWindow.xaml:87` and `Kor.Operations.App/PMTools/PmCapacityWindow.xaml:163`; `MeetingPanel.IsCurrentMeeting` binding is still exposed through the window property at `PmCapacityWindow.xaml.cs:65`.
- The chooser still has two `HomeCardButton` cards for Workload Meeting and PM Capacity & Risk at `Kor.Operations.App/PMTools/PmToolsChooserWindow.xaml:80` and `Kor.Operations.App/PMTools/PmToolsChooserWindow.xaml:99`.
- `AppModule` still registers both PM Tools VMs as singletons and the chooser/child windows as transients at `Kor.Operations.App/CompositionModules/AppModule.cs:100`, `Kor.Operations.App/CompositionModules/AppModule.cs:107`, and `Kor.Operations.App/CompositionModules/AppModule.cs:166`.
- `HomeWindow` still routes the PM Tools card to `PmToolsChooserWindow`, not the retired legacy window, at `Kor.Operations.App/HomeWindow.xaml.cs:139`.

## Round 37 regression check
- All 13 anchors in place: clean.
- T1.001: verified at `Kor.Opportunities.Data/Crm/SqlCrmEngagementStore.cs:20`; `AllColumns` still includes the five BD-tracking columns.
- T1.002: verified at `tools/BdCanonicalDedup/Program.cs:75`; `FkTargets` still includes `new("CrmEngagements", "BuyerCanonicalOrgId")`.
- T1.003: verified at `tools/BdResearchImport/Program.cs:4019`; `ImportBdTrackingAsync` still calls `DeleteBdTrackingChildrenAsync` before child reinsert on existing engagements.
- T1.004: verified against `BD-PM-AUDIT-20260530-R3.md`; no Round 39 PM Tools files touched the BD-tracking resolver/importer correction.
- T1.005: verified at `Kor.Operations.App/Crm/BdTrackingViewModel.cs:379`; linked-project SQL still filters `m.RetiredAtUtc IS NULL`.
- T2.001: verified at `Kor.Operations.App/Opportunities/CompetitionInfoView.xaml.cs:41`; inline `View_Loaded` remains wrapped in try/catch.
- T3.001: verified at `Kor.Operations.App/Crm/BdTrackingViewModel.cs:220`; grid query still uses `OUTER APPLY` instead of scalar SELECT-list subqueries.
- T3.002: verified at `Kor.Operations.App/Crm/BdTrackingViewModel.cs:81`; `FilteredEngagements` remains a materialized `ObservableCollection`.
- T4.001: verified at `tools/BdResearchImport/Program.cs:4234`; cross-link duplicate-key races `2627`/`2601` are still caught and skipped.
- T4.002: verified at `Kor.Operations.App/Crm/BdTrackingViewModel.cs:286`; main load failures still log through `_logger?.LogError`.
- T4.003: verified at `Kor.Operations.App/Opportunities/OpportunitiesViewModel.cs:599`; outer detail-load failure still logs through Serilog.
- T6.001: verified at `Kor.Operations.App/Crm/BdTrackingViewModel.cs:127`; filter changes still update the status line with the filtered count.
- T7.001: verified at `tools/BdTrackingImport/extract.py:45`; extractor paths are still computed from `__file__`.

## Summary
- Round 39 fixes closed: 4 / 6 clean; 6 / 6 original findings are addressed, but T2.001 and T2.002 introduced new edge-case regressions.
- New findings: T1=0, T2=2, T3=0
- Round 38 regression: clean
- Round 37 regression: clean

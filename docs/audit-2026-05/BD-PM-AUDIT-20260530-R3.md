# BD-PM-AUDIT-20260530-R3.md

## Round 38 findings

### T1 (Critical / High)

- [T1.001] `Kor.Operations.App/PMTools/PmCapacityWindow.xaml.cs:46`  `PmCapacityWindow` leaks one closed window per open. **Why it bites:** the constructor subscribes lambdas to singleton `_meetingPanel.PropertyChanged` and singleton `_meetingPanel.CurrentProjects.CollectionChanged`; both lambdas call the instance method `SyncMeetingPrioritiesToRows()`, so the delegate target closes over `this`. `Window_Closing` unregisters AI context and flushes notes, but never removes either singleton subscription, so every close/reopen leaves the closed window, its visual tree, and its `PmToolsViewModel` references reachable from the singleton VM. **Repro:** open PM Capacity & Risk, close it, reopen it several times, then change a meeting priority; every leaked handler runs `SyncMeetingPrioritiesToRows()` against its closed window instance. **Fix:** store the two delegates in fields and unsubscribe them in `Window_Closing` or `Closed`; also clear `_cts` after cancellation.

### T2 (Medium)

- [T2.001] `Kor.Operations.App/PMTools/WorkloadMeetingPanelViewModel.cs:643`  `WorkloadMeetingWindow` does not populate its own priority list on load. **Why it bites:** `WorkloadMeetingWindow` binds to `PriorityProjects` and shows the empty state when `HasPriorityProjects` is false, but `LoadAsync`/`ApplyMeetingSelectionAsync` only populate `CurrentProjects`. The only code that projects `CurrentProjects` into `PriorityProjects` is `PmCapacityWindow.SyncMeetingPrioritiesToRows()`, so opening Workload Meeting by itself can show "No priority projects yet" even when the database has meeting priorities. **Repro:** create priorities, close both windows, restart or open only Workload Meeting; `_meetingPanel.LoadAsync()` loads `CurrentProjects`, but `PriorityProjects` remains whatever the singleton last held, often empty. **Fix:** move the `CurrentProjects -> PriorityProjects` projection into `WorkloadMeetingPanelViewModel` or have `WorkloadMeetingWindow` perform the projection on load without depending on the capacity window.

- [T2.002] `Kor.Operations.App/PMTools/PmCapacityWindow.xaml.cs:399`  Priority save failures leave the capacity grid lying. **Why it bites:** the ComboBox two-way binding changes `PmProjectRow.MeetingPriority` before the async save runs; `UpsertPriorityFromUiAsync` catches all exceptions internally, sets `MeetingError`, and does not return a failure signal. The capacity window does not bind `MeetingError`, so a failed DB write leaves the row showing P1/P2/etc. even though the store rejected the save and the meeting list did not update. **Repro:** make `UpsertProjectPriorityAsync` fail, change a priority in PM Capacity, and observe the combo remains selected with no capacity-window error. **Fix:** have `UpsertPriorityFromUiAsync` return success/failure or throw after logging, then revert `row.MeetingPriority` and surface an error in the capacity window.

- [T2.003] `Kor.Operations.App/App.xaml.cs:123`  DI singletons are not disposed on app shutdown. **Why it bites:** `AppCompositionRoot.BuildServiceProvider()` returns a disposable root provider, and `WorkloadMeetingPanelViewModel.Dispose()` cancels `_disposeCts`; however `OperationsApp.OnExit` only stops the pipe server and guard, then calls `base.OnExit(e)`. The Round 38 comments say the DI container disposes the meeting VM on shutdown, but this bootstrapper never disposes the provider, so singleton disposables and Serilog provider disposal are skipped. **Repro:** inspect `OnExit`; there is no `(_services as IDisposable)?.Dispose()` or async equivalent. **Fix:** dispose the root provider in `OnExit` after app services are no longer needed, preferably with exception logging and before `base.OnExit(e)`.

- [T2.004] `Kor.Operations.App/Services/AppAiContextBuilder.cs:19`  AI context registration is idempotent but not open-window counted. **Why it bites:** `Register` removes by `ProviderName` then adds the singleton `_vm`, while `Unregister` removes by object reference. Because the chooser can open multiple `PmCapacityWindow` instances, closing any one capacity window unregisters the singleton PM provider even if another capacity window is still open. **Repro:** open two PM Capacity windows from the chooser, close one, then ask the AI panel in the remaining window; the PM context provider has been removed. **Fix:** prevent duplicate capacity windows, or make `AppAiContextBuilder` registration scoped/ref-counted per provider name.

### T3 (Low)

- [T3.001] `Kor.Operations.App/PMTools/PmCapacityWindow.xaml:64`  Meeting-only XAML resources survived the PowerShell split. **Why it bites:** `Button.Accent`, `Button.Danger`, and `SortHeaderButton` remain in `PmCapacityWindow.xaml`, but `rg` shows they are only defined there and no longer referenced after the meeting board was removed. This is not a runtime break, but it is a transformation artifact that makes future XAML cleanup/error searches noisier. **Repro:** search `Button.Accent|Button.Danger|SortHeaderButton` in `PmCapacityWindow.xaml`; only the resource definitions remain. **Fix:** remove the unused resource keys from the capacity window or move shared ones to a common dictionary if needed elsewhere.

## Round 37 regression  closed fixes still in place

- T1.001 (Round 37): verified at `Kor.Opportunities.Data/Crm/SqlCrmEngagementStore.cs:20`; `AllColumns` includes `BuyerCanonicalOrgId`, `Region`, `ProposalsSubmittedCad`, `ProposalsAcceptedCad`, and `PotentialProjects`, and insert/update/map paths include the same fields.
- T1.002: verified at `tools/BdCanonicalDedup/Program.cs:71`; `FkTargets` includes `new("CrmEngagements", "BuyerCanonicalOrgId")`.
- T1.003: verified at `tools/BdResearchImport/Program.cs:4017`; existing BD-tracking engagements call `DeleteBdTrackingChildrenAsync` before re-inserting imported contacts/activities.
- T1.004: verified at `tools/BdResearchImport/Program.cs:3890`; the importer no longer claims `NormalizeForFuzzyMatch` is wired into resolver creation and documents that typo reconciliation is handled by the data-honing pass.
- T1.005: verified at `Kor.Operations.App/Crm/BdTrackingViewModel.cs:373`; linked-project SQL filters `m.RetiredAtUtc IS NULL`.
- T2.001: verified at `Kor.Operations.App/Opportunities/CompetitionInfoView.xaml.cs:41`; inline `View_Loaded` is wrapped in try/catch.
- T3.001: verified at `Kor.Operations.App/Crm/BdTrackingViewModel.cs:214`; the grid query no longer has scalar subqueries in the SELECT list and uses `OUTER APPLY` against the indexed activity/contact tables.
- T3.002: verified at `Kor.Operations.App/Crm/BdTrackingViewModel.cs:77`; `FilteredEngagements` is now a stable `ObservableCollection`, refreshed by the filter setters.
- T4.001: verified at `tools/BdResearchImport/Program.cs:4229`; cross-link insert catches SQL duplicate-key races `2627`/`2601` and continues.
- T4.002: verified at `Kor.Operations.App/Crm/BdTrackingViewModel.cs:280`; main BD-tracking load failure logs through `_logger?.LogError`.
- T4.003: verified at `Kor.Operations.App/Opportunities/OpportunitiesViewModel.cs:591`; the outer selected-detail catch logs via Serilog before clearing panels.
- T6.001: verified at `Kor.Operations.App/Crm/BdTrackingViewModel.cs:127`; filter changes update status with the filtered count.
- T7.001: verified at `tools/BdTrackingImport/extract.py:45`; paths are computed from `__file__`, with no hardcoded `C:/VIsual Studio Projects/...` constants.

## Summary

- New findings: T1=1, T2=4, T3=1
- Round 37 fixes confirmed in place: 13 / 13

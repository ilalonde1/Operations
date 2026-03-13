# Remediation Plan
**Generated:** 2026-03-12

---

## Phase 1 — Do Now (Critical / Quick Wins)

### REM-001 SecurityGroupAccess fail-open on missing config
**Finding Ref:** SEC-002
**File(s):** `Kor.Operations.App/Services/SecurityGroupAccess.cs` lines 13–14, 20–21, 29–30
**What to change:** `IsUserInGroup` returns `true` when `groupName` is blank (line 14), when the config key is absent (line 21), or when the parsed member set is empty after normalization (line 30). The `members.Count == 0` branch should return `false` rather than `true` so that a misconfigured or absent allowlist denies access instead of granting it. The wildcard `"*"` path (line 32–33) is intentional and can remain; only the "no config" and "empty config" paths need hardening. Consider logging a warning when the config key is missing so administrators are alerted.
**Acceptance criteria:** A call where the config key is absent or empty returns `false` for any non-null user identity. Existing tests covering the wildcard and fully-populated lists continue to pass.
**Estimated effort:** XS

### REM-002 Silent swallowing of transmittal persistence failures
**Finding Ref:** ERR-001 (transmittal persistence)
**File(s):** `Kor.Operations.App/Services/TransmittalService.cs` lines 68–84 (`LogTransmittalAsync`), 105–116 (`InsertRedirectTargetsAsync` per-recipient), 156–165 (`AddRecipientsAsync`), 169–182 (`MarkSentAsync`)
**What to change:** All four persistence catch blocks are bare `catch { }` — they discard the exception without logging, so a broken connection string or SQL schema mismatch produces no observable failure. Replace each bare catch with at minimum a `Debug.WriteLine` or structured `ILogger` call at Warning level, and surface a non-fatal indicator back to the caller (e.g., return a `TransmittalSendResult` with a `PersistenceWarning` flag) so the UI can notify the user that the transmittal was sent but may not be recorded. Do not rethrow — the send itself should still succeed.
**Acceptance criteria:** A SQL failure during logging is emitted to the application log (Debug trace minimum); the UI shows a dismissible warning rather than silently succeeding; unit tests verify that logging failures do not throw.
**Estimated effort:** S

### REM-003 UI thread blocked on Graph auth at startup
**Finding Ref:** BEST-001
**File(s):** `Kor.Operations.App/App.xaml.cs` lines 573–576 (`MsalGraphAuthenticationProvider.CreateAsync(...).GetAwaiter().GetResult()`) and lines 579 (`provider.EnsureSignedInAsync(...).GetAwaiter().GetResult()`)
**What to change:** Both `.GetAwaiter().GetResult()` calls in `EnsureGraphInitializedForDelegatedAuth()` block the UI thread during MSAL token-cache wiring and interactive sign-in, which can deadlock under a synchronization context. Extract the method to return a `Task` and call it with `await` by moving startup into an async `OnStartupAsync` helper or by using `Application.Current.Dispatcher.InvokeAsync`. The pre-warm of auth (`EnsureSignedInAsync`) should be awaited on a background thread with the result marshalled back before the first window is shown.
**Acceptance criteria:** App startup no longer calls `.GetResult()` on any Graph/MSAL task; the UI thread is not blocked during authentication; integration testing shows no deadlock on machines with cold token caches.
**Estimated effort:** S

---

## Phase 2 — This Sprint (High Priority)

### REM-004 StandardDetailsWindow god-class (1704 lines, owns SQL/auth/workflow)
**Finding Ref:** ARCH-002
**File(s):** `Kor.Operations.App/StandardDetails/StandardDetailsWindow.xaml.cs` (1704 lines total)
**What to change:** Extract all SQL access into a dedicated `StandardDetailsRepository` or reuse `SqlTransmittalsStore`/`PreferencesRepository` patterns already in the Data layer. Move auth/role checks into `SecurityGroupAccess` calls surfaced through a `StandardDetailsService`. The window code-behind should contain only event handlers and data binding; all business logic and database calls should live in injected services. Target under 300 lines for the code-behind.
**Acceptance criteria:** Code-behind is under 400 lines; no `SqlConnection`/`SqlCommand` appears in the window file; SQL logic is covered by at least one integration test; existing functionality is unchanged.
**Estimated effort:** L

### REM-005 DashboardWindow owns raw SQL queries
**Finding Ref:** ARCH-002
**File(s):** `Kor.Operations.App/DashboardWindow.xaml.cs` lines 302–333 (`FetchHintsAsync`), 352–401 (`LoadTransmittalsAsync`), 410–436 (`LoadActivityAsync`)
**What to change:** The three inline query methods duplicate logic already present in `SqlTransmittalsStore.SearchSummaryAsync` and `SqlTransmittalsStore.LoadActivityAsync`. Remove the duplicate queries from the window and inject `ITransmittalsStore` via the DI container (already registered in `App.xaml.cs`). The `FetchHintsAsync` autocomplete query can be added to `SqlTransmittalsStore` or to `PreferencesRepository.SearchProjectsAsync` (which already exists).
**Acceptance criteria:** `DashboardWindow` contains no `SqlConnection` or `SqlCommand` usages; the window delegates to injected store interfaces; no regressions in dashboard search and activity display.
**Estimated effort:** M

### REM-006 remarksHtml / signatureHtml injected into email body without sanitization
**Finding Ref:** SEC-003
**File(s):** `Kor.Operations.App/Services/TransmittalService.cs` lines 199–202 (`remarksHtml.Trim()` appended to `StringBuilder`), 213–214 (`signatureHtml.Trim()` appended); `Kor.Operations.Graph/GraphFacade.cs` lines 261–265 (`remarksHtml` interpolated into `bodyHtml` without escaping)
**What to change:** Both `remarksHtml` and `signatureHtml` values originate from a WPF RichTextBox/WebView2 editor and must be treated as untrusted HTML before being embedded in a Graph API email body. Add a whitelist-based HTML sanitizer (e.g., `HtmlSanitizer` NuGet package) that strips script tags, on* event attributes, and javascript: href values. Apply sanitization in `BuildEmailBodyHtml` in `TransmittalService` before appending either string, and similarly in `GraphFacade.SendMailAsync` before building `bodyHtml`.
**Acceptance criteria:** `<script>` tags and `onerror=` attributes in `remarksHtml` are stripped before sending; unit tests verify that benign formatting (bold, links) is preserved while dangerous payloads are removed.
**Estimated effort:** S

### REM-007 GraphFacade.Instance singleton — replace with DI-injected interface
**Finding Ref:** SOLID-001
**File(s):** `Kor.Operations.Graph/GraphFacade.cs` lines 46–61 (static `_instance` / `Initialize` / `Instance` property); `Kor.Operations.App/Services/TransmittalService.cs` line 49 and line 144 (direct `GraphFacade.Instance` calls); `Kor.Operations.App/Services/UploadOrchestrator.cs` lines 48, 72, 81
**What to change:** Extract an `IGraphFacade` interface covering `UploadWithProgressAsync`, `CreateLinksAsync`, `SendMailAsync`, `ReserveTransmittalNumberAsync`, and `TryGetUserPhotoAsync`. Have `GraphFacade` implement it. Register it as a singleton in `App.xaml.cs` (`BuildServiceProvider`, line 462 already does `services.AddSingleton(_ => GraphFacade.Instance)` — switch to the interface registration). Inject `IGraphFacade` into `TransmittalService` and `UploadOrchestrator` constructors; remove the static `Instance` calls.
**Acceptance criteria:** No production code calls `GraphFacade.Instance` directly; `TransmittalService` and `UploadOrchestrator` receive `IGraphFacade` by constructor; existing tests can substitute a mock without the static singleton.
**Estimated effort:** M

### REM-008 Inline `new GlProfitLossService()` / `new FinancialsService()` in financial windows
**Finding Ref:** SOLID-001
**File(s):** `Kor.Operations.App/Financials/GlProfitLossWindow.xaml.cs` lines 79 and 165 (`new GlProfitLossService()`); `Kor.Operations.App/Financials/GlProfitLossView.xaml.cs` lines 78 and ~165 (same pattern); `Kor.Operations.App/Financials/ExecutiveSummaryService.cs` lines 19–21 (`new FinancialsService()`, `new SqlFinancialPortfolioSnapshotStore()`, `new ExecutiveSummaryDeltekLoader()`)
**What to change:** Register `GlProfitLossService` and `FinancialsService` as transients in `BuildServiceProvider`. Inject them through the window constructors or, since the financial windows are currently not DI-resolved, resolve them via the `IServiceProvider` already held by `DashboardWindow`. Remove the inline `new` constructions; this makes the services testable and swappable.
**Acceptance criteria:** No `new GlProfitLossService()` or `new FinancialsService()` in window code-behinds; services are constructor-injected or resolved through the DI container.
**Estimated effort:** S

### REM-009 GlProfitLossWindow and GlProfitLossView are near-duplicates
**Finding Ref:** MAINT-001
**File(s):** `Kor.Operations.App/Financials/GlProfitLossWindow.xaml.cs` (954 lines); `Kor.Operations.App/Financials/GlProfitLossView.xaml.cs` (1071 lines)
**What to change:** The `Window` and `UserControl` variants share identical private methods (`LoadTablesAsync`, `RefreshAsync`, `BindGrid`, `RenderCharts`, `ExportBtn_Click`, all chart rendering helpers, the `ExportSnapshot`/`ExportRow` inner classes, and `GlProfitLossViewModel`). Extract all shared logic into a `GlProfitLossController` (plain class) and the shared ViewModel into a single `GlProfitLossViewModel` file. Have both XAML hosts delegate to the controller. The duplicated `PickBestDefaultTable` scoring logic, all chart rendering, and Excel export should exist in exactly one place.
**Acceptance criteria:** No method body longer than 5 lines is duplicated between the two files; a single `GlProfitLossViewModel` class exists; both window and view continue to work identically.
**Estimated effort:** M

### REM-010 App.xaml.cs handles startup, auth, DI, IPC, and routing
**Finding Ref:** ARCH-001
**File(s):** `Kor.Operations.App/App.xaml.cs` (587 lines): `OnStartup` (lines 38–228), `BuildServiceProvider` (lines 452–518), `EnsureGraphInitializedForDelegatedAuth` (lines 544–585), `RunPipeServerAsync` (lines 247–338), `RunEmailPickerMode` (lines 344–396)
**What to change:** Split `App.xaml.cs` into focused classes: a `StartupRouter` that handles command-line arg parsing and window selection; a `PipeServer` (already partially self-contained in `RunPipeServerAsync`) extracted to its own class; a `DependencyConfig` static class for `BuildServiceProvider`; and a `GraphInitializer` for the auth bootstrap. `OnStartup` should orchestrate these collaborators with no inline business logic.
**Acceptance criteria:** `App.xaml.cs` is under 150 lines; each extracted class has a single stated responsibility; existing startup modes (picker, email-filing, quick-transfer, single-instance) all still work.
**Estimated effort:** L

### REM-011 Silent catch blocks across service layer
**Finding Ref:** ERR-001 (remaining)
**File(s):** `Kor.Operations.App/App.xaml.cs` lines 213–215 (`LoadInitialFiles` catch), 232–233 (`_pipeCts.Cancel()` catch), 335–337 (pipe server loop catch); `Kor.Operations.App/Services/MainWindowWorkflowService.cs` lines 58–62 (`LoadUserPreferencesAsync` catch), 84–87 (`InitializeCurrentUser` catch), 276–278 (`LoadEmailTeamsFromDatabase` catch); `Kor.Operations.App/Services/HeaderLoader.cs` lines 92–95 (name lookup catch), 123–126 (avatar lookup catch)
**What to change:** Replace bare `catch { }` blocks with `catch (Exception ex)` and a structured log call (or at minimum `Debug.WriteLine`). For the pipe server loop (App.xaml.cs line 335), log the exception type and message before continuing. For `LoadUserPreferencesAsync` and `LoadEmailTeamsFromDatabase`, return a meaningful empty result and log the failure. HeaderLoader already uses `Debug.WriteLine` in most catch blocks — ensure this pattern is applied consistently to all remaining silent catches.
**Acceptance criteria:** No bare `catch { }` or `catch { /* blank */ }` blocks exist in the listed files; all swallowed exceptions are at minimum traced to Debug output; the application's resilience behavior (continue on non-fatal errors) is preserved.
**Estimated effort:** S

### REM-012 Reflection used for SelectedProjectNo, ToRecipients, AvatarImageSource
**Finding Ref:** MAINT-003
**File(s):** `Kor.Operations.App/App.xaml.cs` lines 364–369 (`prop = t.GetProperty("SelectedProjectNo")`); `Kor.Operations.App/Services/MainWindowWorkflowService.cs` lines 157–158 (`GetProperty("ToRecipients")`, `GetProperty("CcRecipients")`); `Kor.Operations.App/DashboardWindow.xaml.cs` lines 109–110 (`GetProperty("AvatarImageSource")`)
**What to change:** For `SelectedProjectNo`: add a strongly-typed `string? SelectedProjectNo` public property to `EmailFilePickerWindow` and call it directly. For `ToRecipients`/`CcRecipients`: these should already be strongly-typed properties on the `Transmittal` model (or a `PreparedHeader`-style DTO already exists) — remove the reflection calls and assign directly. For `AvatarImageSource`: the `HeaderLoader.ApplyAsync` method already sets `header.AvatarImageSource = bmp` directly (line 68 of HeaderLoader.cs) — remove the fallback reflection in `DashboardWindow.InitHeaderIdentityAsync` and use `HeaderLoader.ApplyAsync` there instead.
**Acceptance criteria:** No `GetProperty(...)` / `SetValue(...)` reflection calls appear in the listed files; all three properties are accessed through their public typed API; the functionality is unchanged.
**Estimated effort:** S

---

## Phase 3 — Next Sprint (Modernization)

### REM-013 ConfigurationManager used directly instead of typed options
**Finding Ref:** BEST-002
**File(s):** `Kor.Operations.App/App.xaml.cs` lines 455–459, 522, 530, 538–541, 547–549 (`ConfigurationManager.AppSettings[...]` and `ConfigurationManager.ConnectionStrings[...]`); `Kor.Operations.App/Services/MainWindowWorkflowService.cs` lines 69, 212–213 (`ConfigurationManager.ConnectionStrings[...]`); `Kor.Operations.App/Services/DeltekHeadshotProvider.cs` lines 12–14; `Kor.Operations.App/Financials/FinancialsService.cs` lines 54–56; `Kor.Operations.App/Financials/GlProfitLossService.cs` lines 639–641
**What to change:** Introduce a typed `AppSettings` options class (e.g., `OperationsAppOptions`) with strongly-typed properties for all AppSettings keys. Populate it once at startup from `ConfigurationManager` in `BuildServiceProvider` and register it as a singleton. Inject `OperationsAppOptions` where raw `ConfigurationManager.AppSettings` is currently read. This removes the dependency on static global state from services and makes the settings mockable in tests. `AppConfigKeys` string constants can remain as the key source-of-truth during transition.
**Acceptance criteria:** No `ConfigurationManager.AppSettings[...]` calls exist outside `App.xaml.cs` and `EnvironmentSecretOverrides`; services receive configuration through constructor-injected typed options; at least one test verifies behavior with a non-default options value.
**Estimated effort:** M

### REM-014 Task.Run wrappers over synchronous ODBC / file I/O
**Finding Ref:** PERF-002
**File(s):** `Kor.Operations.App/Financials/FinancialsService.cs` lines 35–42 (`Task.Run(() => LoadSnapshot(...))`); `Kor.Operations.App/Financials/GlProfitLossService.cs` lines 21–46 (`Task.Run(...)` in `GetTablesAsync`), lines 82–163 (`Task.Run(...)` in `BuildProfitLossAsync`), lines 654–771 (`Task.Run(...)` in `LoadLineItemTransactionsAsync`); `Kor.Operations.App/Services/DeltekHeadshotProvider.cs` lines 23–38 (`Task.Run(...)`), lines 46–75 (`Task.Run(...)`); `Kor.Operations.Rendering/CoverSheetRenderer.cs` line 86 (`Task.Run(...)`)
**What to change:** `Task.Run` over synchronous blocking I/O (ODBC, file reads, QuestPDF generation) is not truly async and ties up thread-pool threads. The correct approach for inherently synchronous work is either: (a) accept that these are blocking and call them synchronously from an already off-UI-thread context, or (b) for database work, migrate to an async ODBC wrapper if one becomes available. At minimum, add a comment documenting why the `Task.Run` is intentional, and ensure the callers do not `.GetResult()` from the UI thread. For `CoverSheetRenderer.RenderAsync`, the `Task.Run` is acceptable since QuestPDF has no async API — document this explicitly.
**Acceptance criteria:** All `Task.Run` usages are either removed (if the caller is already off the UI thread) or documented with a justification comment; no new `Task.Run`-over-sync patterns are introduced.
**Estimated effort:** M

### REM-015 Dashboard queries duplicated between SqlTransmittalsStore and DashboardWindow
**Finding Ref:** MAINT-002
**File(s):** `Kor.Operations.App/DashboardWindow.xaml.cs` lines 302–333 (`FetchHintsAsync` — not in store), 352–401 (`LoadTransmittalsAsync` — duplicates `SqlTransmittalsStore.SearchSummaryAsync`), 410–436 (`LoadActivityAsync` — duplicates `SqlTransmittalsStore.LoadActivityAsync`); `Kor.Operations.Data/SqlTransmittalsStore.cs` lines 159–235 (`SearchSummaryAsync`), 238–295 (`LoadActivityAsync`)
**What to change:** This overlaps with REM-005. The `LoadTransmittalsAsync` query in `DashboardWindow` includes SharePoint URL search and Type column filtering that `SqlTransmittalsStore.SearchSummaryAsync` does not (it omits `SharePointUrl LIKE @like` and `Type`). Rather than simply deleting the window query, first update `SearchSummaryAsync` to accept the additional filter parameters, then delete the duplicate from the window. Move `FetchHintsAsync` to `PreferencesRepository.SearchProjectsAsync` or a new `SearchHintsAsync` method in `SqlTransmittalsStore`.
**Acceptance criteria:** `DashboardWindow` contains no raw SQL; `SqlTransmittalsStore` exposes all required query variants; the dashboard's SharePoint URL and Type filter behavior is preserved.
**Estimated effort:** S

### REM-016 AddWithValue and raw SQL strings in service/data layer
**Finding Ref:** MAINT-004
**File(s):** `Kor.Operations.App/Services/MainWindowWorkflowService.cs` lines 226 and 260 (`cmd.Parameters.AddWithValue`); `Kor.Operations.Data/PreferencesRepository.cs` lines 65, 89–91, 100, 103, 134–136, 149–150, 165, 192–194, 222–223, 228, 254–256, 279–281 (pervasive `AddWithValue`); `Kor.EmailSearch.Core/EmailIndexWriter.cs` lines 122, 158, 271–321 (`AddWithValue` throughout `PopulateCommonParameters`)
**What to change:** `AddWithValue` can cause implicit type inference problems (e.g., `nvarchar(MAX)` instead of `nvarchar(n)`, date arithmetic surprises). Replace each `AddWithValue` call with an explicitly typed `SqlParameter` — specifying `SqlDbType`, size, and precision. `EmailIndexWriter.PopulateCommonParameters` is particularly important as it writes user-derived strings to string columns. `SqlTransmittalsStore` already uses the `AddParameter` helper with typed inference via `cmd.CreateParameter()` — apply the same pattern to the remaining files. Raw SQL strings as `const string sql` blocks are acceptable; the issue is parameter typing, not string SQL itself.
**Acceptance criteria:** No `AddWithValue` calls remain in the listed files; all parameters have an explicit `SqlDbType` and size; SQL Server Profiler shows correct implicit conversions for string parameters.
**Estimated effort:** M

### REM-017 Role names are raw strings spread through UI code
**Finding Ref:** SEC-004
**File(s):** Role name strings used as arguments to `SecurityGroupAccess.IsUserInGroup` throughout `StandardDetailsWindow.xaml.cs` (multiple call sites) and any other callers
**What to change:** Introduce a `static class KnownRoles` (or extend `AppConfigKeys`) with `const string` fields for each role name (e.g., `StandardDetailsAdmin`, `StandardDetailsReviewer`). Replace every string literal passed to `IsUserInGroup` with the corresponding constant. This ensures a typo in a role name is a compile error rather than a silent access-grant or access-deny at runtime.
**Acceptance criteria:** No string literal is passed directly to `IsUserInGroup`; all role names are `const` fields in a single `KnownRoles` class; a grep for `IsUserInGroup("` returns zero results.
**Estimated effort:** XS

---

## Phase 4 — Nice to Have

### REM-018 Test project uses HintPath binary references instead of ProjectReference
**Finding Ref:** TEST-001
**File(s):** `Kor.Operations.App/Kor.Transmittals.App.Tests/Kor.Operations.App.Tests.csproj` lines 28–39 (`<Reference Include="Kor.EmailSearch.Core">` with `HintPath`, and three similar entries for `Kor.Operations.Core`, `Kor.Operations.Data`, `Kor.Operations.Graph`)
**What to change:** Replace the four `<Reference>` / `<HintPath>` entries with `<ProjectReference>` elements pointing to the respective `.csproj` files (the same references already used by the main app project, as seen in `Kor.Operations.App.csproj` lines 143–148). The `<Target Name="BuildReferencedLibraries">` custom target on line 54 which pre-builds the DLLs is then unnecessary and should be removed. This eliminates MSB3101 warnings and ensures test builds always reflect the latest source.
**Acceptance criteria:** The test project builds cleanly with `dotnet build` without MSB3101 warnings; all 13 existing passing tests still pass; no `HintPath` entries remain for in-solution projects.
**Estimated effort:** XS

### REM-019 Test coverage limited to SqlTransmittalsStore slice
**Finding Ref:** TEST-002
**File(s):** `Kor.Operations.App/Kor.Transmittals.App.Tests/SqlTransmittalsStoreTests.cs` (only 2 test methods in the file); no tests for `SecurityGroupAccess`, `TransmittalService`, `MainWindowWorkflowService`, `GlProfitLossService`, financial calculations
**What to change:** Add unit tests for: (1) `SecurityGroupAccess.IsUserInGroup` covering the fail-open scenarios being fixed in REM-001; (2) `MainWindowWorkflowService.ParseEmails`, `ValidateRequiredFields`, and `BuildTransmittalFolderPath` (all pure functions, no I/O); (3) `TransmittalService.SendAsync` using a mock `ITransmittalsStore` and `IUploadOrchestrator` to verify the persistence-failure warning path from REM-002; (4) `DeliveryConfidenceCalculator` which is already compiled into the test project. WPF window and Graph/ODBC integration tests are intentionally deferred.
**Acceptance criteria:** Test count grows from ~13 to at least 30; `SecurityGroupAccess` and `MainWindowWorkflowService` pure methods are fully covered; CI pipeline runs tests without requiring SQL Server or SharePoint.
**Estimated effort:** M

### REM-020 Two SQLite disk I/O test failures and MSB3101 warnings
**Finding Ref:** TEST-003
**File(s):** `Kor.Operations.App/Kor.Transmittals.App.Tests/SqlTransmittalsStoreTests.cs` lines 59–87 (`LogTransmittalAsync_InsertsRowRetrievableById`), 89–119 (`MarkSentAsync_UpdatesSentAtTimestamp`); `Kor.Operations.App/Kor.Transmittals.App.Tests/Kor.Operations.App.Tests.csproj` lines 28–39 (HintPath references causing MSB3101)
**What to change:** The SQLite disk I/O errors stem from shared in-memory database contention when multiple test processes open separate connections to the same named in-memory URI. Fix by either: (a) using a single `SqliteConnection` object shared across `InitializeAsync`, `CreateStore`, and all test `OpenConnectionAsync` calls with `cache=shared` mode consistently, or (b) switching to a file-backed SQLite database in `Path.GetTempPath()` created fresh per test run. MSB3101 warnings are resolved by REM-018. After fixing REM-018, rebuild and rerun to confirm both tests pass.
**Acceptance criteria:** All tests in `SqlTransmittalsStoreTests` pass reliably in both `dotnet test` and VS Test Explorer; no SQLite disk I/O exceptions appear in test output; no MSB3101 warnings on build.
**Estimated effort:** S

### REM-021 Stale package versions and sibling repo path reference
**Finding Ref:** DEPS-001
**File(s):** `Kor.Operations.App/Kor.Operations.App.csproj` line 128 (`Microsoft.Web.WebView2 Version="1.0.3800.47"`), line 129 (`MsgReader Version="6.0.9"`); `Kor.Operations.App/Kor.Transmittals.App.Tests/Kor.Operations.App.Tests.csproj` line 16 (`Microsoft.NET.Test.Sdk Version="17.11.1"`); `Kor.Operations.App/Kor.Operations.App.csproj` line 143 (`ProjectReference` to `..\..\EmailIndexer\Kor.EmailCommon\Kor.EmailCommon.csproj` — sibling repo path)
**What to change:** Update `Microsoft.Web.WebView2` to the current stable release (1.0.x or higher) to pick up security and Chromium patches; update `Microsoft.NET.Test.Sdk` to the latest stable version. For the sibling repo `Kor.EmailCommon` reference: document the required relative checkout layout in the repo README, or consider vendoring the relevant interfaces/DTOs from `EmailCommon` into `Kor.Operations.Core` to remove the cross-repo path dependency and make standalone builds possible.
**Acceptance criteria:** All NuGet packages are within one major version of current stable; the build succeeds without requiring a specific sibling repo checkout location; a build on a fresh clone with only this repo produces a working application.
**Estimated effort:** S

### REM-022 EnsureFolderPathAsync enumerates all children per path segment (N+1)
**Finding Ref:** PERF-001
**File(s):** `Kor.Operations.Graph/GraphFacade.cs` lines 385–407 (`EnsureFolderPathAsync` — foreach over `segments`, calls `Children.GetAsync` on every iteration)
**What to change:** The current implementation calls `GET /drives/{driveId}/items/{parentId}/children` for each path segment in sequence, loading the full child list just to find one folder by name. For a path with N segments this is N round-trips, each potentially returning a large children page. Replace with the Graph API's path-based item resolution: `GET /drives/{driveId}/root:/{relativePath}` which resolves the full path in a single call. If the folder does not exist, create it with `PUT /drives/{driveId}/root:/{relativePath}:/children` using `conflictBehavior: fail` followed by a fallback to segment-by-segment creation only when the path is partly missing.
**Acceptance criteria:** A 4-segment SharePoint folder path requires at most 2 Graph API calls (one attempt, one creation if missing) instead of 4+; existing upload and link-creation tests pass; no regression in folder creation for new transmittals.
**Estimated effort:** M

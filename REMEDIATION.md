# Kor Operations — Remediation Task List

_Generated: 2026-03-11. Source of truth: ANALYSIS.md at solution root._
_Audience: Codex (implementation) + Claude (verification)._

---

## How to use this file

- Tasks are ordered by priority. Each task is independently completable unless a dependency is noted.
- Codex implements one task at a time.
- After each task, Claude verifies against the **Verification steps** section before the next task begins.
- Acceptance criteria are binary — each bullet is either satisfied or not.

---

## HIGH PRIORITY

---

### TASK-1: MainWindow God Object decomposition

**Priority:** High
**File(s):**
- `Kor.Operations.App/MainWindow.xaml.cs` (primary — shrink this)
- `Kor.Operations.App/Services/TransmittalService.cs` (new)
- `Kor.Operations.App/Services/UploadOrchestrator.cs` (new)
- `Kor.Operations.App/Services/RecipientResolver.cs` (new)
- `Kor.Operations.App/Services/ProjectSearchService.cs` (new)

**Problem:** `MainWindow.xaml.cs` is approximately 2500 lines and directly contains business logic, data access calls, upload orchestration, and cover sheet generation alongside UI event handlers. This makes the file untestable and unmaintainable.

**Acceptance criteria:**
- `MainWindow.xaml.cs` is ≤ 400 lines after the refactor.
- All business logic (transmittal assembly, file upload coordination, link creation, email dispatch, SQL logging) is moved into one or more new service classes under `Kor.Operations.App/Services/`.
- Each new service class has a corresponding interface (e.g., `ITransmittalService`, `IUploadOrchestrator`).
- MainWindow code-behind contains only: field declarations, constructor, event handler stubs that delegate to service calls, and UI-specific helpers (converters, visibility toggling). No direct calls to `GraphFacade`, `SqlTransmittalsStore`, or `CoverSheetRenderer` remain in `MainWindow.xaml.cs`.
- The application builds with zero new errors (`dotnet build` exits 0).
- All existing runtime behaviors (upload, send, log) are preserved — no logic is deleted, only moved.

**Implementation notes:**
- Extract in this order to minimise merge conflicts: (1) `ProjectSearchService` — wraps `ProjectIndex` file-system search and `PreferencesRepository.SearchProjectsAsync`; (2) `RecipientResolver` — wraps `VantagepointRepository.SearchPeopleAsync` and `PreferencesRepository.SearchPeopleAsync`; (3) `UploadOrchestrator` — encapsulates the sequence: render cover sheet → upload files via `GraphFacade.UploadWithProgressAsync` → create links via `GraphFacade.CreateLinksAsync`; (4) `TransmittalService` — orchestrates `UploadOrchestrator` + `GraphFacade.SendMailAsync` + `SqlTransmittalsStore` logging.
- Do NOT change any method signatures on `GraphFacade`, `CoverSheetRenderer`, or any Data-layer class.
- Do NOT move XAML, resource dictionaries, or data-binding logic.
- Do NOT introduce a DI container in this task (that is TASK-10). Constructor-inject dependencies manually for now.
- Preserve the `WizardState` field on MainWindow — it is the data model for the wizard and must stay accessible to the code-behind.
- Use `IProgress<(string file, long sent, long total)>` when passing progress callbacks from UploadOrchestrator back to MainWindow.

**Verification steps:**
- Run `dotnet build Kor.Operations.App.sln` — must exit 0 with no new warnings promoted to errors.
- Confirm `MainWindow.xaml.cs` line count is ≤ 400 (`wc -l` or equivalent).
- Confirm no direct `GraphFacade.`, `SqlTransmittalsStore.`, or `CoverSheetRenderer.` calls remain in `MainWindow.xaml.cs` (grep for these identifiers).
- Confirm each new service file has a corresponding `I{Name}` interface in the same directory.
- Confirm `TransmittalService` is the single call site for `SqlTransmittalsStore.LogTransmittalAsync`, `AddRecipientsAsync`, and `MarkSentAsync`.

---

### TASK-2: BasicEmailMetadataExtractor — implement with MsgReader

**Priority:** High
**File(s):**
- `Kor.EmailSearch.Core/BasicEmailMetadataExtractor.cs` (rewrite)

**Problem:** `BasicEmailMetadataExtractor` infers only the Subject from the filename and leaves all other `EmailMetadata` fields null. This makes email full-text search non-functional because body, sender, recipients, and date are never indexed.

**Acceptance criteria:**
- `BasicEmailMetadataExtractor.ExtractAsync` returns a fully populated `EmailMetadata` for a valid `.msg` file, with non-null values for: `Subject`, `FromDisplay`, `FromEmail`, `ToList`, `SentOnUtc`, `HasAttachments`, `AttachmentCount`, and `BodyText` (may be truncated to 4000 chars).
- `Format` is set to `"MSG"` for `.msg` files and `"EML"` for `.eml` files.
- `MessageId` is populated from the MSG header if available; null otherwise (not required to be non-null).
- If MsgReader throws or the file is corrupt, the method catches the exception, sets `BodyText = null`, sets all address fields to null, and returns a partial `EmailMetadata` with `FileName` and `Format` still populated — it must not throw.
- The class name remains `BasicEmailMetadataExtractor` and the file path does not change.
- The project builds with zero new errors.

**Implementation notes:**
- Use `MsgKit.Mime.Message` (from the `MsgReader` package, namespace `MsgReader.Outlook`) to open `.msg` files. The relevant type is `MsgReader.Outlook.Storage.Message`.
- To open: `using var msg = new MsgReader.Outlook.Storage.Message(filePath);`
- Access fields: `msg.Subject`, `msg.Sender?.DisplayName`, `msg.Sender?.Email`, `msg.SentOn`, `msg.BodyText`, `msg.Attachments.Count`.
- For `ToList`: join `msg.Recipients` where `msg.Recipients[i].Type == MsgReader.Outlook.Storage.Recipient.RecipientType.To` into a semicolon-separated string.
- For `.eml` files: use `MsgReader.Mime.Message.Load(filePath)` — extract `Headers.Subject`, `Headers.From`, `Headers.To`, `Headers.Date`.
- Truncate `BodyText` at 4000 characters to avoid bloating the SQL index row.
- Do NOT change the `IEmailMetadataExtractor` interface signature.
- Do NOT add new NuGet packages — `MsgReader` 6.0.9 is already referenced in `Kor.Operations.App`. Confirm `Kor.EmailSearch.Core` references it, or add the existing package reference in `Kor.EmailSearch.Core.csproj`.

**Verification steps:**
- Run `dotnet build` — exits 0.
- Unit test (add inline or in test project): construct `BasicEmailMetadataExtractor`, call `ExtractAsync` with a real `.msg` file from `Assets/` or a test fixture. Assert `Subject != null`, `FromEmail != null`, `SentOnUtc != null`.
- Confirm that passing a path to a zero-byte or non-existent file does not throw — returns partial `EmailMetadata` with `FileName` set.
- Grep `Kor.EmailSearch.Core.csproj` to confirm `MsgReader` package reference is present.

---

## MEDIUM PRIORITY

---

### TASK-3: Eliminate reflection in GraphFacade and CoverSheetRenderer

**Priority:** Medium
**File(s):**
- `Kor.Operations.Graph/GraphFacade.cs`
- `Kor.Operations.Rendering/CoverSheetRenderer.cs`

**Problem:** Both files use `GetProp<T>(object obj, string propertyName)` reflection to read `Transmittal` properties by name. This compiles without error even if the property is renamed or deleted, causing silent runtime failures.

**Acceptance criteria:**
- Zero calls to `GetProp`, `GetType().GetProperty`, or `PropertyInfo` remain in `GraphFacade.cs` or `CoverSheetRenderer.cs`.
- All previously reflection-accessed properties (`Subject`, `Purpose`, `Remarks`, `Recipients`, `FromName`, `FromEmail`, `IsCc`, etc.) are accessed via their concrete typed properties on `Transmittal` or `Recipient` directly.
- `GraphFacade.SendMailAsync` accepts `Transmittal` as its first parameter type (replacing `object header`), or the method is changed to accept a purpose-built DTO that `Kor.Operations.Core` defines.
- `CoverSheetRenderer.RenderAsync` accepts `Transmittal` directly (it likely already does — confirm and remove any remaining reflection).
- The project builds with zero new errors.
- No behaviour change: the same fields are used to compose emails and cover sheets.

**Implementation notes:**
- The current `GraphFacade.SendMailAsync` signature is `(object header, ...)`. Change `object header` to `Transmittal header`. Update the single call site in `MainWindow.xaml.cs` (or `TransmittalService` after TASK-1).
- `Kor.Operations.Graph` already references `Kor.Operations.Core` — `Transmittal` is accessible.
- Delete the `GetProp<T>` private helper method entirely once all usages are removed.
- Do NOT change the return types or other parameters of `SendMailAsync` or `RenderAsync`.
- If TASK-1 has been completed, the call site is in `TransmittalService`; if not, it is in `MainWindow.xaml.cs`. Update whichever exists.

**Verification steps:**
- `dotnet build` exits 0.
- Grep `GraphFacade.cs` and `CoverSheetRenderer.cs` for `GetProp`, `GetType`, `GetProperty`, `PropertyInfo` — all return zero results.
- Grep both files for `object header` parameter — returns zero results.
- Manually trace `GraphFacade.SendMailAsync`: confirm `Subject`, `Purpose`, `Remarks`, `Recipients` are accessed as `header.Subject`, `header.Purpose`, etc.

---

### TASK-4: Unify nullable annotations

**Priority:** Medium
**File(s):**
- All `.cs` files in the solution (solution-wide change)
- `Kor.Operations.App/App.xaml.cs` (specifically has `#nullable disable`)

**Problem:** `App.xaml.cs` has `#nullable disable` at the top; other files have inconsistent or missing nullable annotations. This masks potential null-reference exceptions from the compiler.

**Acceptance criteria:**
- `#nullable disable` does not appear in any `.cs` file in the solution.
- All projects have `<Nullable>enable</Nullable>` in their `.csproj` files (or it is set globally in `Directory.Build.props`).
- `dotnet build` produces zero nullable-related warnings (CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625) — all warnings are resolved, not suppressed with `#pragma warning disable`.
- All fields and properties that can legitimately be null are annotated with `?`. All that cannot be null are left without `?` and initialised appropriately.

**Implementation notes:**
- Start by adding `<Nullable>enable</Nullable>` to each `.csproj` one project at a time, fixing warnings before moving to the next. Suggested order: `Kor.Operations.Core` → `Kor.EmailSearch.Core` → `Kor.Operations.Data` → `Kor.Operations.Graph` → `Kor.Operations.Rendering` → `Kor.Operations.App`.
- Common patterns to fix: (a) fields initialised in a method rather than the constructor — add `= null!` with a comment, or restructure; (b) properties on DTOs that are set by Dapper — annotate as `string?` or add `= string.Empty` default; (c) out-parameters — annotate the out type as `T?`.
- Do NOT use `!` (null-forgiving operator) to suppress warnings unless the value is guaranteed non-null by a preceding guard check and a comment explains why.
- Do NOT change any public API return types from non-nullable to nullable unless the method genuinely can return null.

**Verification steps:**
- `dotnet build Kor.Operations.App.sln` exits 0 with zero CS86xx warnings.
- Grep all `.cs` files for `#nullable disable` — returns zero results.
- Grep all `.csproj` files for `<Nullable>` — every project must have `enable`.
- Grep all `.cs` files for `#pragma warning disable CS86` — returns zero results.

---

### TASK-5: Replace hardcoded network path in MainWindow

**Priority:** Medium
**File(s):**
- `Kor.Operations.App/MainWindow.xaml.cs` (or `Services/ProjectSearchService.cs` if TASK-1 is done)
- `Kor.Operations.Core/Models.cs` — confirm `AppConfig.ProjectsRoot` field exists

**Problem:** The file-system project search uses the hardcoded literal `\\KOR-FS01\Projects\Projects` instead of the `AppConfig.ProjectsRoot` field, which is already defined in the domain model.

**Acceptance criteria:**
- The string literal `\\KOR-FS01\Projects\Projects` (or any equivalent UNC path) does not appear anywhere in `.cs` files.
- The path is read exclusively from `AppConfig.ProjectsRoot`, which is populated from `App.config` at startup.
- `App.config` contains a key (e.g., `ProjectsRoot`) whose value is `\\KOR-FS01\Projects\Projects`.
- If `AppConfig.ProjectsRoot` is null or empty at runtime, the project search is disabled (returns empty results) and logs a warning — it does not throw.
- `dotnet build` exits 0.

**Implementation notes:**
- Find where `AppConfig` is constructed/populated (likely in `App.xaml.cs` `OnStartup`). Add reading of the new `App.config` key there: `ConfigurationManager.AppSettings["ProjectsRoot"]`.
- Pass the populated `AppConfig` instance into `ProjectSearchService` (after TASK-1) or into `MainWindow` directly.
- The `ProjectIndex` constructor likely takes the root path as a string — pass `appConfig.ProjectsRoot` there.
- Do NOT hardcode a fallback path. A missing config key should disable the search, not substitute a default UNC path.

**Verification steps:**
- Grep all `.cs` files for `KOR-FS01` and `Projects\\Projects` — both return zero results.
- Confirm `App.config` contains a `ProjectsRoot` key with the correct value.
- Confirm `AppConfig.ProjectsRoot` is read from config (grep for the key name in the startup code).
- `dotnet build` exits 0.

---

### TASK-6: Parameterize dynamic SQL in VantagepointRepository

**Priority:** Medium
**File(s):**
- `Kor.Operations.Data/VantagepointRepository.cs`

**Problem:** `ORDER BY` clauses and potentially table names in `VantagepointRepository` are built by string concatenation. While direct SQL injection via user input is low-risk here, the pattern is unsafe and violates parameterization principles.

**Acceptance criteria:**
- No string concatenation is used to construct `ORDER BY` column names in any query method.
- Allowed sort columns are defined as a private `static readonly` whitelist (e.g., `HashSet<string>` or `enum`-to-column mapping).
- If a sort column is requested that is not on the whitelist, an `ArgumentException` is thrown (not silently ignored or defaulted).
- All `WHERE` clause values continue to use ODBC parameterized queries (`?` placeholders or named params) — no regression.
- `dotnet build` exits 0.

**Implementation notes:**
- Identify every location in `VantagepointRepository.cs` where a sort column or table name is injected via string interpolation or concatenation.
- For sort columns: create a `private static readonly Dictionary<SortColumn, string> _allowedSortColumns` where `SortColumn` is an enum. Map enum values to the exact SQL column names. Use the mapped value in the query.
- For table names (if any): apply the same whitelist pattern with a separate enum.
- Do NOT change the public method signatures of `VantagepointRepository`.
- Do NOT change the ODBC driver or connection factory.

**Verification steps:**
- Grep `VantagepointRepository.cs` for `$"` (interpolated strings containing SQL) and `+ "` (concatenated SQL) — all instances must be reviewed and eliminated from ORDER BY/table-name positions.
- Confirm a whitelist dictionary or enum exists in the file.
- `dotnet build` exits 0.
- Manually verify: passing an unlisted sort column name to an affected method throws `ArgumentException`.

---

## LOW PRIORITY

---

### TASK-7: Centralize App.config key strings

**Priority:** Low
**File(s):**
- `Kor.Operations.App/Configuration/AppConfigKeys.cs` (new)
- All `.cs` files that call `ConfigurationManager.AppSettings["..."]` (update call sites)

**Problem:** App.config key strings (e.g., `"Graph.TenantId"`, `"Vp.Dsn"`, `"SecurityGroup.Financials.Members"`) are repeated as bare string literals across multiple classes. A typo causes a silent null return with no compiler error.

**Acceptance criteria:**
- A new static class `AppConfigKeys` exists at `Kor.Operations.App/Configuration/AppConfigKeys.cs` with a `public const string` for every App.config key used in the solution.
- Every `ConfigurationManager.AppSettings["..."]` call site is updated to reference the corresponding constant (e.g., `ConfigurationManager.AppSettings[AppConfigKeys.GraphTenantId]`).
- No bare string literal App.config key remains at any call site (grep-verifiable).
- `dotnet build` exits 0.
- The set of keys in `AppConfigKeys` exactly matches the set of keys in `App.config` — no orphaned constants, no missing constants.

**Implementation notes:**
- Place `AppConfigKeys` in a new folder `Kor.Operations.App/Configuration/`.
- Name constants in PascalCase matching the config key semantics (e.g., `Graph.TenantId` → `GraphTenantId`).
- Also include connection string names used with `ConfigurationManager.ConnectionStrings["..."]`.
- Do NOT move or rename any keys in `App.config` itself.
- Do NOT change the values of any settings.

**Verification steps:**
- `AppConfigKeys.cs` exists and compiles.
- Grep all `.cs` files for `AppSettings\["` — returns zero results (all replaced by constant references).
- Grep all `.cs` files for `ConnectionStrings\["` — returns zero results.
- Count of `const string` fields in `AppConfigKeys` matches count of `<add key=` entries in `App.config`.
- `dotnet build` exits 0.

---

### TASK-8: Add Polly retry/backoff to Graph and Data layers

**Priority:** Low
**File(s):**
- `Kor.Operations.Graph/GraphFacade.cs`
- `Kor.Operations.Data/SqlTransmittalsStore.cs`
- `Kor.Operations.Data/PreferencesRepository.cs`
- `Kor.Operations.Data/SqlEmailIndexStore.cs`
- `Kor.Operations.Data/SqlFinancialPortfolioSnapshotStore.cs`
- `Kor.Operations.Graph/Kor.Operations.Graph.csproj` (add Polly)
- `Kor.Operations.Data/Kor.Operations.Data.csproj` (add Polly)

**Problem:** All Graph API calls and SQL store calls fail immediately on transient faults (network blip, SQL timeout, throttling). There is no retry or backoff logic.

**Acceptance criteria:**
- `Polly` NuGet package (v8.x) is added to `Kor.Operations.Graph` and `Kor.Operations.Data`.
- All public async methods in `GraphFacade` that make HTTP calls (`UploadWithProgressAsync`, `CreateLinksAsync`, `SendMailAsync`, `TryGetUserPhotoAsync`, `TryGetUserNamesAsync`) are wrapped in a Polly `AsyncRetryPolicy` with: 3 attempts, exponential backoff (2s, 4s, 8s), retrying on `ServiceException` with status 429 or 503, and `HttpRequestException`.
- All public async methods in each SQL store class that execute queries are wrapped in a Polly `AsyncRetryPolicy` with: 3 attempts, exponential backoff (1s, 2s, 4s), retrying on `SqlException` with transient error numbers (1205 deadlock, -2 timeout, 233, 10053, 10054, 10060).
- Non-transient exceptions (auth failures, 404, etc.) are NOT retried — they propagate immediately.
- `dotnet build` exits 0.

**Implementation notes:**
- Define retry policies as `private static readonly AsyncRetryPolicy` fields at the top of each class so they are created once, not per-call.
- For Graph: `Policy.Handle<ServiceException>(ex => ex.ResponseStatusCode == HttpStatusCode.TooManyRequests || ex.ResponseStatusCode == HttpStatusCode.ServiceUnavailable).Or<HttpRequestException>().WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)))`.
- For SQL: `Policy.Handle<SqlException>(IsSqlTransient).WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)))` where `IsSqlTransient` checks `ex.Number` against the transient list.
- Do NOT retry `TryGet*` methods that are already non-throwing — they should remain non-throwing even on exhausted retries (catch `Exception` in the outer try/catch after the policy).
- Do NOT add Polly to `Kor.Operations.App` — only `Graph` and `Data` projects.
- Use `Polly` v8 (`Microsoft.Extensions.Http.Polly` is NOT needed — use `Polly` directly).

**Verification steps:**
- Grep `Kor.Operations.Graph.csproj` and `Kor.Operations.Data.csproj` for `<PackageReference Include="Polly"` — both present.
- Grep `GraphFacade.cs` for `WaitAndRetryAsync` — at least one match per public HTTP method.
- Grep each SQL store file for `WaitAndRetryAsync` — at least one match per file.
- `dotnet build` exits 0.
- Confirm `TryGetUserPhotoAsync` and `TryGetUserNamesAsync` still return null (not throw) after retries are exhausted.

---

### TASK-9: Increase named pipe timeout

**Priority:** Low
**File(s):**
- `Kor.Operations.App/App.xaml.cs`

**Problem:** `NamedPipeClientStream.Connect(2000)` uses a 2-second timeout. On a slow or busy machine at startup, the running instance may not yet be listening, causing the second launch to incorrectly fail to forward its args and exit with an error instead.

**Acceptance criteria:**
- `NamedPipeClientStream.Connect(timeout)` is called with `8000` (8 seconds) instead of `2000`.
- A code comment immediately above (or inline with) the call explains: the timeout was increased from 2s to 8s to accommodate slow-start conditions where the pipe server may not yet be listening when a second instance launches quickly after the first.
- No other logic in the single-instance section is changed.
- `dotnet build` exits 0.

**Implementation notes:**
- This is a one-line change plus a comment. Do not refactor surrounding code.
- The constant value `8000` must be used directly, not as a named constant (keeping the change minimal).

**Verification steps:**
- Grep `App.xaml.cs` for `Connect(2000)` — returns zero results.
- Grep `App.xaml.cs` for `Connect(8000)` — returns exactly one result.
- Confirm a comment referencing "slow-start" or equivalent rationale is adjacent to the call.
- `dotnet build` exits 0.

---

### TASK-10: Add Microsoft.Extensions.DependencyInjection

**Priority:** Low
**File(s):**
- `Kor.Operations.App/App.xaml.cs`
- `Kor.Operations.App/Kor.Operations.App.csproj`
- All service, store, facade, and repository classes instantiated in `App.xaml.cs` or `MainWindow.xaml.cs`

**Dependency:** Best done after TASK-1 (service classes exist) and TASK-2 (extractor is real). Can be done before but requires registering existing manually-wired types.

**Problem:** All services, repositories, and facades are constructed manually with `new`. This couples construction to call sites, prevents interface substitution, and makes testing harder.

**Acceptance criteria:**
- `Microsoft.Extensions.DependencyInjection` NuGet package is added to `Kor.Operations.App.csproj`.
- A `ServiceCollection` is built in `App.xaml.cs` `OnStartup()` before any window is opened.
- All of the following are registered: `ITransmittalsStore` / `SqlTransmittalsStore`, `IUserPreferencesStore` / `SqlUserPreferencesStore`, `PreferencesRepository`, `SqlEmailIndexStore`, `SqlFinancialPortfolioSnapshotStore`, `VantagepointRepository`, `GraphFacade` (singleton), `CoverSheetRenderer` (transient), `EmailIndexWriter` (transient), `EmailSearchService` (transient), `IEmailMetadataExtractor` / `BasicEmailMetadataExtractor` (transient), and any services created in TASK-1 (`ITransmittalService`, `IUploadOrchestrator`, etc.).
- `MainWindow`, `HomeWindow`, and other top-level windows resolve their dependencies via constructor injection (not `new` or `ServiceLocator`).
- No `ServiceLocator` anti-pattern (static `IServiceProvider` accessed globally). The provider is passed to window constructors at creation time in `App.xaml.cs`.
- `dotnet build` exits 0.

**Implementation notes:**
- Register `GraphFacade` as a singleton because it holds auth state. Register all stores and repositories as singletons (they are stateless and hold only a connection string). Register services created in TASK-1 as transient.
- In `App.xaml.cs`, after `serviceCollection.BuildServiceProvider()`, resolve the first window with `provider.GetRequiredService<HomeWindow>()` and call `.Show()`.
- Update each window's constructor to accept its dependencies as parameters. WPF does not natively support constructor injection for windows — resolve windows explicitly from the provider in `App.xaml.cs` rather than via `new`.
- Do NOT use `Microsoft.Extensions.Hosting` (the full host builder). Use `ServiceCollection` directly to keep startup lean.
- Do NOT change the `IAuthenticationProvider` / MSAL initialization sequence — it must still run before `GraphFacade` is constructed.

**Verification steps:**
- Grep `Kor.Operations.App.csproj` for `Microsoft.Extensions.DependencyInjection` — present.
- Grep `App.xaml.cs` for `new ServiceCollection()` — exactly one match.
- Grep `MainWindow.xaml.cs` for `new SqlTransmittalsStore`, `new GraphFacade`, `new CoverSheetRenderer` — all return zero results.
- Grep `App.xaml.cs` for `new MainWindow(` — the call passes resolved dependencies or the window is resolved via the container.
- `dotnet build` exits 0.

---

### TASK-11: Expand test coverage

**Priority:** Low
**File(s):**
- `Kor.Operations.App/Kor.Transmittals.App.Tests/` (extend existing test project)
- New test files (see below)

**Dependency:** TASK-1 (service extraction) and TASK-2 (real extractor) should be done first to make services testable. Can proceed partially without them.

**Problem:** The only tests are for CFO metric math. No tests exist for data access, rendering pipeline, Graph facade, email extraction, or any transmittal logic.

**Acceptance criteria:**
- The following test classes exist and all tests pass (`dotnet test` exits 0):
  - `EmailMetadataExtractorTests` — at least 3 tests: valid `.msg` file returns non-null Subject; corrupt file returns partial metadata without throwing; `.eml` file returns non-null Subject.
  - `TransmittalServiceTests` (or equivalent service from TASK-1) — at least 2 tests: happy-path transmittal assembly produces correct `Transmittal` object; missing required field (e.g., null ProjectNumber) produces a validation error or throws `ArgumentException`.
  - `SqlTransmittalsStoreTests` — at least 2 tests using an in-memory SQLite database (via `Microsoft.Data.Sqlite`): `LogTransmittalAsync` inserts a row retrievable by the same ID; `MarkSentAsync` updates the `SentAt` timestamp.
  - `GraphFacadeTests` — at least 1 test: `SendMailAsync` with a mocked `GraphServiceClient` (use `Moq` or a hand-rolled fake) verifies that `/users/{upn}/sendMail` is called with the correct subject and recipient.
- Test project references `xUnit` and `xUnit.runner.visualstudio` (replace the existing hand-rolled `AssertEqual` runner).
- All pre-existing CFO metric tests pass under xUnit (migrate `Program.cs` assertions to `[Fact]` methods).
- `dotnet test` exits 0.

**Implementation notes:**
- Add these NuGet packages to the test project: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `Moq`, `Microsoft.Data.Sqlite`.
- For `SqlTransmittalsStoreTests`: create the schema in SQLite using `CREATE TABLE` statements that mirror the real SQL Server schema. Pass the SQLite connection string to `SqlTransmittalsStore`.
- For `.msg` test fixtures: copy one real (non-sensitive) `.msg` file into a `TestData/` folder in the test project, marked as `CopyToOutputDirectory = Always`.
- Do NOT write integration tests that require a live SQL Server, Deltek ODBC, or Microsoft Graph — all external dependencies must be mocked or replaced with in-memory equivalents.
- Do NOT delete the existing `Program.cs` CFO test logic — migrate it to `[Fact]` methods in a `CfoMetricTests.cs` file, then delete `Program.cs`.

**Verification steps:**
- `dotnet test Kor.Operations.App.sln` exits 0.
- Test output lists at least 11 passing tests (3 extractor + 2 service + 2 SQL + 1 Graph + 3 migrated CFO).
- Grep test project `.csproj` for `xunit` — present.
- Grep test project for `Program.cs` — file does not exist (migrated).
- Confirm `TestData/` folder contains at least one `.msg` fixture file.

---

### TASK-12: Implement or remove DataFacade

**Priority:** Low
**File(s):**
- `Kor.Operations.Data/DataFacade.cs`
- Any files that reference `DataFacade` (grep to find)

**Problem:** `DataFacade.cs` exists as a stub in `Kor.Operations.Data` but is either a placeholder for planned functionality or dead code. Its current state adds confusion without value.

**Acceptance criteria:**
- **Either** (A) `DataFacade` is implemented as a unified entry point that exposes all SQL store operations (delegating to `SqlTransmittalsStore`, `SqlUserPreferencesStore`, `SqlEmailIndexStore`, `SqlFinancialPortfolioSnapshotStore`) through a single `IDataFacade` interface, **or** (B) `DataFacade.cs` is deleted and all references to it are removed.
- If option A: `IDataFacade` is defined in `Kor.Operations.Data`, `DataFacade` implements it, and at least one call site in `Kor.Operations.App` uses `IDataFacade` instead of the individual store types.
- If option B: no file named `DataFacade.cs` exists; grep for `DataFacade` in `.cs` files returns zero results.
- `dotnet build` exits 0 under either option.

**Implementation notes:**
- Before choosing an option: grep all `.cs` files for `DataFacade` to determine if it is already referenced anywhere. If it has zero call sites, delete it (option B). If it has call sites, implement it (option A).
- If implementing option A: `DataFacade` should be a thin delegator — do NOT move SQL logic into it, only forward calls. Constructor-inject all four store types.
- If deleting (option B): also check `Kor.Operations.Data.csproj` for any explicit `<Compile>` reference to remove.
- Do NOT implement option A purely speculatively if current call sites are zero — prefer deletion.

**Verification steps:**
- If option A: grep for `IDataFacade` — at least one definition and one usage in App project. `dotnet build` exits 0.
- If option B: `DataFacade.cs` does not exist on disk. Grep for `DataFacade` in `.cs` and `.csproj` files — zero results. `dotnet build` exits 0.

---

_End of remediation task list. 12 tasks total: 2 High, 4 Medium, 6 Low._

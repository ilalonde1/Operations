# .NET Operations App Code Review Report
**Date:** 2026-03-12  
**Reviewed By:** Codex AI  
**Severity Legend:** 🔴 Critical | 🟠 High | 🟡 Medium | 🟢 Low | 💡 Suggestion

## Executive Summary
This repository is a .NET 8 WPF desktop application with supporting class libraries for data access, Microsoft Graph integration, PDF rendering, and email indexing. The high-level intent resembles a layered desktop app, but the boundaries are inconsistently enforced: several windows and workflow services still reach directly into configuration, SQL, ODBC, file storage, and security concerns. The most serious issues are security-related: plaintext secrets remain in `App.config`, SQL transport is configured with `TrustServerCertificate=True`, and authorization defaults to allow when a security group entry is missing. The codebase also has major maintainability pressure from very large code-behind files, duplicate financial UI implementations, global singletons, and widespread swallowed exceptions that make failures difficult to detect or diagnose.

## Overall Scores
| Category | Score (/10) | Notes |
|---|---:|---|
| Architecture & Structure | 5 | Multi-project split is sensible, but UI, orchestration, data, and security concerns are still mixed. |
| Modularity & SOLID | 4 | DI exists, but many services bypass it with `new`, statics, reflection, and direct config access. |
| Modern .NET Best Practices | 6 | Nullable and records are used in places, but startup blocking, static globals, and ad hoc config patterns remain. |
| Performance | 5 | Several sync I/O paths are wrapped in `Task.Run`; Graph folder traversal is chatty; duplication amplifies cost. |
| Error Handling & Resilience | 4 | Retry policy exists, but exception swallowing is pervasive and inconsistent. |
| Security | 3 | Plaintext credentials, certificate trust bypass, permissive authorization fallback, and raw HTML propagation are major issues. |
| Maintainability & Code Quality | 4 | Large files, duplicated features, magic strings, and reflection-based contracts increase change risk. |
| Testing | 5 | Core service coverage exists, but UI/security/financial workflows are largely untested and test setup is brittle. |
| Dependency Management | 6 | Most packages are reasonably current, but a few are stale and the test/dependency wiring is fragile. |

## Findings by Category

### [ARCH-001] Startup composition root has become a god class
**Severity:** 🟠 High  
**File(s):** `Kor.Operations.App/App.xaml.cs` (lines 38, 55, 57, 156, 247, 344, 452, 544)  
**Issue:** `App.xaml.cs` owns environment validation, secret migration, Graph authentication, DI container construction, command-line mode routing, single-instance IPC, startup window selection, and pipe-server message handling. This is no longer just a composition root; it contains business and operational workflows.  
**Impact:** Startup changes are risky, difficult to test, and easy to regress. Authentication, file-picker mode, quick-transfer mode, and IPC forwarding are all coupled to one class.  
**Recommended Fix:** Split startup responsibilities into dedicated services and keep `App` as a thin shell.
```csharp
public interface IStartupCoordinator
{
    Task<Window> CreateStartupWindowAsync(string[] args, CancellationToken ct);
}

public sealed class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        var coordinator = _services.GetRequiredService<IStartupCoordinator>();
        MainWindow = await coordinator.CreateStartupWindowAsync(e.Args, CancellationToken.None);
        MainWindow.Show();
    }
}
```

### [ARCH-002] Presentation layer owns persistence, authorization, storage, and workflow logic
**Severity:** 🟠 High  
**File(s):** `Kor.Operations.App/StandardDetails/StandardDetailsWindow.xaml.cs` (lines 21, 81, 104, 126, 351, 639, 871, 1124, 1326, 1588), `Kor.Operations.App/DashboardWindow.xaml.cs` (lines 49, 295, 339, 403)  
**Issue:** `StandardDetailsWindow` and `DashboardWindow` directly read configuration, check authorization, open SQL connections, execute commands, manage file storage behavior, and drive workflow state. `StandardDetailsWindow.xaml.cs` alone is 1,400+ lines.  
**Impact:** UI changes can break data integrity and security behavior. The code is hard to unit test and difficult to reuse outside WPF.  
**Recommended Fix:** Move queries and workflow operations into application services and repositories, and bind the windows to view models that call those abstractions.
```csharp
public interface IStandardDetailsService
{
    Task<IReadOnlyList<DocumentRowDto>> SearchAsync(StandardDetailsQuery query, CancellationToken ct);
    Task PublishAsync(long documentVersionId, UserContext user, CancellationToken ct);
}
```

### [SOLID-001] Dependency inversion is undermined by globals and ad hoc construction
**Severity:** 🟠 High  
**File(s):** `Kor.Operations.Graph/GraphFacade.cs` (lines 45-62), `Kor.Operations.App/Services/TransmittalService.cs` (line 49), `Kor.Operations.App/Services/UploadOrchestrator.cs` (lines 48, 72, 81), `Kor.Operations.App/Financials/ExecutiveSummaryService.cs` (lines 19-21), `Kor.Operations.App/Financials/GlProfitLossWindow.xaml.cs` (lines 79, 165), `Kor.Operations.App/Financials/GlProfitLossView.xaml.cs` (lines 78, 164)  
**Issue:** Core dependencies are accessed via `GraphFacade.Instance` or constructed inline with `new`. Financial services construct repositories and loaders directly instead of accepting abstractions.  
**Impact:** Testing requires singleton shims and reflection helpers, feature seams are weak, and replacing Graph/data implementations is unnecessarily hard.  
**Recommended Fix:** Register interfaces and inject them throughout.
```csharp
services.AddSingleton<IGraphFacade, GraphFacade>();
services.AddTransient<IGlProfitLossService, GlProfitLossService>();

public sealed class TransmittalService
{
    private readonly IGraphFacade _graph;
    public TransmittalService(IGraphFacade graph, IUploadOrchestrator uploads, ITransmittalsStore store) { ... }
}
```

### [BEST-001] Startup blocks the UI thread on interactive Graph authentication
**Severity:** 🟠 High  
**File(s):** `Kor.Operations.App/App.xaml.cs` (lines 572-579)  
**Issue:** `EnsureGraphInitializedForDelegatedAuth()` calls `CreateAsync(...).GetAwaiter().GetResult()` and `EnsureSignedInAsync(...).GetAwaiter().GetResult()` during startup.  
**Impact:** Startup hangs and deadlock-like behavior are possible, especially if token cache or interactive sign-in is slow. The UI cannot show a responsive shell while auth initializes.  
**Recommended Fix:** Make auth initialization asynchronous and move it behind a startup splash/progress flow.
```csharp
private static async Task InitializeGraphAsync()
{
    var provider = await MsalGraphAuthenticationProvider.CreateAsync(tenantId, clientId, scopes, loginHint);
    await provider.EnsureSignedInAsync(loginHint);
    GraphFacade.Initialize(provider, driveId);
}
```

### [PERF-001] Graph folder creation performs chatty N+1 remote enumeration
**Severity:** 🟠 High  
**File(s):** `Kor.Operations.Graph/GraphFacade.cs` (lines 373-410)  
**Issue:** `EnsureFolderPathAsync` fetches the full child collection for every path segment, then searches it in memory.  
**Impact:** Deep folder paths amplify Graph round trips and latency. Under larger document libraries this becomes a noticeable bottleneck and raises throttling risk.  
**Recommended Fix:** Use `ItemWithPath` or cache resolved path segments instead of enumerating children repeatedly.
```csharp
var item = await _graph.Drives[driveId]
    .Root
    .ItemWithPath(relativePath)
    .GetAsync(cancellationToken: ct);
```

### [PERF-002] CPU and blocking I/O are offloaded with `Task.Run` instead of being designed as async boundaries
**Severity:** 🟡 Medium  
**File(s):** `Kor.Operations.App/Financials/FinancialsService.cs` (lines 27-42), `Kor.Operations.App/Financials/ExecutiveSummaryService.cs` (lines 45-77), `Kor.Operations.App/Financials/GlProfitLossService.cs` (lines 21, 82, 654), `Kor.Operations.App/Services/DeltekHeadshotProvider.cs` (lines 23, 46, 82), `Kor.Operations.Rendering/CoverSheetRenderer.cs` (lines 86-120)  
**Issue:** Several services wrap synchronous DB, ODBC, rendering, and file work in `Task.Run` to appear asynchronous.  
**Impact:** This consumes thread-pool threads under load, weakens cancellation, and makes latency unpredictable.  
**Recommended Fix:** Isolate true background work behind dedicated services and keep I/O paths explicitly synchronous or explicitly async; do not mix both casually.
```csharp
public Task<FinancialsSnapshot> GetSnapshotAsync(bool forceRefresh, CancellationToken ct)
    => _snapshotLoader.LoadAsync(forceRefresh, ct);
```

### [ERR-001] Critical operations are wrapped in silent catch blocks
**Severity:** 🟠 High  
**File(s):** `Kor.Operations.App/App.xaml.cs` (lines 163-179, 208-215, 330-337, 382-394), `Kor.Operations.App/Services/TransmittalService.cs` (lines 66-85, 103-115, 154-181), `Kor.Operations.App/Services/MainWindowWorkflowService.cs` (lines 55-62, 276-278), `Kor.Operations.App/Services/HeaderLoader.cs` (lines 160, 187-191)  
**Issue:** Logging, redirect-target inserts, recipient persistence, startup forwarding, and user preference loads fail silently.  
**Impact:** Operators and developers lose observability. Users may think a transmittal was fully logged or tracked when persistence actually failed.  
**Recommended Fix:** Centralize logging and classify failures as fatal, retriable, or non-fatal with explicit telemetry.
```csharp
catch (Exception ex)
{
    _logger.LogWarning(ex, "Failed to persist redirect target for {Recipient}", email);
    failures.Add(email);
}
```

### [SEC-001] Source-controlled configuration still contains plaintext credentials and TLS trust bypass
**Severity:** 🔴 Critical  
**File(s):** `Kor.Operations.App/App.config` (lines 49-54), `Kor.Operations.App/Services/SecretMigrationRunner.cs` (lines 19-50, 134-179)  
**Issue:** `App.config` contains a real SQL login name and a hard-coded password placeholder pattern. Both connection strings also set `TrustServerCertificate=True`. `SecretMigrationRunner` attempts cleanup only at startup and only when running elevated, which does not protect the repository or non-admin environments.  
**Impact:** Secrets leak through source control, local copies, build artifacts, and logs. TLS trust bypass leaves SQL transport vulnerable to certificate spoofing.  
**Recommended Fix:** Remove all credentials from the repo, fail fast if env/secret store values are missing, and require proper server certificate validation.
```xml
<add name="KorTransmittalsDb"
     connectionString="Server=KOR-APP01\SQLEXPRESS;Database=KorTransmittals;Encrypt=True;" />
```
```csharp
var cs = builder.Configuration.GetConnectionString("KorTransmittalsDb")
         ?? throw new InvalidOperationException("Missing KorTransmittalsDb");
```

### [SEC-002] Authorization defaults to allow when configuration is missing
**Severity:** 🔴 Critical  
**File(s):** `Kor.Operations.App/Services/SecurityGroupAccess.cs` (lines 11-43), `Kor.Operations.App/StandardDetails/StandardDetailsWindow.xaml.cs` (lines 126-175)  
**Issue:** `IsUserInGroup` returns `true` when `groupName` is empty, when the configured member list is missing, and when parsing yields zero members.  
**Impact:** A missing or malformed config entry silently grants access to protected functionality. This is the opposite of fail-safe authorization.  
**Recommended Fix:** Default to deny and explicitly configure open-access features.
```csharp
if (string.IsNullOrWhiteSpace(raw))
    return false;

if (members.Count == 0)
    return false;
```

### [SEC-003] User-supplied HTML is injected directly into outbound mail bodies
**Severity:** 🟠 High  
**File(s):** `Kor.Operations.App/Services/TransmittalService.cs` (lines 189-225), `Kor.Operations.Graph/GraphFacade.cs` (lines 261-266)  
**Issue:** `remarksHtml` and `signatureHtml` are appended directly into the email HTML body without sanitization.  
**Impact:** Malicious or malformed HTML can be propagated to recipients. Even in an internal desktop app, this can break rendering, embed unintended remote content, or inject unsafe markup into email clients.  
**Recommended Fix:** Sanitize allowed tags/attributes before composing email HTML.
```csharp
var sanitizedRemarks = _htmlSanitizer.Sanitize(request.RemarksHtml ?? string.Empty);
var sanitizedSignature = _htmlSanitizer.Sanitize(request.SignatureHtml ?? string.Empty);
```

### [SEC-004] Security-sensitive identity and authorization rules are configuration-driven and stringly typed
**Severity:** 🟡 Medium  
**File(s):** `Kor.Operations.App/StandardDetails/StandardDetailsWindow.xaml.cs` (lines 126-175), `Kor.Operations.App/App.config` (lines 30-43)  
**Issue:** Role names such as `StandardDetailsAdmins`, `StandardDetailsApprovers`, and `StandardDetailsPublishers` are raw strings spread through UI code and backed by local config member lists.  
**Impact:** Typos or inconsistent deployments weaken authorization and are difficult to audit.  
**Recommended Fix:** Centralize role names and resolve them through a dedicated authorization service.
```csharp
public static class Roles
{
    public const string StandardDetailsAdmins = "StandardDetailsAdmins";
}
```

### [MAINT-001] Duplicate GL P&L implementations create parallel maintenance paths
**Severity:** 🟠 High  
**File(s):** `Kor.Operations.App/Financials/GlProfitLossWindow.xaml.cs` (lines 22-220), `Kor.Operations.App/Financials/GlProfitLossView.xaml.cs` (lines 23-220)  
**Issue:** `GlProfitLossWindow` and `GlProfitLossView` contain largely duplicated loading, scoring, refresh, export, and grid-binding logic.  
**Impact:** Fixes will drift. One surface can get new behavior while the other remains stale, producing inconsistent financial results or UX.  
**Recommended Fix:** Extract a shared view model and reusable presenter/service, then keep the window and user control as thin hosts.
```csharp
public sealed class GlProfitLossPresenter
{
    public Task<GlProfitLossState> RefreshAsync(GlProfitLossQuery query, CancellationToken ct);
}
```

### [MAINT-002] Repository/query duplication already exists between data and UI layers
**Severity:** 🟡 Medium  
**File(s):** `Kor.Operations.Data/SqlTransmittalsStore.cs` (lines 159-296), `Kor.Operations.App/DashboardWindow.xaml.cs` (lines 295-435)  
**Issue:** Dashboard summary and activity queries are implemented once in `SqlTransmittalsStore` and again directly in `DashboardWindow`.  
**Impact:** Query behavior, filters, and projections can diverge. Index tuning and bug fixes must be repeated.  
**Recommended Fix:** Make the window depend on `ITransmittalsStore` only.
```csharp
var rows = await _transmittalsStore.SearchSummaryAsync(text, startUtc, endUtc, ct: ct);
var activity = await _transmittalsStore.LoadActivityAsync(id, ct: ct);
```

### [MAINT-003] Reflection is being used as an application contract
**Severity:** 🟡 Medium  
**File(s):** `Kor.Operations.App/App.xaml.cs` (lines 364-369), `Kor.Operations.App/Services/MainWindowWorkflowService.cs` (lines 157-158), `Kor.Operations.App/DashboardWindow.xaml.cs` (lines 109-110)  
**Issue:** The app uses reflection to read `SelectedProjectNo`, set `ToRecipients`/`CcRecipients`, and set `AvatarImageSource`.  
**Impact:** Renames or refactors can break runtime behavior without compile-time errors.  
**Recommended Fix:** Replace reflection with explicit interfaces or strongly typed properties.
```csharp
public interface IProjectSelectionDialog
{
    string? SelectedProjectNo { get; }
}
```

### [MAINT-004] Magic strings and raw SQL parameter inference reduce clarity and plan stability
**Severity:** 🟡 Medium  
**File(s):** `Kor.Operations.App/Services/MainWindowWorkflowService.cs` (lines 223-260), `Kor.Operations.Data/PreferencesRepository.cs` (lines 54-332), `Kor.EmailSearch.Core/EmailIndexWriter.cs` (lines 119-221), `Kor.Operations.App/StandardDetails/StandardDetailsWindow.xaml.cs` (multiple SQL blocks starting at lines 351, 639, 871, 1124, 1326)  
**Issue:** The code relies heavily on raw SQL text, string status values, and `AddWithValue`.  
**Impact:** Type inference can produce poor query plans; scattered status constants and SQL text make behavior harder to audit.  
**Recommended Fix:** Use explicit parameter types and centralize status values.
```csharp
var p = cmd.Parameters.Add("@upn", SqlDbType.NVarChar, 256);
p.Value = userUpn;
```

### [BEST-002] Configuration management predates modern .NET options patterns
**Severity:** 🟡 Medium  
**File(s):** `Kor.Operations.App/App.xaml.cs` (lines 455-474, 520-542, 544-584), `Kor.Operations.App/Services/EnvironmentSecretOverrides.cs` (lines 18-195), `Kor.Operations.App/Financials/FinancialsService.cs` (lines 53-58), `Kor.Operations.App/Services/DeltekHeadshotProvider.cs` (lines 12-16)  
**Issue:** Settings are read directly from `ConfigurationManager` across the codebase instead of being bound once and injected as typed options.  
**Impact:** Environment-specific behavior is scattered and hard to validate. Tests must mimic global config state.  
**Recommended Fix:** Introduce typed settings classes and bind them in the composition root.
```csharp
public sealed class GraphOptions
{
    public string TenantId { get; init; } = "";
    public string ClientId { get; init; } = "";
    public string DriveId { get; init; } = "";
}
```

### [TEST-001] Test project wiring is brittle and partially bypasses SDK project references
**Severity:** 🟡 Medium  
**File(s):** `Kor.Operations.App/Kor.Transmittals.App.Tests/Kor.Operations.App.Tests.csproj` (lines 27-59)  
**Issue:** The test project references built DLLs via `<Reference HintPath=...>` and uses a custom `MSBuild` target to build dependencies. It also links source files from the app project directly into tests.  
**Impact:** Build order, output path, and incremental test behavior are fragile. Small changes in configuration or permissions can break tests.  
**Recommended Fix:** Replace binary references with normal `ProjectReference` entries and test public contracts rather than linked implementation files.
```xml
<ItemGroup>
  <ProjectReference Include="..\..\Kor.Operations.Data\Kor.Operations.Data.csproj" />
  <ProjectReference Include="..\..\Kor.Operations.Graph\Kor.Operations.Graph.csproj" />
</ItemGroup>
```

### [TEST-002] Coverage is concentrated in a narrow slice of the codebase
**Severity:** 🟡 Medium  
**File(s):** `Kor.Operations.App/Kor.Transmittals.App.Tests/TransmittalServiceTests.cs` (line 10), `Kor.Operations.App/Kor.Transmittals.App.Tests/SqlTransmittalsStoreTests.cs` (line 11), `Kor.Operations.App/Kor.Transmittals.App.Tests/GraphFacadeTests.cs` (line 9)  
**Issue:** Current tests cover transmittal send orchestration, SQL store basics, Graph request serialization, email extraction, and a few CFO metrics. There is no meaningful automated coverage for WPF workflows, startup modes, Standard Details, financial dashboard windows, authorization, secret handling, or config-driven behavior.  
**Impact:** The most complex and riskiest areas of the product remain regression-prone.  
**Recommended Fix:** Add service-level tests around startup coordination, authorization decisions, and Standard Details workflows, then add UI automation for critical WPF flows.
```csharp
[Fact]
public async Task IsUserInGroup_MissingConfig_DeniesAccess()
{
    var auth = new SecurityGroupAuthorizer(options);
    Assert.False(auth.IsInRole(Roles.StandardDetailsAdmins, "user@korstructural.com"));
}
```

### [TEST-003] Current test run is not clean
**Severity:** 🟡 Medium  
**File(s):** `Kor.Operations.App/Kor.Transmittals.App.Tests/SqlTransmittalsStoreTests.cs` (lines 17-48), `Kor.Operations.App/Kor.Transmittals.App.Tests/Kor.Operations.App.Tests.csproj` (lines 54-59)  
**Issue:** `dotnet test Kor.Operations.App\Kor.Transmittals.App.Tests\Kor.Operations.App.Tests.csproj -c Release -v minimal` passed 11 tests and failed 2 tests. Both failures were `SqliteException: SQLite Error 10: 'disk I/O error'` during `SqlTransmittalsStoreTests.InitializeAsync`, and the build emitted multiple `MSB3101` write-access warnings for `obj\Release` cache files.  
**Impact:** CI/local reliability is compromised, and developers cannot trust the suite as a clean gate.  
**Recommended Fix:** Remove the file-backed attached in-memory workaround, use an isolated temp location when needed, and normalize the test build output permissions.
```csharp
var cs = new SqliteConnectionStringBuilder
{
    DataSource = Path.Combine(Path.GetTempPath(), $"transmittals-{Guid.NewGuid():N}.db")
}.ToString();
```

### [DEPS-001] Dependency posture is mostly acceptable, but a few packages and reference practices need attention
**Severity:** 🟡 Medium  
**File(s):** `Kor.Operations.App/Kor.Operations.App.csproj` (lines 128-134, 143), `Kor.Operations.App/Kor.Transmittals.App.Tests/Kor.Operations.App.Tests.csproj` (lines 11-22)  
**Issue:** As of 2026-03-12, several core packages are reasonably current (`Microsoft.Graph 5.103.0`, `Polly 8.5.2`, `ClosedXML 0.105.0`, `QuestPDF 2026.2.3`, `Microsoft.Identity.Client 4.82.1`). The notable laggards are `Microsoft.Web.WebView2` (`1.0.3800.47` in the app vs newer stable releases on NuGet) and `Microsoft.NET.Test.Sdk` (`17.11.1` in tests vs newer stable releases). The app also references `..\..\EmailIndexer\Kor.EmailCommon\Kor.EmailCommon.csproj`, which makes the solution boundary dependent on an external sibling repo path.  
**Impact:** You inherit avoidable browser-runtime and test-host issues, and the solution becomes harder to build reproducibly on another machine.  
**Recommended Fix:** Update stale packages deliberately, remove path assumptions to sibling repos where possible, and introduce central package management if this solution continues to grow.
```xml
<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
```

## Prioritized Refactoring Roadmap
### Phase 1 Critical Fixes (Do Now)
- Remove all credentials from `App.config`, rotate any exposed secrets, and remove `TrustServerCertificate=True` from production SQL connections.
- Change authorization to fail closed when group configuration is missing or empty.
- Stop swallowing persistence/tracking failures silently in transmittal send flows; at minimum log them centrally.
- Isolate Graph startup/auth initialization from the UI thread and surface a controlled startup/auth experience.

### Phase 2 High Priority (This Sprint)
- Extract `StandardDetailsWindow`, `DashboardWindow`, and startup routing logic into testable services and view models.
- Replace `GraphFacade.Instance` and inline `new` construction with injected interfaces.
- Deduplicate the GL P&L window/control implementations behind a shared presenter/view model.
- Remove reflection-based runtime contracts and replace them with typed interfaces/properties.

### Phase 3 Modernization (Next Sprint)
- Introduce typed options for Graph, SQL, ODBC, storage, and security settings.
- Replace widespread `AddWithValue` usage with typed parameters.
- Reduce `Task.Run` wrappers by moving blocking work behind explicit worker services and by adopting async APIs where available.
- Consolidate dashboard and transmittal queries into repositories only.

### Phase 4 Nice-to-Haves
- Add XML docs for public APIs in `Core`, `Data`, `Graph`, and `Rendering`.
- Introduce analyzers/style rules for large file thresholds, forbidden `catch { }`, and forbidden direct `ConfigurationManager` access outside composition.
- Consider introducing a lightweight CQRS/Mediator layer only after the current service boundaries are cleaned up.

## Appendix: Quick Wins
- Replace `SecurityGroupAccess` default returns with `false` when config is missing.
- Remove the test project’s binary `HintPath` references and use `ProjectReference`.
- Replace obvious `AddWithValue` calls on `@upn`, `@id`, and `@like` with explicit `SqlDbType`.
- Extract role names and status values into shared constants/enums.
- Move dashboard queries from `DashboardWindow` to `ITransmittalsStore`.
- Add a logger abstraction and replace empty catch blocks in `App.xaml.cs` and `TransmittalService`.
- Sanitize `remarksHtml` and `signatureHtml` before composing outbound email HTML.
- Replace reflection access to `SelectedProjectNo` and `AvatarImageSource` with typed contracts.
- Add a build/test cleanup step or temp-path strategy for SQLite tests to eliminate the current disk I/O failures.

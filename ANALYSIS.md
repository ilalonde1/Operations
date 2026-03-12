# Kor Operations — Architectural Analysis

_Generated: 2026-03-11_

---

## 1. Directory Structure Overview

```
Operations/
├── Kor.EmailSearch.Core/          (.NET Standard 2.0 — Email index read/write)
│   ├── BasicEmailMetadataExtractor.cs
│   ├── EmailIndexWriter.cs
│   ├── EmailRow.cs
│   ├── EmailSearchService.cs
│   ├── SearchResult.cs
│   └── Kor.EmailSearch.Core.csproj
│
├── Kor.Operations.Core/           (.NET 8.0 — Shared domain models)
│   ├── Models.cs
│   └── Kor.Operations.Core.csproj
│
├── Kor.Operations.Data/           (.NET 8.0-windows — SQL + ODBC data access)
│   ├── DataFacade.cs
│   ├── PreferencesRepository.cs
│   ├── SqlEmailIndexStore.cs
│   ├── SqlFinancialPortfolioSnapshotStore.cs
│   ├── SqlTransmittalsStore.cs
│   ├── SqlUserPreferencesStore.cs
│   ├── VantagepointRepository.cs
│   ├── VpOdbcDsnFactory.cs
│   └── Kor.Operations.Data.csproj
│
├── Kor.Operations.Graph/          (.NET 8.0 — Microsoft Graph SDK wrapper)
│   ├── GraphFacade.cs
│   └── Kor.Operations.Graph.csproj
│
├── Kor.Operations.Rendering/      (.NET 8.0 — PDF generation/extraction)
│   ├── CoverSheetRenderer.cs
│   ├── PdfBookmarkExtractor.cs
│   └── Kor.Operations.Rendering.csproj
│
├── Kor.Operations.App/            (.NET 8.0-windows — WPF desktop app)
│   ├── App.xaml / App.xaml.cs     (Entry point, single-instance, auth init)
│   ├── App.config                 (All runtime config + connection strings)
│   ├── MainWindow.xaml/.cs        (Transmittal wizard, ~2500 lines)
│   ├── HomeWindow.xaml/.cs        (Dashboard)
│   ├── DashboardWindow.xaml/.cs   (Transmittal history/search)
│   ├── EmailSearchWindow.xaml/.cs (Email search UI)
│   ├── EmailFilePickerWindow.xaml/.cs
│   ├── QuickTransferWindow.xaml/.cs
│   ├── QuickTransferRunner.cs
│   ├── InboundUploadRunner.cs
│   ├── PreferencesWindow.xaml/.cs
│   ├── ContactPickerWindow.xaml/.cs
│   ├── BookmarkNotesWindow.xaml/.cs
│   ├── TeamsPickerWindow.xaml/.cs
│   ├── Controls/
│   │   ├── CenteredUniformGrid.cs
│   │   └── KorHeader.xaml/.cs
│   ├── Services/
│   │   ├── DeltekHeadshotProvider.cs
│   │   ├── DeltekHealthProbe.cs
│   │   ├── Dpapi.cs
│   │   ├── EnvironmentSecretOverrides.cs
│   │   ├── HeaderLoader.cs
│   │   ├── MsalGraphAuthenticationProvider.cs
│   │   ├── OdbcSettings.cs
│   │   ├── ProjectRecipientMemory.cs
│   │   ├── SecretMigrationRunner.cs
│   │   └── SecurityGroupAccess.cs
│   ├── Financials/
│   │   ├── FinancialsWindow.xaml/.cs
│   │   ├── FinancialsService.cs
│   │   ├── ExecutiveSummaryService.cs / ViewModel / View / DeltekLoader
│   │   ├── GlProfitLossService.cs / Window / View
│   │   ├── ProfitLossReportService.cs / Window
│   │   ├── ProjectFinancialDetailWindow.xaml/.cs
│   │   ├── FinancialMetricDictionaryWindow.xaml/.cs
│   │   ├── MetricDetailWindow.xaml/.cs
│   │   ├── DeliveryConfidenceCalculator.cs
│   │   ├── DeliveryConfidenceLevel.cs
│   │   ├── DisplayTerms.cs
│   │   ├── DeltekSchemaDumper.cs
│   │   └── CfoMetrics/
│   │       ├── ICfoMetric.cs
│   │       ├── CfoMetricRegistry.cs
│   │       ├── ProjectData.cs
│   │       ├── BudgetBurnRateMetric.cs
│   │       ├── DeliveryConfidenceMetric.cs
│   │       ├── PercentHoursSpentMetric.cs
│   │       └── PortfolioHealthCountsMetric.cs
│   ├── StandardDetails/
│   │   ├── StandardDetailsWindow.xaml/.cs
│   │   ├── CreateStandardDocumentWindow.xaml/.cs
│   │   ├── GroupEditWindow.xaml/.cs
│   │   └── StatusWatermarkRenderer.cs
│   ├── Themes/
│   │   └── KorTheme.xaml
│   ├── Assets/
│   │   ├── Fonts/ (Mulish/Muli TTF)
│   │   ├── logo.png / logo.ico
│   │   ├── QuickRemarksEditor.html
│   │   ├── SignatureEditor.html
│   │   └── tinymce/ (full TinyMCE editor)
│   └── Kor.Transmittals.App.Tests/
│       ├── Program.cs
│       └── Kor.Operations.App.Tests.csproj
│
└── Kor.Operations.App.sln
```

---

## 2. Tech Stack Summary

| Layer | Technology | Version |
|---|---|---|
| UI Framework | WPF (XAML) | .NET 8.0-windows |
| Auth / OAuth | MSAL (`Microsoft.Identity.Client`) | 4.82.1 |
| Graph API | Microsoft Graph SDK v5 (Kiota) | 5.103.0 |
| PDF Generation | QuestPDF (Community License) | 2026.2.3 |
| PDF Reading | PDFsharp-WPF | 6.2.4 |
| HTML Editor | Microsoft.Web.WebView2 + TinyMCE | 1.0.3800.47 |
| Email Parsing | MsgReader | 6.0.9 |
| Excel | ClosedXML | 0.105.0 |
| SQL ORM | Dapper | 2.1.66 |
| SQL Client | Microsoft.Data.SqlClient | 6.1.4 |
| ODBC | System.Data.Odbc | 10.0.3 |
| Azure Auth | Azure.Identity | 1.18.0 |
| Database A | SQL Server (KorTransmittals) | KOR-APP01\SQLEXPRESS |
| Database B | SQL Server (KorEmailIndex) | KOR-APP01\SQLEXPRESS |
| ERP | Deltek Vantagepoint (ODBC DSN) | — |
| Cloud Storage | SharePoint Online (via Graph API) | — |
| Platform | Windows 11, x64 | — |
| Target Runtimes | .NET 8.0-windows / .NET Standard 2.0 | — |

---

## 3. Application Flow

### 3a. Startup & Routing

```
App.exe [args]
    │
    ├─ Validate env vars (KOR_DB_USER, KOR_DB_PASSWORD, KOR_ODBC_USER, KOR_ODBC_PASSWORD)
    ├─ SecretMigrationRunner.RunOnceAtStartup()
    ├─ EnvironmentSecretOverrides.Apply()
    ├─ Clear proxy env vars (fixes ODBC)
    │
    ├─ Single-instance check (Mutex + NamedPipes)
    │     └─ If another instance running → forward args via pipe → exit
    │
    ├─ GraphFacade.Initialize(MsalGraphAuthenticationProvider, driveId)
    │
    └─ Route by args:
         --file-picker         → EmailFilePickerWindow
         --file-emails=<paths> → EmailFilePickerWindow
         --quick-transfer      → QuickTransferWindow
         --email-search        → EmailSearchWindow
         <file paths>          → MainWindow (preset files)
         (none)                → HomeWindow
```

### 3b. Transmittal Creation Flow

```
HomeWindow
    └─ "New Transmittal" → MainWindow
         │
         ├─ Project search:
         │     ProjectIndex.Search() ──────────── file-system (\\KOR-FS01\Projects)
         │     PreferencesRepository.SearchProjectsAsync() ── SQL autocomplete
         │
         ├─ Contact/recipient search:
         │     VantagepointRepository.SearchPeopleAsync() ── Deltek ODBC
         │     (Union of CRM Contacts + EMMain employees)
         │
         ├─ File attachment:
         │     Drag-drop or browse → TransmittalFile list
         │     PdfBookmarkExtractor.TryGetBookmarks() ── (if "Site Instructions")
         │
         ├─ [User edits remarks in WebView2/TinyMCE]
         │
         ├─ Preview cover sheet:
         │     CoverSheetRenderer.RenderAsync() ── QuestPDF → PDF on disk
         │
         ├─ Send:
         │     GraphFacade.UploadWithProgressAsync() ── files → SharePoint (5 MiB chunks)
         │     GraphFacade.CreateLinksAsync() ── internal + optional external link
         │     GraphFacade.SendMailAsync() ── /users/{upn}/sendMail (Graph API)
         │
         └─ Log:
               SqlTransmittalsStore.LogTransmittalAsync()
               SqlTransmittalsStore.AddRecipientsAsync()
               SqlTransmittalsStore.MarkSentAsync()
```

### 3c. Email Filing Flow (Outlook Add-In Trigger)

```
Outlook VSTO Add-In
    └─ App.exe --file-emails="path1.msg|path2.msg"
         │
         └─ EmailFilePickerWindow
               └─ [User selects project]
                    └─ EmailIndexWriter.UpsertEmailAsync(projectNumber, filePath)
                          ├─ ComputeSha1Hex(file) ── dedup hash
                          ├─ BasicEmailMetadataExtractor.ExtractAsync() ── filename → Subject
                          └─ INSERT/UPDATE dbo.Emails (KorEmailIndex)
```

### 3d. Email Search Flow

```
App.exe --email-search
    └─ EmailSearchWindow
          └─ EmailSearchService.SearchAsync(query, project, dateRange, page)
                └─ dbo.SearchEmailsPaged (SQL full-text stored proc)
                      └─ Returns paginated List<EmailRow>
```

### 3e. Quick Transfer Flow

```
App.exe --quick-transfer
    └─ QuickTransferWindow
          └─ QuickTransferRunner.RunAsync(request)
                ├─ GraphFacade.UploadWithProgressAsync()
                ├─ GraphFacade.CreateLinksAsync()
                ├─ GraphFacade.SendMailAsync()
                └─ SqlTransmittalsStore.LogTransmittalAsync()
```

### 3f. Financials Flow

```
HomeWindow → FinancialsWindow
    ├─ SecurityGroupAccess.IsUserInGroup("Financials") ── config-gated
    │
    ├─ ExecutiveSummaryDeltekLoader
    │     ├─ FinancialsService.QueryDeltek() ── Deltek ODBC
    │     └─ SqlFinancialPortfolioSnapshotStore ── SQL snapshot cache
    │
    ├─ CfoMetricRegistry.GetAllMetrics()
    │     ├─ DeliveryConfidenceMetric
    │     ├─ PercentHoursSpentMetric
    │     ├─ BudgetBurnRateMetric
    │     └─ PortfolioHealthCountsMetric
    │
    └─ ExecutiveSummaryViewModel ── binds to ExecutiveSummaryView (XAML)
```

---

## 4. Module Dependency Map

```
                     ┌──────────────────────────┐
                     │   Kor.Operations.Core     │
                     │   (Models.cs — DTOs only) │
                     └────────────┬─────────────┘
                                  │ (referenced by all)
          ┌───────────────────────┼─────────────────────────┐
          │                       │                         │
┌─────────▼────────┐   ┌──────────▼──────────┐   ┌─────────▼────────────┐
│ Kor.Operations   │   │ Kor.Operations.Graph │   │ Kor.Operations.      │
│ .Data            │   │ (Microsoft Graph     │   │ Rendering            │
│ (SQL + ODBC)     │   │  SDK facade)         │   │ (QuestPDF +          │
└──────────────────┘   └─────────────────────┘   │  PDFsharp)           │
                                                  └──────────────────────┘
          │                       │                         │
          └───────────────────────┼─────────────────────────┘
                                  │
                     ┌────────────▼─────────────┐
                     │   Kor.Operations.App      │
                     │   (WPF, entry point)      │
                     └────────────┬─────────────┘
                                  │ also references
                                  ▼
                       Kor.EmailSearch.Core
                       (.NET Standard 2.0)
                       (EmailIndexWriter +
                        EmailSearchService)
                                  │
                          Kor.EmailCommon
                          (external project,
                           not in this repo)
```

**External NuGet dependency graph (key packages):**

```
Kor.Operations.App
  ├── Microsoft.Identity.Client 4.82.1        (MSAL / OAuth)
  ├── Microsoft.Web.WebView2 1.0.3800.47      (Chromium for TinyMCE)
  ├── MsgReader 6.0.9                         (MSG email parsing)
  ├── ClosedXML 0.105.0                       (Excel output)
  └── System.Configuration.ConfigurationManager 10.0.3

Kor.Operations.Graph
  └── Microsoft.Graph 5.103.0                 (Graph SDK v5 / Kiota)

Kor.Operations.Rendering
  ├── QuestPDF 2026.2.3                       (PDF generation)
  └── PDFsharp-WPF 6.2.4                      (PDF reading)

Kor.Operations.Data
  ├── Microsoft.Data.SqlClient 6.1.4
  ├── Dapper 2.1.66
  └── System.Data.Odbc 10.0.3

Kor.EmailSearch.Core
  ├── Microsoft.Data.SqlClient 6.1.4
  └── Dapper 2.1.66

All projects:
  └── Azure.Identity 1.18.0
```

**No circular dependencies detected.**

---

## 5. Key Architectural Patterns

| Pattern | Where Used | Notes |
|---|---|---|
| **Facade** | `GraphFacade`, `DataFacade` | Hides SDK/DB complexity behind a single surface |
| **Repository** | `VantagepointRepository`, `PreferencesRepository`, `SqlTransmittalsStore` | Consistent data-access interfaces |
| **Factory** | `VpOdbcDsnFactory`, `CfoMetricRegistry` | Object creation abstracted |
| **Plugin / Registry** | `CfoMetricRegistry` + `ICfoMetric` | New metrics added without touching existing code |
| **Single-Instance** | `App.xaml.cs` (Mutex + NamedPipes) | Subsequent launches route to running instance via pipe |
| **Command routing** | `App.OnStartup()` arg parsing | CLI-style launch modes (`--quick-transfer`, `--email-search`, etc.) |
| **IProgress decoupling** | `GraphFacade.UploadWithProgressAsync` | Upload logic independent of UI layer |
| **Multi-level caching** | `HeaderLoader` | In-memory `ConcurrentDictionary` + 7-day disk cache |
| **MSAL delegated auth** | `MsalGraphAuthenticationProvider` | Silent token acquisition + interactive fallback, DPAPI token cache |
| **Tiered architecture** | Core → Data/Graph/Rendering → App | Clean layering; no upward references |
| **Partial MVVM** | `Financials/` module | Only Financials uses a ViewModel; all other windows use code-behind |

---

## 6. Key Findings & Observations

### Architecture
- **Tiered but not DI-driven.** Layer separation is clean (Core → Data/Graph/Rendering → App), but there is no dependency injection container. Interfaces exist (`IEmailMetadataExtractor`, `ITransmittalsStore`, `IOdbcConnectionFactory`) but are wired manually, making unit testing difficult outside the CFO metrics.
- **GraphFacade is a singleton** initialized at startup. All windows share the same Graph client and auth context. Appropriate for a single-user desktop app; would need revisiting for multi-user or multi-tenant scenarios.
- **MainWindow is a God Object.** ~2500 lines of code-behind mixing UI events, business logic, data access calls, upload orchestration, and logging. The single largest architectural debt in the codebase.
- **Financials module is the best-factored area.** It uses a proper ViewModel + View split and a plugin-style metric registry.
- **Email extraction is a placeholder.** `BasicEmailMetadataExtractor` ignores all email fields except the subject (inferred from filename). The comment references a future MsgReader-based implementation; `MsgReader` is already a declared dependency.
- **Config-driven security groups** (`SecurityGroupAccess`) is simple and auditable but requires App.config edits to grant/revoke access with no admin UI.
- **A companion tracking service exists** (`RedirectorBaseUrl` in App.config) that records open/click events back to `KorTransmittalsDb`. That service is not in this repo.
- **An Outlook VSTO Add-In exists** (not in this repo) and is the primary caller of `--file-emails`, `--quick-transfer`, and `--email-search` launch modes. The inter-process contract lives only in `App.OnStartup()`.

### Positive Highlights
- `async/await` throughout; no `Task.Wait()` or `.Result` blocking calls observed.
- MSAL token cache is DPAPI-encrypted — no plaintext credentials on disk.
- `PdfBookmarkExtractor` is fully defensive (never throws; returns empty list on any error).
- `GraphFacade.TryGet*` methods are non-throwing — good resilience convention.
- `VpOdbcDsnFactory` uses a builder pattern to avoid raw DSN string concatenation.

---

## 7. Suspected Issues & Code Smells

### High Priority

| # | Issue | Location | Description |
|---|---|---|---|
| 1 | **God Object** | `MainWindow.xaml.cs` | ~2500-line code-behind mixes UI, business logic, and data access. Difficult to test or extend. |
| 2 | **Placeholder email extractor** | `BasicEmailMetadataExtractor` | All email metadata fields except Subject (filename-derived) are null. Full-text search is severely limited until replaced with a MsgReader-based implementation. |

### Medium Priority

| # | Issue | Location | Description |
|---|---|---|---|
| 3 | **Reflection for property access** | `GraphFacade.SendMailAsync`, `CoverSheetRenderer` | Uses `GetProp<T>(object, "PropertyName")` string reflection. Silent breakage on property rename; no compile-time safety. |
| 4 | **Inconsistent nullable annotations** | `App.xaml.cs` (`#nullable disable`) | Mixed `#nullable enable`/disable across files. Potential null-reference exceptions in disabled regions. |
| 5 | **Hardcoded network path** | `MainWindow.xaml.cs` ~line 50 | `\\KOR-FS01\Projects\Projects` is hardcoded. Should be driven by `AppConfig.ProjectsRoot` (the field exists but appears unused here). |
| 6 | **Dynamic SQL concatenation** | `VantagepointRepository` | `ORDER BY` clause and table names are concatenated strings. Low exploitability (internal ODBC, no untrusted input in ORDER BY), but still a principle-of-least-privilege concern. |

### Low Priority

| # | Issue | Location | Description |
|---|---|---|---|
| 7 | **Magic config-key strings** | `MsalGraphAuthenticationProvider`, `DeltekHeadshotProvider`, etc. | App.config keys are bare string literals scattered across classes. A typo causes silent fallback to null/default. |
| 8 | **No retry / backoff** | `GraphFacade`, SQL stores | Graph API and SQL calls have no retry policy. Transient failures surface immediately as user-visible errors. Consider Polly for production robustness. |
| 9 | **Named pipe timeout too short** | `App.xaml.cs OnStartup()` | `NamedPipeClientStream.Connect(2000)` may fail on a slow machine at startup, incorrectly preventing a second launch from forwarding its args to the running instance. |
| 10 | **No DI container** | App-wide | Manual constructor wiring makes unit testing outside CFO metrics difficult. `Microsoft.Extensions.DependencyInjection` would be a low-friction addition. |
| 11 | **Thin test coverage** | `Kor.Transmittals.App.Tests` | Only CFO metric math is tested. No tests for data access, rendering, Graph facade, email extraction, or transmittal wizard logic. |
| 12 | **DataFacade is a stub** | `Kor.Operations.Data/DataFacade.cs` | Appears to be an unfilled placeholder for unified data access. Either implement or remove. |
| 13 | **Opaque external reference** | `Kor.Operations.App.csproj` → `Kor.EmailCommon` | Referenced as a project path but not present in this repo. Its API surface is invisible; breaking changes are undetectable without the full solution. |

---

## 8. ASCII Module Interaction Summary

```
┌───────────────────────────────────────────────────────────────┐
│                    KOR OPERATIONS APP                         │
│                    (WPF Desktop, .NET 8)                      │
│                                                               │
│  ┌──────────────┐  ┌────────────────┐  ┌──────────────────┐  │
│  │  HomeWindow  │  │  MainWindow    │  │ FinancialsWindow  │  │
│  │  (dashboard) │  │ (transmittal   │  │ (CFO dashboard)   │  │
│  └──────┬───────┘  │  wizard)       │  └────────┬─────────┘  │
│         │          └───────┬────────┘           │            │
│         │                  │                    │            │
│  ┌──────▼──────────────────▼────────────────────▼──────────┐ │
│  │                    Services Layer                        │ │
│  │  MsalAuth  HeaderLoader  SecurityGroupAccess             │ │
│  │  DeltekHeadshotProvider  ProjectRecipientMemory          │ │
│  └──┬────────────────────┬───────────────────┬─────────────┘ │
│     │                    │                   │               │
└─────┼────────────────────┼───────────────────┼───────────────┘
      │                    │                   │
      ▼                    ▼                   ▼
┌──────────────┐  ┌────────────────┐  ┌──────────────────┐
│ Graph Facade │  │ Data           │  │ Rendering        │
│              │  │ (SQL + ODBC)   │  │ (QuestPDF +      │
│ Upload files │  │                │  │  PDFsharp)       │
│ Send mail    │  │ Transmittals   │  │                  │
│ Share links  │  │ Preferences    │  │ Cover sheet PDF  │
│ User photos  │  │ Email Index    │  │ Bookmark extract │
└──────┬───────┘  │ Vantagepoint   │  └──────────────────┘
       │          └───────┬────────┘
       ▼                  ▼
SharePoint         SQL Server              Deltek Vantagepoint
Online             KorTransmittals    ←──  (ODBC DSN)
(Graph API)        KorEmailIndex
```

---

_End of analysis._

# KOR Operations Application — Architectural Analysis

_Generated: 2026-03-11_

---

## 1. Directory Structure Overview

```
Operations/
├── Kor.Operations.App/                    [Main WPF Application]
│   ├── App.xaml.cs                       [Application entry point]
│   ├── MainWindow.xaml.cs                [Primary transmittal editor window]
│   ├── HomeWindow.xaml.cs                [Dashboard/home screen]
│   ├── Services/                         [Business logic & utilities]
│   │   ├── MsalGraphAuthenticationProvider.cs
│   │   ├── SecretMigrationRunner.cs
│   │   ├── EnvironmentSecretOverrides.cs
│   │   ├── DeltekHealthProbe.cs
│   │   ├── DeltekHeadshotProvider.cs
│   │   ├── HeaderLoader.cs
│   │   ├── ProjectRecipientMemory.cs
│   │   ├── OdbcSettings.cs
│   │   ├── Dpapi.cs
│   │   └── SecurityGroupAccess.cs
│   ├── Financials/                       [Financial analytics module]
│   │   ├── CfoMetrics/
│   │   │   ├── ICfoMetric.cs
│   │   │   ├── BudgetBurnRateMetric.cs
│   │   │   ├── DeliveryConfidenceMetric.cs
│   │   │   ├── PercentHoursSpentMetric.cs
│   │   │   ├── PortfolioHealthCountsMetric.cs
│   │   │   └── CfoMetricRegistry.cs
│   │   ├── FinancialsService.cs
│   │   ├── ExecutiveSummaryService.cs
│   │   ├── GlProfitLossService.cs
│   │   └── ProfitLossReportService.cs
│   ├── StandardDetails/                  [Standard document handling]
│   ├── Controls/                         [Custom WPF controls]
│   ├── Themes/                           [XAML theme resources]
│   ├── Assets/                           [Fonts, logos, TinyMCE]
│   ├── QuickTransferRunner.cs            [Email file transfer runner]
│   ├── InboundUploadRunner.cs            [Inbound upload handler]
│   └── App.config                        [Configuration file]
│
├── Kor.Operations.Core/                  [Domain models]
│   └── Models.cs                         [Transmittal, Recipient, TransmittalFile, WizardState, AppConfig]
│
├── Kor.Operations.Data/                  [Data access layer]
│   ├── DataFacade.cs                     [Stub data service]
│   ├── SqlTransmittalsStore.cs           [Transmittal persistence]
│   ├── SqlEmailIndexStore.cs             [Email indexing]
│   ├── SqlFinancialPortfolioSnapshotStore.cs
│   ├── SqlUserPreferencesStore.cs        [User preferences]
│   ├── PreferencesRepository.cs
│   ├── VantagepointRepository.cs         [Deltek Vantagepoint queries]
│   └── VpOdbcDsnFactory.cs               [ODBC connection factory]
│
├── Kor.Operations.Graph/                 [Microsoft Graph integration]
│   └── GraphFacade.cs                    [SharePoint/OneDrive uploads, email, links]
│
├── Kor.Operations.Rendering/             [PDF rendering]
│   ├── CoverSheetRenderer.cs             [QuestPDF-based PDF generation]
│   └── PdfBookmarkExtractor.cs           [PDF bookmark extraction]
│
└── Kor.EmailSearch.Core/                 [Email search module]
    ├── EmailSearchService.cs             [SQL full-text search]
    ├── BasicEmailMetadataExtractor.cs
    ├── EmailIndexWriter.cs
    ├── EmailRow.cs
    └── SearchResult.cs

Total Source Files: ~76 C# files (~26,926 lines of code)
```

---

## 2. Tech Stack Summary

### Framework & Platform
- **Platform**: Windows-only (.NET 8.0-windows)
- **UI Framework**: WPF (Windows Presentation Foundation) with XAML
- **Output Type**: WinExe (Windows executable)
- **Target Architecture**: x64

### External NuGet Packages
```
Cloud Integration:
  Microsoft.Graph v5.103.0
  Microsoft.Identity.Client (MSAL) v4.82.1
  Microsoft.Identity.Client.Extensions.Msal v4.82.1
  Azure.Identity v1.18.0

Data Access:
  Microsoft.Data.SqlClient v6.1.4
  System.Data.Odbc v10.0.3 (Deltek ODBC)
  Dapper v2.1.66 (micro-ORM)

PDF & Rendering:
  QuestPDF v2026.2.3 (PDF generation)
  PDFsharp-WPF v6.2.4

Other:
  Microsoft.Web.WebView2 v1.0.3800.47
  MsgReader v6.0.9
  ClosedXML v0.105.0
  System.Configuration.ConfigurationManager v10.0.3
```

### Database Technologies
- **SQL Server**: Microsoft SQL Server Express with SqlClient
- **ODBC**: Progress DataDirect Hybrid driver for Deltek Vantagepoint
- **Connection Pattern**: ADO.NET (direct SqlCommand) + Dapper

### Authentication & Security
- **Microsoft Graph Auth**: MSAL (delegated auth, public client)
- **Scopes**: User.Read, Mail.Send, Files.ReadWrite.All
- **Token Cache**: MSAL token cache to disk
- **Secrets**: Environment variables (KOR_DB_USER, KOR_DB_PASSWORD, KOR_ODBC_USER, KOR_ODBC_PASSWORD)
- **DPAPI**: Data Protection API for local credential encryption

---

## 3. App Flow Diagram

### Startup Sequence
```
Application Start (App.xaml.cs OnStartup)
    │
    ├─ Check required environment variables (DB, ODBC credentials)
    │  └─ If missing → Show error & shutdown
    │
    ├─ Run SecretMigrationRunner (one-time machine env setup)
    ├─ Apply EnvironmentSecretOverrides
    ├─ Clear proxy env vars (ODBC DataDirect workaround)
    │
    └─ Parse command-line arguments
        ├─ --file-picker          → EmailFilePickerWindow → Exit
        ├─ --file-emails=<paths>  → EmailFilePickerWindow (with files) → Exit
        ├─ --quick-transfer       → Initialize Graph → QuickTransferWindow → Exit
        ├─ --email-search         → Initialize Graph → Mutex → NamedPipe → EmailSearchWindow
        └─ (normal / file args)   → Initialize Graph → Mutex → NamedPipe → HomeWindow/MainWindow
```

### Graph Authentication (Delegated MSAL)
```
EnsureGraphInitializedForDelegatedAuth()
    │
    ├─ Read Graph.TenantId, Graph.ClientId, Graph.DriveId from App.config
    ├─ MsalGraphAuthenticationProvider.CreateAsync()
    │   ├─ Build MSAL PublicClientApplication
    │   ├─ Configure disk token cache
    │   ├─ Attempt silent token acquisition
    │   └─ Force interactive sign-in if needed
    │
    └─ GraphFacade.Initialize(authProvider, driveId)
           └─ Ready for all Graph calls
```

### Transmittal Workflow
```
User (MainWindow) builds a Transmittal
    │
    ├─ Selects project, subject, purpose, remarks
    ├─ Picks recipients (To/Cc) and files
    │
    ├─ CoverSheetRenderer.RenderAsync()  →  QuestPDF → PDF on disk
    │
    ├─ GraphFacade.UploadWithProgressAsync()
    │   ├─ Ensure SharePoint folder structure exists
    │   ├─ Create upload session on OneDrive drive
    │   ├─ Chunk-upload (5 MiB chunks) with progress reporting
    │   └─ Return webUrl
    │
    ├─ GraphFacade.CreateLinksAsync()
    │   ├─ org-scoped view link (internal)
    │   └─ anonymous link (external, if requested)
    │
    ├─ GraphFacade.SendMailAsync()
    │   ├─ Build HTML body (purpose + remarks)
    │   └─ Send via signed-in user's mailbox (/users/{upn}/sendMail)
    │
    └─ SqlTransmittalsStore
        ├─ LogTransmittalAsync()    → dbo.Transmittals
        └─ AddRecipientsAsync()     → dbo.TransmittalRecipients (one row per recipient)
```

### Quick Transfer Workflow (Outlook Add-in)
```
User: Outlook → "KOR Quick Transfer" ribbon button
    │
    └─ Outlook spawns: App.exe --quick-transfer --from=X --to=Y --cc=Z --subject=S
        │
        └─ QuickTransferWindow (user selects files)
            │
            └─ QuickTransferRunner.RunAsync()
                ├─ Build header from request args
                ├─ Reserve transmittal number
                ├─ Upload files to SharePoint
                ├─ Create internal/external links
                ├─ Build subject: "RE: [original] - KOR File Transfer"
                ├─ Send email via Graph
                └─ Exit process
```

### Email Search Workflow
```
User: Outlook → "Search Filed Emails" → App.exe --email-search
    │
    └─ EmailSearchWindow
        │
        └─ EmailSearchService.SearchAsync()
            ├─ Build full-text query (quoted tokens, AND logic)
            ├─ Call dbo.SearchEmailsPaged (stored proc)
            │   ├─ Filter: subject/body full-text, project, date range, attachments
            │   └─ Return paginated results
            └─ User clicks result → Opens MSG or EML file
```

### Financial Analytics Workflow
```
User opens FinancialsWindow
    │
    ├─ SecurityGroupAccess.IsAccessAllowed() — gate on App.config group list
    │
    ├─ VantagepointRepository (ODBC → Deltek Vantagepoint)
    │   ├─ dbo.EMMain, EMPhoto       (employees / headshots)
    │   ├─ dbo.PJProjects            (project budgets, hours)
    │   └─ dbo.GLAccounts, GLBudget  (general ledger)
    │
    ├─ CfoMetricRegistry computes metrics:
    │   ├─ PercentHoursSpentMetric      : hoursSpent / hoursBudgeted
    │   ├─ BudgetBurnRateMetric         : percentHoursSpent / percentBilled
    │   ├─ DeliveryConfidenceMetric     : confidence level → score
    │   └─ PortfolioHealthCountsMetric  : healthy / watch / critical counts
    │
    ├─ SqlFinancialPortfolioSnapshotStore → cache snapshot to dbo.FinancialSnapshots
    │
    └─ Display: Executive Summary, G/L P&L, Metric dashboard
```

---

## 4. Module Dependency Map

```
Kor.Operations.Core  (Models: Transmittal, Recipient, TransmittalFile, …)
    └── Referenced by all other modules

Kor.Operations.Data
    ├── Kor.Operations.Core
    └── SQL Server (KorTransmittalsDb, KorEmailIndex), Deltek ODBC

Kor.Operations.Graph
    ├── Kor.Operations.Core
    └── Microsoft.Graph SDK, MSAL

Kor.Operations.Rendering
    ├── Kor.Operations.Core
    ├── QuestPDF
    └── PDFsharp-WPF

Kor.EmailSearch.Core
    ├── Dapper
    └── SQL Server (KorEmailIndex)

Kor.Operations.App  [Hub]
    ├── Kor.Operations.Core
    ├── Kor.Operations.Data
    ├── Kor.Operations.Graph
    ├── Kor.Operations.Rendering
    ├── Kor.EmailSearch.Core
    ├── Kor.EmailCommon (external: EmailIndexer\)
    ├── EmailFilerv2   (external: Email Filer\)
    └── Third-party: Microsoft.Graph, MSAL, WebView2, MsgReader, ClosedXML
```

No circular dependencies detected.

---

## 5. Architectural Patterns

| Pattern | Where Used |
|---------|-----------|
| Facade | `GraphFacade`, `DataFacade` — hide SDK/provider complexity |
| Repository | `SqlTransmittalsStore`, `SqlEmailIndexStore`, `VantagepointRepository`, `SqlUserPreferencesStore` |
| Factory | `VpOdbcDsnFactory`, `CfoMetricRegistry` |
| Strategy | `ICfoMetric` interface + concrete metric classes |
| Single-Instance | Mutex + NamedPipe server; subsequent launches forward commands to running instance |
| Command (CLI) | Command-line arg dispatch (`--quick-transfer`, `--email-search`, `--file-picker`, …) |
| Observer | WPF routed events, `IProgress<T>` for upload progress |
| MVVM (partial) | XAML views + code-behind; some dedicated ViewModel classes (e.g. `ExecutiveSummaryViewModel`) |

---

## 6. External Integration Points

### Azure AD / MSAL
- Tenant: `d9be1f7f-aacf-461a-8d1b-5528b86d540f`
- Client: `69b68cd2-a051-4782-a45e-4f1276942c06` (public client)
- Token cache: `%APPDATA%\Microsoft\IdentityService\msal_cache.db3`

### Microsoft Graph Endpoints
- `POST /users/{upn}/sendMail`
- `POST /drives/{driveId}/items/{folderId}/createUploadSession`
- `POST /drives/{driveId}/items/{folderId}/createLink`
- `GET  /users/{upn}/photo/content`

### SQL Server Databases
- **KorTransmittalsDb**: `dbo.Transmittals`, `dbo.TransmittalRecipients`, `dbo.UserPreferences`, `dbo.FinancialSnapshots`
- **KorEmailIndex**: `dbo.Emails` (full-text indexed), stored proc `dbo.SearchEmailsPaged`

### Deltek Vantagepoint (ODBC)
- DSN: `Deltek` (Progress DataDirect Hybrid driver)
- Credentials: `KOR_ODBC_USER` / `KOR_ODBC_PASSWORD` env vars
- Tables: `dbo.EMMain`, `dbo.EMPhoto`, `dbo.PJProjects`, `dbo.GLAccounts`, `dbo.GLBudget`

### Outlook (VSTO Add-in)
- Ribbon buttons: File with KOR, Send to KOR, KOR Transmittals, Quick Transfer, Search Filed Emails
- Communication: command-line args, temp files (`%APPDATA%\KOR\EmailFilePickerResult.txt`), named pipes

---

## 7. Key Findings & Observations

### Strengths
1. Clear layered architecture: UI → Business Logic → Data Access → Database/Graph
2. Async-aware throughout; non-blocking UI
3. Multi-modal single executable (picker, quick transfer, email search, main app)
4. Delegated MSAL auth — no hardcoded service credentials in auth path
5. DPAPI encryption and security-group feature gating
6. Modern cloud-native file delivery via SharePoint/OneDrive
7. Full audit trail: all transmittals logged to SQL with timestamps, links, recipients

### Naming Conventions
- Classes/Methods: PascalCase
- Private fields: `_camelCase`
- Config keys: `Dot.Notation.PascalCase` (e.g. `Graph.TenantId`)

---

## 8. Suspected Issues & Code Smells

| # | Issue | Severity | Location |
|---|-------|----------|----------|
| 1 | **Azure credentials in source / App.config** — TenantId, ClientId, DriveId, SQL password placeholder all in plaintext | 🔴 Critical | `App.config` |
| 2 | **Reflection-based property setting** — `GetType().GetProperty("EmailSubject")?.SetValue(...)` used to set model properties; silent failure if renamed | ⚠️ High | `App.xaml.cs`, `QuickTransferRunner.cs`, `InboundUploadRunner.cs` |
| 3 | **DataFacade is a stub** — `SearchContactsAsync` always returns empty list; UI calls it expecting real results | ⚠️ Medium | `Kor.Operations.Data/DataFacade.cs` |
| 4 | **Broad exception swallowing** — `catch { }` and `catch (Exception ex) { Debug.WriteLine(...) }` scattered throughout; production errors are silent | ⚠️ Medium | `App.xaml.cs`, `Services/*.cs` |
| 5 | **No structured logging** — no Serilog/NLog; audit events rely on SQL insert success only | ⚠️ Medium | Throughout |
| 6 | **Static GraphFacade** — `GraphFacade.Instance` used directly everywhere; not injectable or mockable | ⚠️ Medium | `MainWindow.xaml.cs`, runners, windows |
| 7 | **ODBC proxy workaround clears all process proxy vars** — `ClearProcessProxyEnvVars()` affects any legitimate proxy usage by the process | ⚠️ Medium | `App.xaml.cs` |
| 8 | **No IoC container** — all dependencies manually constructed; hard to unit-test business logic | ⚠️ Low–Medium | All modules |
| 9 | **Tight coupling of business logic to WPF code-behind** — `MainWindow.xaml.cs` mixes UI event handling with orchestration logic | ⚠️ Low | `MainWindow.xaml.cs` |
| 10 | **ODBC connection pooling not configured** — each repository call may create a new connection; no explicit min/max pool settings | ⚠️ Low | `VpOdbcDsnFactory.cs` |

---

## 9. Recommendations

1. **Move secrets to Azure Key Vault** — remove TenantId, ClientId, DriveId, and SQL credentials from `App.config` and source control.
2. **Add structured logging** — integrate Serilog with a SQL or file sink; replace `Debug.WriteLine` and empty catch blocks.
3. **Eliminate reflection hacks** — add the required properties (`EmailSubject`, `ToRecipients`, etc.) directly to the `Transmittal` model.
4. **Implement or remove DataFacade** — stub returning empty results is misleading; either wire it to a real contact source or delete it.
5. **Add dependency injection** — use `Microsoft.Extensions.DependencyInjection` to register and resolve services; replace static `GraphFacade.Instance`.
6. **Unit test critical business logic** — CFO metrics, email parsing, and transmittal numbering are good candidates; the current static coupling makes this impossible without refactoring.
7. **Scope the ODBC proxy workaround** — configure the DataDirect driver directly rather than clearing process-wide proxy environment variables.
8. **Document the VSTO add-in contract** — the CLI argument protocol between Outlook and this executable should be explicitly documented (it currently exists only implicitly in `App.xaml.cs`).

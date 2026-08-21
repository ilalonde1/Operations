# Cross-Cutting Mechanical Scan — 2026-08-20

Evidence tier: **RUN** (grep over source, excluding bin/obj). Counts are raw matches,
not triaged — a high count is a signal to look, not a verdict. NOTE: "total catch" counts every catch block, not swallowed ones; only "empty catch" indicates silent failure.

## Debt markers by project

| project | .cs | TODO/FIXME/HACK | NotImplemented | empty catch | total catch |
|---|---|---|---|---|---|
| Operations/Kor.Operations.App | 532 | 1 | 29 | 22 | 536 |
| Operations/Kor.Operations.Mcp | 55 | 0 | 0 | 0 | 74 |
| Operations/Kor.Opportunities.Data | 233 | 0 | 2 | 11 | 158 |
| Operations/Kor.Opportunities.Worker | 76 | 0 | 0 | 0 | 126 |
| Operations/Kor.Opportunities.Core | 54 | 0 | 0 | 0 | 0 |
| Operations/Kor.Operations.FileSync.Service | 58 | 1 | 0 | 0 | 84 |
| Operations/Kor.Operations.EngineeringTools.Core | 69 | 0 | 0 | 0 | 4 |
| Operations/Kor.Operations.Business | 26 | 0 | 0 | 12 | 13 |
| Operations/Kor.Operations.Data | 22 | 0 | 0 | 0 | 1 |
| Operations/Kor.Operations.Core | 29 | 0 | 0 | 0 | 5 |
| Operations/Kor.Operations.Graph | 2 | 0 | 0 | 0 | 13 |
| Operations/EmailFiler | 9 | 0 | 2 | 1 | 14 |
| Operations/Kor.EmailSearch.Core | 7 | 0 | 0 | 0 | 1 |
| Redirector | 3 | 0 | 0 | 2 | 2 |
| KOR.RevitTools | 96 | 0 | 0 | 19 | 43 |
| KOR.Drafter | 4 | 0 | 0 | 2 | 5 |

## ⚠ CORRECTION 2026-08-20 — THE "NO HARDCODED SECRETS" FINDING WAS WRONG

**Retracted.** The earlier statement in this file — *"A scan across ~1,300 C# files found no
hardcoded credential, API key, or bearer token in source"* — is **false**, and was false for two
independent reasons. Both are defects in the scan, not in the reporting of it.

1. **The scan covered `.cs` files only.** Credentials in this suite live in `.config`. The scan
   never looked at them.
2. **Even within `.cs`, the pattern was too narrow.** It matched `secret = "..."` but not the
   null-coalescing fallback form `Configuration["X"] ?? "literal-secret"`, which is exactly how
   the redirector embeds its Graph credentials.

### What is actually exposed `[RUN]` `[QUERIED]`

| location | what | tracked in git? |
|---|---|---|
| `Kor.Operations.App/App.config` (connectionStrings) | **Live SQL password for `transmittals_app`** — still the literal scaffold placeholder shipped by the template | **YES** |
| `Kor.Operations.App/App.config` (connectionStrings) | **Live SQL password for `opportunities_app`** | **YES** |
| `Redirector/Kor.Transmittals.Redirector/Program.cs:33` | **Azure AD Graph tenant ID, client ID and client secret**, hardcoded as `??` fallbacks. No `Graph:*` key exists in any appsettings on disk or on the server — **so the fallback is what production uses** | no (dir is untracked) |
| `Redirector/Kor.Transmittals.Redirector/Program.cs` | reCAPTCHA **secret** key, plaintext | no |
| `KOR-APP01` deployed `appsettings.Production.json` (MCP) | Live Anthropic API key, Deltek credentials, SQL password, cleartext | no — server only |

`App.config` being tracked means the two SQL passwords are **in git history**, so rotating the
password is necessary but not sufficient — the history retains them.

### Second correction — the empty-catch counts are UNDERCOUNTS

The module audit of `KOR.RevitTools` counted by hand what this scan counted by regex:

| | this scan | actual |
|---|---|---|
| empty catches in `KOR.RevitTools` | 19 | **83** single-line empty-body, of 187 `catch` keywords; ~129 including comment-only and bare-return bodies |

The `grep -zoP 'catch\s*(\([^)]*\))?\s*\{\s*\}'` pattern only matches a catch whose braces
are empty with nothing but whitespace between them. It misses comment-only bodies, bare `return;`
bodies, and multi-line formatting. **Treat every empty-catch number in the table above as a floor,
not a count.**

Also corrected: this scan's "3 hardcoded paths in KOR.RevitTools tests" are **inline JSON test
fixtures that never touch disk** — not a portability problem. All 79 of its tests pass `[RUN]`.

### Third credential exposure (found by module audit, missed here)

`KOR.RevitTools/PALETTE-README.md:20` commits a **live SQL password** (`standards_reader`).
Confirmed working, and confirmed correctly scoped — a `SELECT` on `analysis.vw_RuleSetting` was
refused. It exists **only on the unmerged `feature/details-palette` branch**, so unlike the
`App.config` passwords it can still be scrubbed before it reaches main.

### Corrected method for any future scan

Search `.cs`, `.config`, `.json`, `.xml`, `.ps1`, **and `.md`** — one live password is in a README. Match at minimum:
`(password|pwd|secret|clientsecret|apikey|api_key)\s*[=:]`, XML `connectionString="..."`
attributes, and the `?? "literal"` fallback form. Grep the *deployed* server config too — the
MCP finding exists only there.

### Standing lesson

An absence-of-evidence result is only as broad as the scan behind it. This file originally
presented a narrow scan's silence as a positive security finding. That was the error.

## Triage of the two flagged categories — BOTH ARE FALSE ALARMS

**Do not report either of these as defects.** Recorded here so downstream analysis does not
re-raise them.

### "Potential hardcoded secrets" — 3 hits, all benign `[RUN]`

All three are in `Kor.Operations.App/Services/AppConfigKeys.cs` (lines 41, 48, 51) and are
*names of configuration keys*, not values:

    public const string VpPassword            = "Vp.Password";
    public const string WatchlistSyncPassword = "WatchlistSync.Password";
    public const string McpServerPassword     = "McpServer.Password";

~~**A scan across ~1,300 C# files found no hardcoded credential, API key, or bearer token in source.**~~ **RETRACTED — see the correction above.** The original (wrong) reasoning was: secrets are externalised
to configuration and environment variables. Verifying where those values actually live at
runtime, and whether they are protected, remains open for the module audits.

### "NotImplementedException / NotSupportedException" — 33 hits, all idiomatic `[RUN]`

Of 33 matches, 30 are `IValueConverter.ConvertBack` / `IMultiValueConverter.ConvertBack` stubs.
Throwing there is the standard WPF idiom for a one-way binding converter — correct, not a gap.
The remaining three:

- `Brochures/BrochureContentStep.xaml.cs:179,193` — also `ConvertBack` stubs
- `Brochures/BrochureProposalPickerWindow.xaml.cs:178` — also a `ConvertBack` stub
- `CompositionModules/RenderingModule.cs:12` — a **deliberate DI guard** carrying an explanatory
  message: `"CoverSheetRenderer is static and is not constructed through DI."`

**Zero of the 33 represent unfinished functionality.**

### What remains genuinely worth investigating

- **~69 empty catch blocks** across the suite — silent failure sites. Concentrated in
  `Kor.Operations.App` (22), `KOR.RevitTools` (19), `Kor.Operations.Business` (12),
  `Kor.Opportunities.Data` (11). These are real and belong to the module audits.
- **59 hardcoded absolute path literals** (`C:\...` or `\server\share\...`) — each one is a
  place the software may only work on one machine. Listed in the section below.

## Potential hardcoded secrets

Pattern hits requiring human review. A hit is not proof; absence of hits IS meaningful.

```
Operations/Kor.Operations.App/Services/AppConfigKeys.cs:41:        public const string VpPassword      = "Vp.Password";
Operations/Kor.Operations.App/Services/AppConfigKeys.cs:48:        public const string WatchlistSyncPassword   = "WatchlistSync.Password";
Operations/Kor.Operations.App/Services/AppConfigKeys.cs:51:        public const string McpServerPassword   = "McpServer.Password";
```

## Hardcoded absolute paths (would break on another machine)

```
Operations/Kor.Operations.App/BusinessDevelopment/Briefs/HtmlBriefPdfGenerator.cs:332:                @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
Operations/Kor.Operations.App/BusinessDevelopment/Briefs/HtmlBriefPdfGenerator.cs:333:                @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
Operations/Kor.Operations.App/Email/EmailFilingService.cs:48:        @"\\kor-fs01\Projects\Reporting\Scripts\Logs";
Operations/Kor.Operations.App/EngineeringTools.Tests/PdfToSafe/FirmDefaultsEdgeCaseTests.cs:211:                    SafeExePath = @"C:\Program Files\Computers and Structures\SAFE 22\SAFE.exe",
Operations/Kor.Operations.App/Kor.Transmittals.App.Tests/TransmittalServiceTests.cs:26:                CoverLocalPath: @"C:\Temp\cover.pdf",
Operations/Kor.Operations.App/Kor.Transmittals.App.Tests/TransmittalServiceTests.cs:84:                        LocalPath = @"C:\Temp\Drawing.pdf",
Operations/Kor.Operations.App/Kor.Transmittals.App.Tests/TransmittalServiceTests.cs:109:        Assert.Equal(@"C:\Temp\cover.pdf", result.CoverLocalPath);
Operations/Kor.Operations.App/Opportunities/HistoricalOpportunityDetailViewModel.cs:145:        // Translate "C:\OpsArchive\..." → "\\KOR-APP01\C$\OpsArchive\..."
Operations/Kor.Operations.App/Opportunities/HistoricalOpportunityDetailViewModel.cs:146:        if (localPath.StartsWith(@"C:\OpsArchive", StringComparison.OrdinalIgnoreCase))
Operations/Kor.Operations.App/Opportunities/HistoricalOpportunityDetailViewModel.cs:148:            var unc = @"\\KOR-APP01\C$" + localPath.Substring(2);
Operations/Kor.Operations.App/StandardDetails/StandardDetailsFileStore.cs:38:            return @"\\Kor-fs01\Drafting";
Operations/Kor.Opportunities.Data/Ingestion/Scraping/AlbertaPurchasingAwardsScraper.cs:453:                Environment.GetEnvironmentVariable("PROGRAMDATA") ?? @"C:\ProgramData",
Operations/Kor.Opportunities.Data/Ingestion/Scraping/AlbertaPurchasingScraper.cs:354:                Environment.GetEnvironmentVariable("PROGRAMDATA") ?? @"C:\ProgramData",
Operations/Kor.Opportunities.Data/Ingestion/Scraping/BcBidAwardsScraper.cs:105:                    Environment.GetEnvironmentVariable("PROGRAMDATA") ?? @"C:\ProgramData",
Operations/Kor.Opportunities.Data/Ingestion/Scraping/BcBidHistoricalScraper.cs:475:                    var dir = @"C:\ProgramData\KorOperations\Opportunities\diagnostics";
Operations/Kor.Opportunities.Data/Ingestion/Scraping/BcBidHistoricalScraper.cs:661:                Environment.GetEnvironmentVariable("PROGRAMDATA") ?? @"C:\ProgramData",
Operations/Kor.Opportunities.Data/Ingestion/Scraping/BcBidHistoricalScraper.cs:768:                    Environment.GetEnvironmentVariable("PROGRAMDATA") ?? @"C:\ProgramData",
Operations/Kor.Opportunities.Data/Ingestion/Scraping/BcBidHistoricalScraper.cs:791:                    Environment.GetEnvironmentVariable("PROGRAMDATA") ?? @"C:\ProgramData",
Operations/Kor.Opportunities.Data/Ingestion/Scraping/BcBidPlanTakerExtractor.cs:202:                Environment.GetEnvironmentVariable("PROGRAMDATA") ?? @"C:\ProgramData",
Operations/Kor.Opportunities.Data/Ingestion/Scraping/BcBidScraper.cs:150:                    Environment.GetEnvironmentVariable("PROGRAMDATA") ?? @"C:\ProgramData",
Operations/Kor.Opportunities.Data/Ingestion/Scraping/BcBidScraper.cs:644:                    Environment.GetEnvironmentVariable("PROGRAMDATA") ?? @"C:\ProgramData",
Operations/Kor.Opportunities.Data/Ingestion/Scraping/BcBidScraper.cs:668:                    Environment.GetEnvironmentVariable("PROGRAMDATA") ?? @"C:\ProgramData",
Operations/Kor.Opportunities.Data/Ingestion/Scraping/BcBidScraper.cs:773:                Environment.GetEnvironmentVariable("PROGRAMDATA") ?? @"C:\ProgramData",
Operations/Kor.Opportunities.Data/Ingestion/Scraping/BcBidUnverifiedBidResultsScraper.cs:857:                Environment.GetEnvironmentVariable("PROGRAMDATA") ?? @"C:\ProgramData",
Operations/Kor.Opportunities.Data/Ingestion/Scraping/BcBidUnverifiedBidResultsScraper.cs:966:                    Environment.GetEnvironmentVariable("PROGRAMDATA") ?? @"C:\ProgramData",
Operations/Kor.Opportunities.Data/Ingestion/Scraping/BcBidUnverifiedBidResultsScraper.cs:989:                    Environment.GetEnvironmentVariable("PROGRAMDATA") ?? @"C:\ProgramData",
Operations/Kor.Opportunities.Data/Ingestion/Scraping/BidsAndTendersAwardsScraper.cs:526:                Environment.GetEnvironmentVariable("PROGRAMDATA") ?? @"C:\ProgramData",
Operations/Kor.Opportunities.Data/Ingestion/Scraping/BidsAndTendersScraper.cs:353:                Environment.GetEnvironmentVariable("PROGRAMDATA") ?? @"C:\ProgramData",
Operations/Kor.Opportunities.Data/Ingestion/Scraping/PlaywrightScraperBase.cs:73:                Environment.GetEnvironmentVariable("PROGRAMDATA") ?? @"C:\ProgramData",
Operations/Kor.Opportunities.Worker/Options/BdPersonResearchExecutorOptions.cs:42:    public string OutputDir { get; set; } = @"C:\ProgramData\KorOperations\Research\people-outputs";
Operations/Kor.Opportunities.Worker/Options/BdProjectResearchExecutorOptions.cs:27:        @"C:\ProgramData\KorOperations\Research\projects-outputs";
Operations/Kor.Opportunities.Worker/Options/BdResearchExecutorOptions.cs:31:    public string OutputDir { get; set; } = @"C:\ProgramData\KorOperations\Research\outputs";
Operations/Kor.Opportunities.Worker/Options/OpportunitiesWorkerOptions.cs:68:    public string BcBidHistoricalDocumentArchiveRoot { get; set; } = @"C:\OpsArchive\Opportunities";
Operations/Kor.Opportunities.Worker/Options/OpportunitiesWorkerOptions.cs:246:        @"C:\ProgramData\KorOperations\DataHealthAudit";
Operations/Kor.Opportunities.Worker/Options/OpportunitiesWorkerOptions.cs:276:        @"C:\ProgramData\KorOperations\BdDeltekLink\nightly";
Operations/Kor.Opportunities.Worker/Options/OpportunitiesWorkerOptions.cs:284:        @"C:\ProgramData\KorOperations\QueueDrain";
Operations/Kor.Opportunities.Worker/Options/OpportunitiesWorkerOptions.cs:286:        @"C:\Program Files\PowerShell\7\pwsh.exe";
Operations/Kor.Opportunities.Worker/Services/BcBidHistoricalDocumentDownloadJob.cs:35:            ? @"C:\OpsArchive\Opportunities"
Operations/Kor.Opportunities.Worker/Services/DataHealthAuditJob.cs:252:                ? @"C:\ProgramData\KorOperations\DataHealthAudit"
Operations/Kor.Opportunities.Worker/Services/Reporting/WeeklyAttackSheetJob.cs:37:        @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
Operations/Kor.Opportunities.Worker/Services/Reporting/WeeklyAttackSheetJob.cs:38:        @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
Operations/Kor.Operations.FileSync.Service/Jobs/ConcreteTestReports/ConcreteTestReportsOptions.cs:18:    public const string DefaultSourceRoot = @"\\KOR-FS01\Library\ADMIN\Concrete Test Reports";
Operations/Kor.Operations.FileSync.Service/Jobs/ConcreteTestReports/ConcreteTestReportsOptions.cs:19:    public const string DefaultMappingXlsx = @"\\KOR-FS01\Library\ADMIN\Concrete Test Reports\_Notes\Concrete Test Report Data.xlsx";
Operations/Kor.Operations.FileSync.Service/Jobs/ConcreteTestReports/ConcreteTestReportsOptions.cs:21:    public const string DefaultOutputRoot = @"\\KOR-FS01\Library\ADMIN\Concrete Test Reports\_Processed";
Operations/Kor.Operations.FileSync.Service/Jobs/MoveReportsToEor/MoveReportsToEorOptions.cs:24:    public const string DefaultAuditLogDir = @"\\KOR-FS01\Projects\Reporting\Number Of Reports";
Operations/Kor.Operations.FileSync.Service/Jobs/MoveReportsToToSend/MoveReportsToToSendOptions.cs:22:    public const string DefaultProjectsRootPath = @"\\KOR-FS01\Projects\Projects";
Operations/Kor.Operations.FileSync.Service/Jobs/MoveReportsToToSend/MoveReportsToToSendOptions.cs:27:    public const string DefaultAuditLogDir = @"\\KOR-FS01\Projects\Reporting\Number Of Reports";
Operations/Kor.Operations.FileSync.Service/Jobs/Watcher/WatcherHostedService.cs:45:        @"\\Newforma\\email($|\\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
Operations/Kor.Operations.FileSync.Service/Jobs/Watcher/WatcherOptions.cs:27:    public const string DefaultWatchPath = @"\\KOR-FS01\Projects\Projects";
Operations/Kor.Operations.FileSync.Service/Jobs/WeeklyPmDeadlines/WeeklyPmDeadlinesOptions.cs:21:    public const string DefaultExcelPath = @"C:\Users\app-admin\KOR - Structured Engineering\Kor Hub - Deltek Connection\Project Deadlines.xlsx";
Operations/EmailFiler/EmailFilerv2/EmailFilerRibbon.cs:429:            @"\\kor-fs01\Projects\Reporting\Scripts\Logs";
Operations/EmailFiler/EmailFilerv2/HostExeResolver.cs:27:                var prodFallback = @"C:\Newerforma\Kor.Operations.App.exe";
Operations/EmailFiler/EmailFilerv2/ItemsToFileProcessor.cs:23:        private const string ProjectsRoot = @"\\Kor-fs01\Projects\Projects";
Operations/EmailFiler/EmailFilerv2/ItemsToFileProcessor.cs:41:            @"\\kor-fs01\Projects\Reporting\Scripts\Logs";
KOR.RevitTools/tests/KOR.RevitTools.Core.Tests/CoreLogicTests.cs:132:                      { "label": "Beam", "familyName": "KOR_SBEAM", "typeName": "Std", "sourceFile": "C:\\fam\\beam.rfa" } ] } ],
KOR.RevitTools/tests/KOR.RevitTools.Core.Tests/DetailsCatalogTests.cs:14:                "{\"detailsPalette\":{\"connectionString\":\"Server=KOR-APP01\\\\SQLEXPRESS;Database=KorStandards;User ID=standards_reader;Password=secret\",\"templatePath\":\"C:\\\\KOR\\\\standards.rvt\",\"showUnverified\":true}}");
KOR.RevitTools/tests/KOR.RevitTools.Core.Tests/DetailsCatalogTests.cs:18:            Assert.Equal(@"C:\KOR\standards.rvt", config.DetailsPalette.TemplatePath);
KOR.Drafter/src/KOR.Drafter.Bridge/BridgeApp.cs:34:                ? r : @"C:\KOR.Drafter";
KOR.Drafter/src/KOR.Drafter.Bridge/BridgeApp.cs:152:                if (File.Exists(@"C:\KOR.Drafter\bridge\SENTRY-OFF") ||
```

_end_

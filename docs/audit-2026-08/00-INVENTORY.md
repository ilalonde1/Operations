# KOR Suite — Mechanical Inventory

Generated 2026-08-20. Machine-produced, no interpretation. Evidence tier: RUN (script output).

LOC = raw line count of .cs excluding bin/obj. 'last' = last commit date touching that project directory.
deps: SQL=SqlClient/SqlConnection, ODBC=Deltek ODBC, GRAPH=Microsoft.Graph, HTTP=HttpClient, SP=SharePoint, AI=Anthropic/OpenAI/Ollama/MCP

## Operations
branch `develop` · last commit **2026-08-15 6ce3e428 The audit brief states the product before it asks about the gap** · commits 90d/30d: **1226 / 107** · uncommitted: 1

| project | tfm | output | .cs | .xaml | LOC | last | deps |
|---|---|---|---|---|---|---|---|
| EmailFilerv2 |  | Library | 9 | 0 | 2793 | 2026-06-15 | SQL |
| Kor.EmailCommon | net8.0 | Library | 2 | 0 | 132 | 2026-08-01 | — |
| Kor.EmailSearch.Core | netstandard2.0 | Library | 7 | 0 | 390 | 2026-08-01 | SQL |
| Kor.Operations.EngineeringTools.Tests | net8.0-windows10.0.19041.0 | Library | 28 | 0 | 7182 | 2026-05-15 | — |
| Kor.Operations.App | net8.0-windows10.0.19041.0 | WinExe | 532 | 127 | 108254 | 2026-08-15 | SQL ODBC GRAPH HTTP SP AI |
| Kor.Operations.App.Tests | net8.0-windows10.0.19041.0 | Library | 78 | 6 | 10737 | 2026-08-01 | SQL ODBC GRAPH SP AI |
| Kor.Operations.Business | net8.0-windows | Library | 26 | 0 | 6063 | 2026-07-31 | ODBC |
| Kor.Operations.Core | net8.0 | Library | 29 | 0 | 1514 | 2026-08-01 | SP |
| Kor.Operations.Data | net8.0-windows | Library | 22 | 0 | 3564 | 2026-08-01 | SQL ODBC SP |
| Kor.Operations.EngineeringTools.Core.Tests | net8.0 | Library | 47 | 0 | 8645 | 2026-08-15 | — |
| Kor.Operations.EngineeringTools.Core | net8.0 | Library | 69 | 0 | 16819 | 2026-08-15 | SQL |
| Kor.Operations.EngineeringTools.TakeoffCli | net8.0 | Exe | 3 | 0 | 3562 | 2026-08-15 | HTTP AI |
| Kor.Operations.FileSync.Service | net8.0 | Library | 58 | 0 | 6919 | 2026-08-15 | SQL ODBC GRAPH HTTP SP |
| Kor.Operations.Graph | net8.0 | Library | 2 | 0 | 1268 | 2026-06-30 | GRAPH HTTP SP |
| Kor.Operations.ImportTool | net8.0-windows | Exe | 2 | 0 | 962 | 2026-04-11 | SQL |
| Kor.Operations.Mcp.Smoke | net8.0-windows | Exe | 32 | 0 | 1434 | 2026-05-14 | SQL ODBC HTTP |
| Kor.Operations.Mcp.Tests | net8.0-windows | Library | 9 | 0 | 835 | 2026-05-14 | SQL |
| Kor.Operations.Mcp | net8.0-windows | Library | 55 | 0 | 7897 | 2026-07-31 | SQL HTTP AI |
| Kor.Operations.Rendering | net8.0 | Library | 21 | 0 | 6329 | 2026-08-01 | SP |
| Kor.Opportunities.ApcImport | net8.0 | Exe | 1 | 0 | 478 | 2026-07-11 | SQL |
| Kor.Opportunities.Capture | net8.0 | Exe | 1 | 0 | 177 | 2026-05-21 | — |
| Kor.Opportunities.Core | net8.0 | Library | 54 | 0 | 3684 | 2026-08-01 | GRAPH |
| Kor.Opportunities.Data.Tests | net8.0 | Library | 9 | 0 | 1519 | 2026-07-13 | SQL |
| Kor.Opportunities.Data | net8.0 | Library | 233 | 0 | 48832 | 2026-08-02 | SQL GRAPH HTTP AI |
| Kor.Opportunities.Worker | net8.0-windows | Library | 76 | 0 | 12995 | 2026-07-18 | SQL ODBC GRAPH HTTP AI |
| _SmokeRun | net8.0-windows | Exe | 4 | 0 | 230 | 2026-06-09 | SQL |
| _SmokeVerify | net8.0-windows | Exe | 2 | 0 | 62 | 2026-06-09 | SQL |
| ApcInterestBackfill | net8.0 | Exe | 1 | 0 | 266 | 2026-06-03 | SQL |
| ApcInterestProbe | net8.0 | Exe | 1 | 0 | 263 | 2026-06-09 | — |
| AwardOllamaBackfill | net8.0 | Exe | 3 | 0 | 656 | 2026-05-25 | SQL HTTP AI |
| BcBidDetailProbe | net8.0 | Exe | 1 | 0 | 73 | 2026-07-12 | — |
| BcBidInterestProbe | net8.0 | Exe | 1 | 0 | 167 | 2026-06-09 | — |
| BcMpiImporter | net8.0 | Exe | 1 | 0 | 861 | 2026-06-15 | SQL |
| BdApolloEnrich | net8.0 | Exe | 1 | 0 | 316 | 2026-06-24 | SQL HTTP |
| BdBriefSmoke | net8.0-windows10.0.19041.0 | Exe | 1 | 0 | 115 | 2026-07-10 | — |
| BdCanonicalDedup | net8.0 | Exe | 1 | 0 | 1765 | 2026-08-01 | SQL |
| BdContactEnrich | net8.0 | Exe | 1 | 0 | 237 | 2026-07-12 | SQL HTTP |
| BdDeltekLink | net8.0 | Exe | 1 | 0 | 846 | 2026-06-18 | SQL ODBC |
| BdHeatGraph | net8.0 | Exe | 1 | 0 | 204 | 2026-06-23 | SQL |
| BdHoningIntelBackfill | net8.0-windows | Exe | 1 | 0 | 102 | 2026-06-09 | SQL |
| BdIntegrityCheck | net8.0 | Exe | 1 | 0 | 200 | 2026-06-25 | SQL |
| BdIntelExtract | net8.0 | Exe | 1 | 0 | 403 | 2026-06-30 | SQL |
| BdOpportunityPurge | net8.0 | Exe | 1 | 0 | 288 | 2026-05-30 | SQL |
| BdOrphanOrgPurge | net8.0 | Exe | 1 | 0 | 596 | 2026-06-15 | SQL |
| BdPersonResearchExecutorSmoke | net8.0-windows | Exe | 1 | 0 | 102 | 2026-06-05 | AI |
| BdProjectResearchExecutorSmoke | net8.0-windows | Exe | 1 | 0 | 154 | 2026-06-09 | AI |
| BdQueueDrainIngest | net8.0-windows | Exe | 1 | 0 | 1168 | 2026-07-09 | SQL |
| BdResearchExecutorSmoke | net8.0-windows | Exe | 1 | 0 | 102 | 2026-06-14 | AI |
| BdResearchImport | net8.0 | Exe | 1 | 0 | 8650 | 2026-07-11 | SQL |
| BdSectorSmoke | net8.0 | Exe | 1 | 0 | 118 | 2026-06-23 | — |
| BdSeedImport | net8.0 | Exe | 1 | 0 | 727 | 2026-07-11 | ODBC |
| BdSynthesisSmoke | net8.0 | Exe | 3 | 0 | 218 | 2026-07-10 | SQL HTTP AI |
| BdVerdictBackfill | net8.0-windows | Exe | 1 | 0 | 162 | 2026-06-09 | SQL |
| BidsAndTendersInterestProbe | net8.0 | Exe | 1 | 0 | 206 | 2026-06-09 | — |
| BulkTenantOnboarder | net8.0 | Exe | 1 | 0 | 252 | 2026-05-26 | SQL |
| DetailPageProbe | net8.0 | Exe | 1 | 0 | 88 | 2026-08-01 | — |
| GovCanEngineeringImport | net8.0 | Exe | 1 | 0 | 309 | 2026-05-26 | SQL |
| MerxProbe | net8.0 | Exe | 2 | 0 | 126 | 2026-07-10 | — |
| OpportunityEnrichmentBackfill | net8.0 | Exe | 1 | 0 | 217 | 2026-07-12 | SQL |
| PrimeRfpClassifierBackfill | net8.0 | Exe | 1 | 0 | 205 | 2026-05-26 | SQL |
| ScraperProbe | net8.0 | Exe | 1 | 0 | 203 | 2026-05-22 | — |

## Redirector
**NOT A GIT REPO**

| project | tfm | output | .cs | .xaml | LOC | last | deps |
|---|---|---|---|---|---|---|---|
| Kor.Transmittals.Redirector | net8.0-windows7.0 | Library | 3 | 0 | 1054 | — | SQL HTTP SP |

## KOR.Drafter
branch `main` · last commit **2026-08-15 3624a61 The bridge exports DXF, and stops cancelling opens it was only being told about** · commits 90d/30d: **69 / 69** · uncommitted: 0

| project | tfm | output | .cs | .xaml | LOC | last | deps |
|---|---|---|---|---|---|---|---|
| KOR.Drafter.Bridge | $(RevitTargetFramework) | Library | 4 | 0 | 3489 | 2026-08-15 | — |

## KOR.RevitTools
branch `feature/details-palette` · last commit **2026-08-06 9687ad8 Swap details palette catalog to SQL** · commits 90d/30d: **35 / 3** · uncommitted: 1

| project | tfm | output | .cs | .xaml | LOC | last | deps |
|---|---|---|---|---|---|---|---|
| KOR.RevitTools.Addin | $(RevitTargetFramework) | Library | 71 | 0 | 10968 | 2026-08-06 | SQL SP |
| KOR.RevitTools.Core | netstandard2.0 | Library | 15 | 0 | 1334 | 2026-08-06 | — |
| KOR.RevitTools.Loader | $(RevitTargetFramework) | Library | 1 | 0 | 223 | 2026-08-01 | SP |
| KOR.RevitTools.Core.Tests | net8.0 | Library | 9 | 0 | 680 | 2026-08-06 | — |

## Contract Radar — **OUT OF SCOPE** (excluded by owner 2026-08-20; separate product, not part of the KOR suite demoed to MVE)
**NOT A GIT REPO**

| project | tfm | output | .cs | .xaml | LOC | last | deps |
|---|---|---|---|---|---|---|---|
| ContractRadar.Api |  | Library | 8 | 0 | 786 | — | SQL |
| ContractRadar.Application |  | Library | 52 | 0 | 1841 | — | — |
| ContractRadar.Domain | net8.0 | Library | 6 | 0 | 96 | — | — |
| ContractRadar.Infrastructure |  | Library | 33 | 0 | 5120 | — | GRAPH HTTP |
| ContractRadar.Worker |  | Library | 3 | 0 | 222 | — | SQL |
| ContractRadar.Application.Tests | net8.0 | Library | 6 | 0 | 832 | — | — |
| ContractRadar.Infrastructure.Tests | net8.0 | Library | 3 | 0 | 153 | — | — |

## KOR Inspections Bookings
branch `develop` · last commit **2026-08-04 b3fe034 Exclude letter-prefixed internal Deltek projects from public search** · commits 90d/30d: **5 / 4** · uncommitted: 0

| project | tfm | output | .cs | .xaml | LOC | last | deps |
|---|---|---|---|---|---|---|---|
| Kor.Inspections.App | net8.0 | Library | 84 | 0 | 14805 | 2026-08-04 | SQL ODBC HTTP |
| Kor.Inspections.Tests | net8.0 | Library | 26 | 0 | 4580 | 2026-07-22 | HTTP |

## Deltek Project Creation — **OUT OF SCOPE** (excluded by owner 2026-08-20; not part of the Operations Brain)
**NOT A GIT REPO**

| project | tfm | output | .cs | .xaml | LOC | last | deps |
|---|---|---|---|---|---|---|---|
| DeltekProjectProvisioning.Api | net8.0 | Library | 23 | 0 | 1776 | — | SQL HTTP |

## DeltekProjectDeadlines — **OUT OF SCOPE** (excluded by owner 2026-08-20; not part of the Operations Brain)
**NOT A GIT REPO**

| project | tfm | output | .cs | .xaml | LOC | last | deps |
|---|---|---|---|---|---|---|---|
| DeltekProjectDeadlines | net8.0 | Exe | 9 | 0 | 2351 | — | ODBC GRAPH |

## App Demo Maker
branch `develop` · last commit **2026-08-01 439a6ec Work-in-progress sweep: commit outstanding changes on develop** · commits 90d/30d: **1 / 1** · uncommitted: 0

| project | tfm | output | .cs | .xaml | LOC | last | deps |
|---|---|---|---|---|---|---|---|
| DemoStudio.Application | net8.0 | Library | 35 | 0 | 2023 | 2026-03-14 | — |
| DemoStudio.Automation.Abstractions | net8.0 | Library | 1 | 0 | 25 | 2026-03-09 | — |
| DemoStudio.Automation.FlaUI | net8.0 | Library | 10 | 0 | 580 | 2026-03-14 | — |
| DemoStudio.Automation.FlaUIRunner | net8.0-windows | Exe | 1 | 0 | 422 | 2026-03-08 | — |
| DemoStudio.Capture.Abstractions | net8.0 | Library | 1 | 0 | 18 | 2026-03-09 | — |
| DemoStudio.Desktop.App | net8.0-windows | WinExe | 92 | 5 | 17586 | 2026-08-01 | HTTP AI |
| DemoStudio.Desktop.Core | net8.0 | Library | 6 | 0 | 167 | 2026-03-09 | — |
| DemoStudio.Desktop.Smoke | net8.0 | Exe | 1 | 0 | 477 | 2026-03-08 | — |
| DemoStudio.Domain | net8.0 | Library | 12 | 0 | 556 | 2026-08-01 | — |
| DemoStudio.Infrastructure | net8.0 | Library | 48 | 0 | 4490 | 2026-08-01 | — |
| DemoStudio.Redaction.Abstractions | net8.0 | Library | 1 | 0 | 16 | 2026-03-09 | — |
| DemoStudio.Desktop.App.Tests | net8.0-windows | Library | 23 | 0 | 3447 | 2026-08-01 | SQL HTTP AI |
| DemoStudio.Desktop.Core.Tests | net8.0 | Library | 1 | 0 | 63 | 2026-03-08 | — |

## Portfolio Website — **OUT OF SCOPE** (excluded by owner 2026-08-20; not part of the Operations Brain)
**NOT A GIT REPO**

| project | tfm | output | .cs | .xaml | LOC | last | deps |
|---|---|---|---|---|---|---|---|
| OilOfTrop.Web | net8.0 | Library | 19 | 0 | 710 | — | — |

## SAFE
**NOT A GIT REPO**

_no .csproj found_

## Kor.Operations.App — feature-folder breakdown

| folder | .cs | .xaml | LOC | last commit | DI-registered |
|---|---|---|---|---|---|
| Brochures | 24 | 8 | 3585 | 2026-05-15 | no |
| BusinessDevelopment | 39 | 16 | 11014 | 2026-07-13 | no |
| Compensation | 5 | 1 | 1146 | 2026-05-15 | yes |
| CompositionModules | 8 | 0 | 866 | 2026-07-26 | no |
| Controls | 9 | 7 | 1385 | 2026-06-20 | no |
| Converters | 5 | 0 | 149 | 2026-06-12 | no |
| Crm | 21 | 6 | 5236 | 2026-07-09 | no |
| Email | 8 | 1 | 1437 | 2026-05-07 | no |
| EngineeringTools.Tests | 28 | 0 | 7182 | 2026-05-15 | no |
| EngineeringTools | 49 | 6 | 13055 | 2026-06-26 | no |
| FeeProposal | 21 | 18 | 2001 | 2026-07-01 | no |
| FileSync | 17 | 4 | 3303 | 2026-05-15 | no |
| Financials | 57 | 7 | 16957 | 2026-08-01 | yes |
| Kor.Transmittals.App.Tests | 78 | 6 | 10737 | 2026-08-01 | no |
| Logging | 3 | 0 | 55 | 2026-03-18 | no |
| Opportunities | 43 | 19 | 8811 | 2026-07-13 | yes |
| Options | 1 | 0 | 73 | 2026-07-08 | no |
| PMTools | 27 | 9 | 5963 | 2026-08-01 | no |
| Preferences | 6 | 0 | 342 | 2026-04-12 | no |
| Scripts | 0 | 0 | 0 | 2026-08-15 | no |
| Services | 38 | 0 | 5086 | 2026-07-26 | no |
| Shared | 2 | 0 | 142 | 2026-04-14 | no |
| StandardDetails | 8 | 3 | 1703 | 2026-05-07 | no |
| Views | 9 | 4 | 1337 | 2026-05-09 | no |

## tools/ CLI fleet — last touched

| tool | LOC | last commit |
|---|---|---|
| ApcInterestBackfill | 266 | 2026-06-03 |
| ApcInterestProbe | 263 | 2026-06-09 |
| AwardOllamaBackfill | 656 | 2026-05-25 |
| BcBidDetailProbe | 73 | 2026-07-12 |
| BcBidInterestProbe | 167 | 2026-06-09 |
| BcMpiImporter | 861 | 2026-06-15 |
| BdApolloEnrich | 316 | 2026-06-24 |
| BdBriefSmoke | 115 | 2026-07-10 |
| BdCanonicalDedup | 1765 | 2026-08-01 |
| BdContactEnrich | 237 | 2026-07-12 |
| BdDeltekLink | 846 | 2026-06-18 |
| BdDocTemplate | 0 | 2026-07-10 |
| BdGatherIntel | 0 | 2026-06-03 |
| BdHeatGraph | 204 | 2026-06-23 |
| BdHoningIntelBackfill | 102 | 2026-06-09 |
| BdIntegrityCheck | 200 | 2026-06-25 |
| BdIntelExtract | 403 | 2026-06-30 |
| BdOpportunityPurge | 288 | 2026-05-30 |
| BdOrphanOrgPurge | 596 | 2026-06-15 |
| BdPersonBriefRepair | 0 | 2026-06-10 |
| BdPersonResearchExecutorSmoke | 102 | 2026-06-05 |
| BdPrimesRecovery | 0 | 2026-06-09 |
| BdProjectResearchExecutorSmoke | 154 | 2026-06-09 |
| BdQueueDrainBatchGenerate | 0 | 2026-07-10 |
| BdQueueDrainIngest | 1168 | 2026-07-09 |
| BdQueueDrainPrompts | 0 | 2026-06-30 |
| BdReportBuilders | 0 | 2026-07-10 |
| BdResearchExecutorSmoke | 102 | 2026-06-14 |
| BdResearchImport | 8650 | 2026-07-11 |
| BdSectorSmoke | 118 | 2026-06-23 |
| BdSeedImport | 727 | 2026-07-11 |
| BdSynthesisSmoke | 218 | 2026-07-10 |
| BdTrackingImport | 0 | 2026-06-30 |
| BdVerdictBackfill | 162 | 2026-06-09 |
| BidsAndTendersInterestProbe | 206 | 2026-06-09 |
| BulkTenantOnboarder | 252 | 2026-05-26 |
| DetailPageProbe | 88 | 2026-08-01 |
| GovCanEngineeringImport | 309 | 2026-05-26 |
| HoningInputs | 0 | 2026-05-30 |
| MerxProbe | 126 | 2026-07-10 |
| OpportunityEnrichmentBackfill | 217 | 2026-07-12 |
| PrimeRfpClassifierBackfill | 205 | 2026-05-26 |
| ScraperProbe | 203 | 2026-05-22 |
| WorkstationOps | 0 | 2026-08-15 |

_end_

# Codex prompt — CA major-projects funnel (Step 3)

> Paste into Codex. Goal + constraints + the exact existing pattern to copy. Do NOT run dotnet build/test (Codex env hangs) — Claude verifies the build locally afterward.

---

**Goal:** Add a California major-projects ingestion funnel to `Kor.Opportunities`, modeled *exactly* on the existing BC/AB major-projects providers + job, so new CA projects auto-ingest into `opportunities.MajorProjectsInventory` (Province='CA') through the `StructuralRelevanceGate` — catching projects at permit/CEQA filing (12–36 months before SE selection).

**Reuse, don't reinvent. Templates to copy:**
- `Kor.Opportunities.Data/Ingestion/Providers/BcMajorProjectsInventoryProvider.cs` (provider structure: HttpClient + connStr + `CanonicalOrgResolver` + logger + maxBytes/maxRows; `SourceType => OpportunitySourceType.MajorProjectsInventory`; `StructuralRelevanceGate.Evaluate()` before any insert; the `(Province, SourceKey)` upsert with name-match COALESCE-fill).
- `Kor.Opportunities.Worker/Services/BcMajorProjectsInventoryJob.cs` (job: `IIngestionDispatcher.RunByNameAsync(SourceName,…)`).

**Build:**
1. **`CaSocrataMajorProjectsInventoryProvider.cs`** (`…Data/Ingestion/Providers/`) — copy the Bc provider; swap the CSV fetch for **Socrata SODA JSON** (`GET <resource>.json?$where=…&$limit=…&$offset=…`, optional `X-App-Token` header, offset paging). Drive endpoint + `$where` filter from `OpportunitySource.BaseUrl`/`sourceConfig` so ONE provider serves SF (`data.sfgov.org/resource/k2ra-p3nq.json`), San Diego County (`data.sandiegocounty.gov/resource/dyzh-7eat.json`), and San Jose (CKAN `datastore_search`). Map units/valuation/type/stories/address → MPI columns; `Province='CA'`; `SourceKey = "<srckey>:<permit#>"`. Filter to KOR's lane (multifamily/commercial + a units/valuation threshold) in `$where`.
2. **`CeqanetMajorProjectsInventoryProvider.cs`** — HTML-scrape `ceqanet.lci.ca.gov/Search/Recent` (paced, browser User-Agent). Extract SCH#, document type (MND/EIR/NOD), lead agency, received date, title, description, county. Keep residential/mixed-use/hotel/commercial/school/hospital; drop road/utility/trail/pipeline. Same gate + upsert; `SourceKey="ceqa:<SCH#>"`. Use the repo's existing HTML approach; if none, minimal/regex parse — do **not** add a new NuGet without checking what's already referenced.
3. **`CaMajorProjectsInventoryJob.cs`** (`…Worker/Services/`) — copy `BcMajorProjectsInventoryJob`; `SourceName = "CA_MajorProjectsInventory"`.
4. **`Program.cs`** — for each provider: `AddHttpClient(nameof(...))` (+ `RetryPolicy`) and `AddSingleton<...>` then `AddSingleton<IOpportunityProvider>(sp => sp.GetRequiredService<...>())` — match the AB/BC block (~lines 502–539). Register the job + cron trigger (~lines 971–993): default cron `"0 30 4 ? * SUN"`, config key `CaMajorProjectsInventoryCronSchedule`.
5. **`ScheduledJobDefinition.cs`** — add `new(nameof(CaMajorProjectsInventoryJob), "CaMajorProjectsInventoryCronSchedule", "0 30 4 ? * SUN", _ => true, "Ingestion")`.
6. **`OpportunitiesWorkerOptions.cs`** — add `CaMajorProjectsInventoryCronSchedule` + an optional `CaSocrataAppToken`.
7. **New schema migration** (next number, currently 195 used → use 196) — seed `opportunities.OpportunitySources` rows: `CA_SocrataSF`, `CA_SocrataSanDiego`, `CA_SanJoseCkan`, `CA_CEQAnet` (SourceType = MajorProjectsInventory, IsEnabled=1) with endpoints in BaseUrl/config.

**Constraints:**
- Reuse `StructuralRelevanceGate`, `CanonicalOrgResolver`, the Polly retry, and the upsert path UNCHANGED. `ProvinceNormalizer` already handles 'CA'.
- Honor `IngestionMaxBytesPerResponse` + a `maxRowsPerRun` cap (copy Bc).
- Match surrounding code style. No `dotnet build`/`test` (env hangs).

**Output:** the 3 new files + edits to Program.cs / ScheduledJobDefinition.cs / OpportunitiesWorkerOptions.cs + the migration. Then stop — Claude builds + smoke-tests locally.

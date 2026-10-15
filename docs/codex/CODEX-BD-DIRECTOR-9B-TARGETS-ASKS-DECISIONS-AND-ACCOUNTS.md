# Codex — BD Director 9b: the number, the accounts, and the asks-and-decisions ledger

**DO NOT BUILD. DO NOT RUN TESTS. DO NOT RUN ANY TOOL AGAINST A DATABASE. DO NOT APPLY THE
MIGRATION.** Make the edits, add the migration and the regression gates as source, and ping when
applied. Claude builds, runs the filtered test classes, applies migration 348 with sqlcmd, renders
the five tabs and reads the PNGs, and deploys the local install. Ian runs nothing.

## Why this exists

Brief 9a shipped the BD Director window (commit `dcfe00a9`) with the two panes that existing tables
could fill: people and funnel. This brief adds the three things a Director does that nothing in the
app records today: **sets a number** and reads progress against it, **asks** a named person for one
bounded thing by a date, and **writes down a decision** with the reasoning so it can be scored later.
It also adds the **accounts** pane, which reads what the firm already knows about its own clients.

Three new tables, one migration. Everything else reads stores that exist. One model call site is
not added; no email is sent; nothing runs on the Worker.

Measured on the live database and in the source on 2026-10-14 at commit `dcfe00a9`. Use these as
given; do not remeasure, do not quantify beyond them. They are also the harness fixture (Piece 7).

| Fact | Value |
|---|---|
| Live pursuits (`opportunities.CrmEngagements`, `Stage` 1 or 3) | 101 |
| Live pursuits with `ProposedFee > 0` | **0 of 101**. The live pipeline fee is $0.00 |
| Live pursuits with a `NextActionNote` / a `NextActionDueUtc` | 27 / 27 (24 overdue, 3 future) |
| App files (non-test .cs/.xaml) that display `NextActionDueUtc` or `NextActionNote` | **0**. Only `SqlCrmEngagementStore` round-trips them. An engineer cannot see a next action anywhere in the app today |
| `CrmEngagementStageHistory` rows entering Submitted (3) in the last 24 months | 0. No live pursuit is at Submitted |
| `opportunities.CanonicalOrg` with `RetiredAtUtc IS NULL` | 9,792 |
| … of which `ClendorClientId IS NOT NULL` (a Deltek client) | 569 |
| … of which `KorProjectsCount > 0` | 346 |
| … of which have a live pursuit as buyer | 77 |
| … of which `CrmBuyerEmailWarmth.LastTouchUtc` within 90 d | 21 |
| Top 15 Deltek-client orgs by `KorProjectsCount` (Id · DisplayName · Kind · count · live pursuits · last touch) | 141 Northland Properties Ltd. KorClient 378 · 0 · none; 200 Larco Investments Ltd. KorClient 185 · 0 · none; 49 Cressey Development Group KorClient 117 · 0 · none; 28 Polygon Development Ltd. KorClient 114 · 0 · none; 157 Reliance Properties Ltd. KorClient 82 · 0 · none; 69677 Bosa Development Developer 80 · 1 · 2026-10-09; 12 Wesgroup Properties Ltd. KorClient 80 · 0 · none; 207 Anthem Properties Group Ltd. KorClient 72 · 1 · 2026-10-09; 218 Bucci Developments Ltd. KorClient 66 · 0 · none; 69802 Appia Development Developer 63 · 0 · none; 20962 Mondiale Development Developer 57 · 0 · none; 69730 Solterra Development Corp. Developer 56 · 1 · 2026-09-01; 29 Intracorp Projects Ltd. KorClient 46 · 0 · none; 38943 Bosa Properties Developer 44 · 1 · 2024-11-08; 125 Ledingham McAllister Properties Ltd. KorClient 40 · 0 · none |
| `CanonicalOrg` columns the accounts pane reads | `Id bigint`, `Kind nvarchar`, `DisplayName nvarchar`, `ClendorClientId varchar`, `LastKorProjectAtUtc datetime2`, `KorProjectsCount int`, `KorProjectsCountIsRecordsNotWins bit` (migration 315: the count is Deltek records, proposals included, not wins), `RetiredAtUtc datetimeoffset` |
| Latest migration | `Kor.Opportunities.Data/Schema/347_PlainDismissalReasons.sql`. **This brief creates 348.** Migrations are applied manually (README: no runner), must be idempotent and GO-batched, and must not contain `USE`. The DDL convention is `274_CrmEnrichment.sql`: block-comment header, `SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; GO`, then `IF OBJECT_ID(N'opportunities.X', N'U') IS NULL BEGIN CREATE TABLE … END` with `GO` after each |
| 9a gate that constrains this brief | `BdDirectorRegressionTests.Store_ReadsTheSpecifiedPopulations_AndGroupsByResolvedPerson` asserts `SqlBdDirectorStore.cs` contains no `INSERT`, `UPDATE`, `DELETE` or `MERGE`. **Writes therefore go in a new file** |

Line numbers below are as of `dcfe00a9`.

## The decisions, so you build the one thing

- **Three tables, one migration, one ledger store.** `348_BdDirectorLedger.sql` creates
  `opportunities.BdTarget`, `opportunities.BdAsk`, `opportunities.BdDecision`. A new
  `IBdDirectorLedgerStore` / `SqlBdDirectorLedgerStore` (`Kor.Opportunities.Data/Crm/`) owns every
  read and write of those three tables. The 9a `IBdDirectorStore` stays SELECT-only and gains two
  reads over *existing* tables: target actuals and the accounts list.
- **A target is a number Ian sets; the actual is computed, never stored.** Only metrics the
  database can measure today are allowed (a CHECK constraint, and a C# constant list):
  `LivePursuits`, `PipelineFee`, `AccountsWithLivePursuit` (point-in-time, ignore the period) and
  `PursuitsOpened`, `BuyersTouched` (counted inside `[PeriodStartUtc, PeriodEndUtc)`). Scope is
  `Firm`, `Region` (ScopeKey = `CrmEngagements.Region`, case-insensitive) or `Person` (ScopeKey =
  the 9a person key, `COALESCE(BdStaff.Mailbox, OwnerStaffId)`). Won fee, proposals submitted and
  win rate are **not** metrics here: 0 of 101 pursuits carry a fee and nothing enters Submitted, so
  a target on them would measure nothing. The pane says so in one sentence instead.
- **An ask is one bounded thing, to one person, by a date.** `Minutes` is required and checked
  `BETWEEN 5 AND 180`; the dialog defaults to 60. When the ask is about a pursuit, the app **also**
  writes the pursuit's `NextActionNote` and `NextActionDueUtc` through the existing
  `ICrmEngagementStore.GetByIdAsync` + `UpdateAsync` (so the Director's overdue count sees it).
  Because no screen shows a next action, the ask is surfaced to the engineer on the **BD
  Dashboard** ("Asked of you", Piece 5). The ask row is the record; the next-action fields are a
  mirror.
- **A decision is immutable except its outcome.** The ledger store exposes `RecordDecisionAsync`
  and `ScoreDecisionAsync` and nothing that edits or deletes a decision. The outcome fields
  (`Outcome`, `OutcomeNote`, `ScoredAtUtc`, `ScoredBy`) are the only columns an UPDATE may set.
  This is the journal a later brief scores; do not add anything that would let a call be reworded
  after the fact.
- **Accounts read the Opportunities database; Deltek is read for one selected account.** The list
  comes from `CanonicalOrg` (Deltek clients, by `KorProjectsCount`). Selecting a row calls
  `IDeltekClientContextService.LoadAsync(ClendorClientId, ct)` for that one client, the same call
  `OrgDossierViewModel.cs` line 487 makes. Never loop it over the list.
- **Tabs.** The window becomes a `TabControl` with five tabs in this order and with these exact
  headers: **The number**, **People**, **Funnel**, **Accounts**, **Asks & decisions**. The 9a
  People and Funnel content moves into its tabs unchanged (same captions, same bindings, same
  `x:Static` references). The render harness writes one PNG per tab.
- **Plain words everywhere**, as in 9a. The 9a plain-words gate extends over the new files.
- **No new DI pattern.** Singletons for stores, transients for dialogs and view models, in
  `OpportunitiesModule.cs` beside the 9a lines (~146 and ~268).

## Pieces

### Piece 1 — migration `Kor.Opportunities.Data/Schema/348_BdDirectorLedger.sql`

Follow `274_CrmEnrichment.sql` exactly in shape. Header: what the three tables are, who writes
them (the BD Director window, through `SqlBdDirectorLedgerStore`), who reads them (the Director
window; the BD Dashboard reads open asks for the signed-in person), and "additive only, no data
moves". Then:

```sql
IF OBJECT_ID(N'opportunities.BdTarget', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.BdTarget (
        Id              BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BdTarget PRIMARY KEY CLUSTERED,
        Metric          NVARCHAR(40)   NOT NULL CONSTRAINT CK_BdTarget_Metric CHECK (Metric IN (N'LivePursuits', N'PipelineFee', N'AccountsWithLivePursuit', N'PursuitsOpened', N'BuyersTouched')),
        Scope           NVARCHAR(20)   NOT NULL CONSTRAINT CK_BdTarget_Scope CHECK (Scope IN (N'Firm', N'Region', N'Person')),
        ScopeKey        NVARCHAR(150)  NULL,
        PeriodStartUtc  DATETIMEOFFSET NOT NULL,
        PeriodEndUtc    DATETIMEOFFSET NOT NULL,
        TargetValue     DECIMAL(18,2)  NOT NULL,
        Note            NVARCHAR(500)  NULL,
        SetBy           NVARCHAR(150)  NOT NULL,
        SetAtUtc        DATETIMEOFFSET NOT NULL CONSTRAINT DF_BdTarget_SetAt DEFAULT sysdatetimeoffset(),
        RetiredAtUtc    DATETIMEOFFSET NULL,
        RetiredBy       NVARCHAR(150)  NULL,
        CONSTRAINT CK_BdTarget_Period CHECK (PeriodEndUtc > PeriodStartUtc)
    );
END
GO
```

`BdAsk`: `Id`, `ToStaffKey NVARCHAR(150) NOT NULL`, `EngagementId BIGINT NULL` (FK to
`opportunities.CrmEngagements(Id)`, no cascade), `CanonicalOrgId BIGINT NULL` (FK to
`opportunities.CanonicalOrg(Id)`), `Ask NVARCHAR(500) NOT NULL`, `WhyNow NVARCHAR(1000) NULL`,
`Minutes INT NOT NULL` with `CK_BdAsk_Minutes CHECK (Minutes BETWEEN 5 AND 180)`, `DueUtc
DATETIMEOFFSET NOT NULL`, `AskedBy NVARCHAR(150) NOT NULL`, `AskedAtUtc` default
`sysdatetimeoffset()`, `Status NVARCHAR(20) NOT NULL` default `N'Open'` with
`CK_BdAsk_Status CHECK (Status IN (N'Open', N'Done', N'Declined', N'Withdrawn'))`, `ClosedAtUtc
DATETIMEOFFSET NULL`, `ClosedBy NVARCHAR(150) NULL`, `ClosedNote NVARCHAR(500) NULL`. Index
`IX_BdAsk_ToStaffKey_Status (ToStaffKey, Status) INCLUDE (DueUtc)` in its own batch, guarded by
`IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BdAsk_ToStaffKey_Status' AND object_id = OBJECT_ID(N'opportunities.BdAsk'))`.

`BdDecision`: `Id`, `Kind NVARCHAR(20) NOT NULL` with `CK_BdDecision_Kind CHECK (Kind IN (N'Go',
N'NoGo', N'Drop', N'Reassign', N'Price', N'Target', N'Other'))`, `EngagementId BIGINT NULL` (FK),
`CanonicalOrgId BIGINT NULL` (FK), `Decision NVARCHAR(500) NOT NULL`, `Rationale NVARCHAR(2000) NOT
NULL`, `ExpectedOutcome NVARCHAR(500) NOT NULL`, `ReviewAtUtc DATETIMEOFFSET NOT NULL`, `DecidedBy
NVARCHAR(150) NOT NULL`, `DecidedAtUtc` default `sysdatetimeoffset()`, `Outcome NVARCHAR(20) NOT
NULL` default `N'Pending'` with `CK_BdDecision_Outcome CHECK (Outcome IN (N'Pending', N'Right',
N'Wrong', N'Unclear'))`, `OutcomeNote NVARCHAR(1000) NULL`, `ScoredAtUtc DATETIMEOFFSET NULL`,
`ScoredBy NVARCHAR(150) NULL`.

No `USE`. No `DROP`. No data. A second apply changes nothing.

### Piece 2 — the ledger store: `Kor.Opportunities.Data/Crm/IBdDirectorLedgerStore.cs` and `SqlBdDirectorLedgerStore.cs`

Same construction and `Microsoft.Data.SqlClient` pattern as `SqlBdDirectorStore.cs`; parameters
always typed (`SqlDbType`), never string-built. Records in the interface file:

```csharp
public sealed record BdTarget(long Id, string Metric, string Scope, string? ScopeKey,
    DateTimeOffset PeriodStartUtc, DateTimeOffset PeriodEndUtc, decimal TargetValue, string? Note,
    string SetBy, DateTimeOffset SetAtUtc);
public sealed record BdAsk(long Id, string ToStaffKey, long? EngagementId, long? CanonicalOrgId,
    string Ask, string? WhyNow, int Minutes, DateTimeOffset DueUtc, string AskedBy,
    DateTimeOffset AskedAtUtc, string Status, DateTimeOffset? ClosedAtUtc, string? ClosedBy, string? ClosedNote);
public sealed record BdDecision(long Id, string Kind, long? EngagementId, long? CanonicalOrgId,
    string Decision, string Rationale, string ExpectedOutcome, DateTimeOffset ReviewAtUtc,
    string DecidedBy, DateTimeOffset DecidedAtUtc, string Outcome, string? OutcomeNote,
    DateTimeOffset? ScoredAtUtc, string? ScoredBy);

public static class BdTargetMetrics
{
    public const string LivePursuits = "LivePursuits", PipelineFee = "PipelineFee",
        AccountsWithLivePursuit = "AccountsWithLivePursuit", PursuitsOpened = "PursuitsOpened",
        BuyersTouched = "BuyersTouched";
    public static readonly IReadOnlyList<string> All = new[] { LivePursuits, PipelineFee, AccountsWithLivePursuit, PursuitsOpened, BuyersTouched };
    public static readonly IReadOnlyList<string> PointInTime = new[] { LivePursuits, PipelineFee, AccountsWithLivePursuit };
}

public interface IBdDirectorLedgerStore
{
    Task<IReadOnlyList<BdTarget>> ListTargetsAsync(CancellationToken ct);                       // RetiredAtUtc IS NULL, newest period first
    Task<long> SetTargetAsync(BdTarget target, CancellationToken ct);                           // INSERT; returns Id
    Task RetireTargetAsync(long id, string by, CancellationToken ct);                           // UPDATE RetiredAtUtc, RetiredBy only
    Task<IReadOnlyList<BdAsk>> ListAsksAsync(string? status, CancellationToken ct);             // all people; status NULL = every status; DueUtc ascending
    Task<IReadOnlyList<BdAsk>> ListOpenAsksForAsync(string toStaffKey, CancellationToken ct);   // Status = 'Open' AND ToStaffKey = @key
    Task<long> CreateAskAsync(BdAsk ask, CancellationToken ct);                                 // INSERT; returns Id
    Task CloseAskAsync(long id, string status, string? note, string by, CancellationToken ct);  // UPDATE Status, ClosedAtUtc, ClosedBy, ClosedNote; only when Status = 'Open'
    Task<IReadOnlyList<BdDecision>> ListDecisionsAsync(CancellationToken ct);                   // DecidedAtUtc descending
    Task<long> RecordDecisionAsync(BdDecision decision, CancellationToken ct);                  // INSERT; returns Id
    Task ScoreDecisionAsync(long id, string outcome, string? note, string by, CancellationToken ct); // UPDATE Outcome, OutcomeNote, ScoredAtUtc, ScoredBy only
}
```

There is deliberately no update or delete for targets' values, asks' text or decisions' text.
Changing a target is retire-and-set. `CloseAskAsync` and `ScoreDecisionAsync` use
`SCOPE_IDENTITY()`-free plain UPDATEs with a `WHERE Id = @id AND Status = N'Open'` (asks) and
`WHERE Id = @id` (decisions). Return `Task`, not the row count.

### Piece 3 — two reads added to `IBdDirectorStore` / `SqlBdDirectorStore` (SELECT-only, as 9a's gate requires)

```csharp
public sealed record BdTargetKey(string Metric, string Scope, string? ScopeKey, DateTimeOffset PeriodStartUtc, DateTimeOffset PeriodEndUtc);
public sealed record BdTargetActuals(IReadOnlyDictionary<BdTargetKey, decimal> Actuals, int Live, int LiveWithFee);
public sealed record BdDirectorAccount(long CanonicalOrgId, string DisplayName, string Kind, string ClendorClientId,
    int KorProjectsCount, bool CountIsRecordsNotWins, DateTime? LastKorProjectAtUtc,
    int LivePursuits, DateTimeOffset? LastTouchUtc, int OpenAsks);

Task<BdTargetActuals> GetTargetActualsAsync(IReadOnlyList<BdTargetKey> keys, CancellationToken ct);
Task<IReadOnlyList<BdDirectorAccount>> GetAccountsAsync(int take, CancellationToken ct);
```

`GetTargetActualsAsync` runs **one** command per call, not one per key: a table-valued pass is not
available here, so build a single batch that returns, in this order, (a) `Live` and `LiveWithFee`
(`COUNT(*)`, `SUM(CASE WHEN ProposedFee > 0 THEN 1 ELSE 0 END)` over stages 1 and 3), then (b) one
result set per key, each a single scalar, appended to the same `SqlCommand` text with numbered
parameters (`@m0`, `@s0`, `@k0`, `@ps0`, `@pe0`, …). The scalar for each metric, over the base set
`e` = `CrmEngagements` with `Stage IN (1, 3)` and scope applied as `(@sN = N'Firm') OR (@sN =
N'Region' AND e.Region = @kN) OR (@sN = N'Person' AND COALESCE(s.Mailbox, e.OwnerStaffId) = @kN)`
with the 9a `LEFT JOIN opportunities.BdStaff s ON s.StaffKey = e.OwnerStaffId`:

| Metric | Scalar |
|---|---|
| `LivePursuits` | `COUNT(*)` |
| `PipelineFee` | `SUM(COALESCE(e.ProposedFee, 0))` |
| `AccountsWithLivePursuit` | `COUNT(DISTINCT e.BuyerCanonicalOrgId)` |
| `PursuitsOpened` | `COUNT(*)` with `e.OpenedAtUtc >= @psN AND e.OpenedAtUtc < @peN` and **no stage filter** (opened in the period, whatever happened since), `COALESCE(e.ExternalSource, N'') NOT LIKE N'Deltek.%'` |
| `BuyersTouched` | `COUNT(DISTINCT e.BuyerCanonicalOrgId)` joined to `CrmBuyerEmailWarmth w ON w.CanonicalOrgId = e.BuyerCanonicalOrgId` with `w.LastTouchUtc >= @psN AND w.LastTouchUtc < @peN` |

`GetAccountsAsync(take)`: `SELECT TOP (@take)` from `CanonicalOrg co` where `co.ClendorClientId IS
NOT NULL AND co.RetiredAtUtc IS NULL`, ordered by `ISNULL(co.KorProjectsCount, 0) DESC,
co.DisplayName`, with `LivePursuits` as a correlated count over stages 1 and 3, `LastTouchUtc` from
a `LEFT JOIN CrmBuyerEmailWarmth`, and `OpenAsks` as a correlated count over
`opportunities.BdAsk WHERE CanonicalOrgId = co.Id AND Status = N'Open'`. Read
`KorProjectsCountIsRecordsNotWins` as the bool. Read `LastKorProjectAtUtc` with `GetDateTime`
(it is `datetime2`), not `GetDateTimeOffset`.

### Piece 4 — the window: five tabs, three new panes, four small dialogs

`BdDirectorWindow.xaml` gets a `TabControl x:Name="Tabs"` in Grid row 1 in place of the
`ScrollViewer`; each `TabItem` holds a `ScrollViewer` with the pane. Keep the header
(title · live · people · as of · Refresh) and the status line. Headers exactly: **The number**,
**People**, **Funnel**, **Accounts**, **Asks & decisions**. Keep the 9a `Window.Resources` (the
`BdDay` converter declaration is gated).

**The number** (tab 0). Caption constant `NumberCaption` =
`Targets you set, against what the records show today. Won fee and proposals submitted are not offered because no live pursuit carries a fee and none has reached Submitted.`
A DataGrid of `BdDirectorTargetRow(BdTarget Target, decimal Actual)` with columns What
(`MetricDisplay`: `LivePursuits`→"Live pursuits", `PipelineFee`→"Pipeline fee",
`AccountsWithLivePursuit`→"Accounts with a live pursuit", `PursuitsOpened`→"Pursuits opened",
`BuyersTouched`→"Buyers touched"), Scope (`ScopeDisplay`: "Firm", "Region · {key}", "Person ·
{display name from the People rows when the key matches, else the key}"), Period (`BdDay` start –
`BdDay` end), Target, Actual, Progress (`Actual / TargetValue` as `P0`, "—" when the target is 0),
Set by. Below it one line: `{Live} live pursuits; {LiveWithFee} carry a fee.` (from
`BdTargetActuals`). Buttons: **Set a target…** (opens `TargetDialog`), **Retire** (on the selected
row; calls `RetireTargetAsync`, then reloads targets only). Fee values use `{0:C0}`.

**Accounts** (tab 3). Caption constant `AccountsCaption` =
`The firm's Deltek clients, most KOR projects first. Counts are Deltek records, proposals included, unless the row says wins. Select an account to read its Deltek summary.`
A DataGrid of `BdDirectorAccountRow` (Account, Kind, KOR projects (with " records" appended when
`CountIsRecordsNotWins`), Last KOR project (`BdDay` of the `DateTime`, "—" when null), Live
pursuits, Last touch (`BdDay`, "never"), Open asks). `SelectedAccount` drives a right-hand panel
bound to `SelectedAccountDeltek` (a `DeltekClientIntelligence?`): "Lifetime fee {LifetimeFee:C0} ·
{ProjectCount} projects · latest {LatestProjectName}, {LatestProjectStart:BdDay}" and, when `Ar` is
not null, "AR outstanding {Ar.TotalOutstanding:C0}, {Ar.Outstanding90Plus:C0} over 90 days,
{Ar.OpenInvoiceCount} open invoices"; "Billed figures posted through {SummaryPostedThrough:BdDay}"
when present; the sentence `No Deltek record could be read for this client.` when `LoadAsync`
returns null; `Some Deltek sections could not be read.` when `HasDegradedSections`. Load it in the
`SelectedAccount` setter through a cancellable per-selection token (cancel the previous load when
the selection changes); never on `LoadAsync` of the window and never for more than the selected
row. Buttons on the panel: **Open dossier** (`new OrgDossierWindow(AppServices.Get<OrgDossierViewModel>(), CanonicalOrgId) { Owner = this }.Show()`,
the route `RelationshipsView.xaml.cs` line 61–62 uses), **Ask…** (opens `AskDialog` with
`CanonicalOrgId` preset), **Decide…** (opens `DecisionDialog` with `CanonicalOrgId` preset).

**Asks & decisions** (tab 4). Two stacked grids. Asks: caption constant `AsksCaption` =
`One bounded thing, for one person, by a date. Done and declined asks stay on the record.`
Columns: To (display name resolved from the People rows by owner key, else the key), Ask, About
(pursuit project name via the 9a `_allPursuits` when `EngagementId` matches, else the account name
from the accounts list, else "—"), Minutes, Due (`BdDay`), Status, Closed note. Filter ComboBox:
Open (default) / Done / Declined / Withdrawn / All. Buttons: **Ask…** (opens `AskDialog` with no
preset; the dialog lists pursuits? no — a free ask needs no context), **Withdraw** (selected Open
ask → `CloseAskAsync(id, "Withdrawn", null, actor)`). Decisions: caption constant
`DecisionsCaption` = `What was decided, why, and what was expected. A decision is never edited; it is scored Right, Wrong or Unclear when its review date comes.`
Columns: Decided (`BdDay`), Kind, Decision, Rationale, Expected, Review (`BdDay`), Outcome,
Scored (note). Buttons: **Decide…** (`DecisionDialog`, no preset), **Right / Wrong / Unclear** (on
the selected Pending decision: open `NoteDialog` for the note, then `ScoreDecisionAsync`).

In the **People** tab's drill grid add one button per pursuit row, **Ask…**, which opens
`AskDialog` with `EngagementId` and the pursuit's owner key preset as the person.

**Dialogs** (`Kor.Operations.App/BusinessDevelopment/Director/`, each `.xaml` + `.xaml.cs`,
modelled on `ReassignDialog`): 
- `TargetDialog`: Metric (ComboBox over `BdTargetMetrics.All` with the display names above),
  Scope (Firm/Region/Person), Scope key (TextBox; enabled unless Firm; for Person it is the
  mailbox, pre-filled from a ComboBox of `PersonOption` when the user picks one), Period start and
  end (`DatePicker`s defaulting to the current calendar quarter, interpreted as local midnight →
  UTC), Target value (decimal), Note. Exposes `BdTarget Result`.
- `AskDialog(IReadOnlyList<PersonOption> people, long? engagementId, long? canonicalOrgId, string? presetPersonKey)`:
  To (ComboBox of `PersonOption`, matched on `Email` when a preset is given), Ask (500), Why now
  (1000, optional), Minutes (default 60, numeric, 5–180), Due (`DatePicker`, default today + 7).
  Exposes `BdAsk Result` with `ToStaffKey = selected.Email`.
- `DecisionDialog(long? engagementId, long? canonicalOrgId)`: Kind (ComboBox of the seven kinds),
  Decision (500), Rationale (2000), Expected outcome (500), Review date (default today + 90).
  Exposes `BdDecision Result`.
- `NoteDialog(string title, string prompt)`: one multi-line TextBox, OK/Cancel; `string? Note`.

The actor for every write is the 9a `_actor` resolved once in `Window_Loaded`.

### Piece 5 — the BD Dashboard shows the signed-in person their open asks

`DashboardViewModel.cs` (ctor line 52): add `IBdDirectorLedgerStore? ledger = null` as the last
optional parameter, and `ObservableCollection<BdAskRow> AskedOfMe`, `bool HasAsks`, and
`LoadAsksAsync(string actor, CancellationToken ct)` which calls `ListOpenAsksForAsync(actor)` and
fills the rows (Ask, Why now, Minutes, Due `BdDay`, Asked by, Id). `CloseAskAsync(long id, string status, string? note, string actor)`
forwards to the ledger and removes the row. `DashboardView.xaml`: a section **Asked of you** above
Priority Actions, collapsed when `HasAsks` is false, one row per ask with **Done** and **Can't**
buttons; both open `NoteDialog` (optional note), then call the view model with `"Done"` or
`"Declined"`. `DashboardView.xaml.cs` already resolves the actor at line 264; call `LoadAsksAsync`
with it wherever the dashboard loads its other sections. `BdDay` is declared in
`DashboardView.xaml` line 21; use it. No new inline date formats (the ratchet).

### Piece 6 — wiring

`OpportunitiesModule.cs`: `services.AddSingleton<IBdDirectorLedgerStore>(_ => new SqlBdDirectorLedgerStore(options.OpportunitiesDb));`
beside line ~268; `AddTransient` for the four dialogs is unnecessary (they are constructed with
arguments); `BdDirectorViewModel` gains `IBdDirectorLedgerStore ledger` and
`IDeltekClientContextService deltek` constructor parameters (both already registered). Update the
9a test fixture `Fixture.Create()` and the 9a `RenderDirector` to pass mocks for both.

### Piece 7 — harness and gates

**Render.** `BdScreensRenderTests.RenderDirector` renders each tab: set `Tabs.SelectedIndex = i`,
`UpdateLayout()`, then `RenderView` to `director-number.png`, `director-people.png`,
`director-funnel.png`, `director-accounts.png`, `director-asks.png`. Retire `director.png`. The
minimum render count becomes **10**. The 9a people/funnel fixture is unchanged. New fixture rows,
from the table above: the 15 accounts verbatim (`Id`, name, kind, count, live pursuits, last touch;
`CountIsRecordsNotWins` true for every row, `LastKorProjectAtUtc` and `OpenAsks` are layout
examples); `BdTargetActuals(Live: 101, LiveWithFee: 0)` with two targets whose actuals are the
measured values: `LivePursuits` Firm 101 (target 120) and `AccountsWithLivePursuit` Firm 77 (target
100); mark any further target rows illustrative. Three asks and two decisions as layout examples,
labelled illustrative in a comment as 9a did. The Deltek mock returns a `DeltekClientIntelligence`
with `LifetimeFee`, `ProjectCount`, `Ar` filled for the first account so the panel renders.
Assert, as 9a does, that the fixture sums match the measured totals (15 accounts, 4 with a live
pursuit, 3 touched within 90 days of 2026-10-14).

**New class `BdDirectorLedgerRegressionTests.cs`** beside the 9a class. Rule 11 summary: what it
covers, what it does not, and one same-class miss (e.g. a CHECK constraint the gate reads as text
but that SQL Server would reject at apply time). Gates, each with an EXPECTED/ACTUAL message:

1. `Migration348_IsAdditiveIdempotentAndBatched`: the file exists; contains the three
   `IF OBJECT_ID(N'opportunities.Bd…', N'U') IS NULL` guards; a `GO` follows each `END`; no `USE `,
   no `DROP`, no `INSERT`, no `UPDATE`, no `DELETE`; the five metric names, the four ask statuses,
   the seven kinds and the four outcomes each appear in a CHECK; `Minutes BETWEEN 5 AND 180`.
2. `LedgerStore_WritesOnlyItsThreeTables_AndNeverDeletes`: every `INSERT INTO` and `UPDATE` in
   `SqlBdDirectorLedgerStore.cs` targets `opportunities.BdTarget`, `BdAsk` or `BdDecision`; no
   `DELETE`; the decision UPDATE's SET list names only `Outcome`, `OutcomeNote`, `ScoredAtUtc`,
   `ScoredBy`; the ask UPDATE's `WHERE` carries `Status = N'Open'`; the target UPDATE's SET list
   names only `RetiredAtUtc`, `RetiredBy`.
3. `ReadStore_StaysSelectOnly_AndReadsTheAccountPopulation`: `SqlBdDirectorStore.cs` still has no
   write keyword; contains `ClendorClientId IS NOT NULL`, `RetiredAtUtc IS NULL`,
   `KorProjectsCountIsRecordsNotWins`, `Status = N'Open'`; `GetDateTime(` is used for
   `LastKorProjectAtUtc` (assert the SQL column list order you chose and the matching reader call).
4. `Decisions_AreImmutableExceptTheirOutcome`: by reflection, `IBdDirectorLedgerStore` has no
   method whose name starts with `Update`, `Edit`, `Delete` or `Remove`; it has `ScoreDecisionAsync`.
5. `Ask_OnAPursuit_MirrorsToTheNextAction`: view-model test with mocks; creating an ask with
   `EngagementId = 1` calls `CreateAskAsync` once and `ICrmEngagementStore.UpdateAsync` once with
   `NextActionNote == ask.Ask` and `NextActionDueUtc == ask.DueUtc` and the actor; with no
   `EngagementId`, `UpdateAsync` is never called.
6. `Targets_ActualsComeFromTheReadStore`: the view model asks `GetTargetActualsAsync` with one key
   per listed target and shows the returned actual; a metric outside `BdTargetMetrics.All` in a
   stored row renders as "Unknown metric" rather than throwing.
7. `Accounts_ReadDeltekForTheSelectedAccountOnly`: after `LoadAsync`, the Deltek mock was never
   called; selecting the first account calls `LoadAsync` once with its `ClendorClientId`; selecting
   another calls it once more; a null return produces the "No Deltek record" sentence.
8. `Dashboard_ShowsOpenAsksForTheSignedInPerson`: `DashboardViewModel` with a ledger mock:
   `LoadAsksAsync("ilalonde@korstructural.com")` calls `ListOpenAsksForAsync` with that key and
   fills `AskedOfMe`; `CloseAskAsync` forwards `"Done"` with the note and actor and removes the row;
   with `ledger == null` the collection stays empty and nothing throws.
9. `Window_HasTheFiveTabs_InOrder_AndPlainWords`: the XAML has exactly five `TabItem`s with the
   headers above in order; the 9a plain-words regex passes over every `.xaml` and `.cs` in the
   Director folder, including the dialogs; all five caption constants are referenced by `x:Static`.
10. `Director_StillContributesZeroInlineDates`: the 9a ratchet gate extended to the dialogs and
    the dashboard edit: zero `yyyy-MM-dd` / `MMM d, yyyy` literals added in the Director folder,
    and `DashboardView.xaml`'s count is unchanged from its value at `dcfe00a9` (read it, state it).

`HomeTileVisibilityStateTests` and the 9a class need no change beyond the fixture constructor.

## What is out of scope (next briefs)

The market pane, the Director brief on the MCP server, the scoring loop that reads `BdDecision`,
the "Mine" mode call sheet, two-seat handoff, any model call, any email, any Worker job, the
Submitted-stage question (why nothing reaches it), and fee entry on pursuits.

## Read bound

Read these and nothing else, in this order (about 215 KB):

- `docs/codex/CODEX-BD-DIRECTOR-9A-THE-WINDOW-AND-TWO-PANES.md` (what 9a decided) and its
  `-RESPONSE.md`
- `Kor.Operations.App/BusinessDevelopment/Director/` — all four 9a files (27.5 KB)
- `Kor.Opportunities.Data/Crm/IBdDirectorStore.cs`, `SqlBdDirectorStore.cs` (9.5 KB)
- `Kor.Opportunities.Data/Crm/SqlPursuitOverwatchStore.cs` (11.8 KB; the typed-parameter write pattern)
- `Kor.Opportunities.Data/Crm/ICrmEngagementStore.cs` (1 KB), `Kor.Opportunities.Core/Models/CrmEngagement.cs` (5.7 KB; `NextActionDueUtc` line 105, `NextActionNote` line 107; it is a `record`, use `with`)
- `Kor.Operations.App/BusinessDevelopment/Workspace/ReassignDialog.xaml` + `.xaml.cs` (5.4 KB; `PersonOption(string Display, string Email)` at line 10), `StaffDirectory.cs` (1.6 KB)
- `Kor.Operations.App/Crm/DeltekClientContextService.cs` lines 1–117 only (the records and the interface)
- `Kor.Operations.App/Opportunities/OrgDossierViewModel.cs` lines 480–495 only (the one `LoadAsync` call)
- `Kor.Operations.App/BusinessDevelopment/Workspace/RelationshipsView.xaml.cs` lines 52–70 only (`OrgDossierWindow` construction)
- `Kor.Operations.App/BusinessDevelopment/Workspace/DashboardViewModel.cs` (12.6 KB), `DashboardView.xaml` (26 KB), `DashboardView.xaml.cs` (12.9 KB)
- `Kor.Operations.App/CompositionModules/OpportunitiesModule.cs` lines 140–150 and 260–270
- `Kor.Opportunities.Data/Schema/274_CrmEnrichment.sql` (2.5 KB) and `Schema/README.md`
- `Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdDirectorRegressionTests.cs` (16.6 KB), `BdScreensRenderTests.cs` (27 KB), `BdPresentationRegressionTests.cs` lines 240–270

Remember: App projects with `UseWPF` do **not** get implicit `System.IO`; add `using System.IO;`
where a file touches `Path` or `File`.

## Files

Creates (13): `348_BdDirectorLedger.sql`; `IBdDirectorLedgerStore.cs`; `SqlBdDirectorLedgerStore.cs`;
`TargetDialog.xaml` + `.cs`; `AskDialog.xaml` + `.cs`; `DecisionDialog.xaml` + `.cs`;
`NoteDialog.xaml` + `.cs`; `BdDirectorLedgerRegressionTests.cs`;
`docs/codex/CODEX-BD-DIRECTOR-9B-TARGETS-ASKS-DECISIONS-AND-ACCOUNTS-RESPONSE.md`.

Edits (10): `IBdDirectorStore.cs`, `SqlBdDirectorStore.cs`, `BdDirectorWindow.xaml` + `.cs`,
`BdDirectorViewModel.cs`, `BdDirectorPersonRow.cs` (new row types), `DashboardViewModel.cs`,
`DashboardView.xaml` + `.cs`, `OpportunitiesModule.cs`, `BdDirectorRegressionTests.cs` (fixture),
`BdScreensRenderTests.cs`.

## Response

Write the response file in the 9a format: applied changes by piece, the gate table ("authored, not
executed"), fixture discrepancies, the files list. State plainly that nothing was built, run,
rendered or applied, and that the migration is unapplied. `git diff --check` must be clean. Do not
commit.

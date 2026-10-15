TASK A - GIVE THE APP A READ-ONLY CONNECTION TO KorStandards (no UI, no writes)

GOAL
The Standard Details module today talks to exactly one database, KorTransmittals. Task A adds a
SECOND, READ-ONLY data source: KorStandards, so later tasks can list the 612 KOR-D details and
their confidence. This task adds the connection + a small read repository and NOTHING ELSE - no
window changes, no buttons, no writes. It is the foundation the Link-Detail picker (Task B) and
the registers (Task E) build on.

Do NOT build, run tests, execute SQL, or change git state. Claude verifies after you finish.

WHAT EXISTS (read these to match the pattern exactly - do not invent a new style)
- Connection strings are surfaced through four places, in this exact chain:
  1. App.config  <connectionStrings>  (Kor.Operations.App/App.config, ~line 158) - currently
     KorTransmittalsDb, KorEmailIndex, KorOpportunitiesDb.
  2. Kor.Operations.App/Services/AppConfigKeys.cs -> nested `ConnectionStrings` class (~line 53).
  3. Kor.Operations.App/Options/AppOptions.cs -> `DatabaseOptions` (line 13).
  4. Kor.Operations.App/CompositionModules/CompositionHelpers.cs -> `GetDatabaseOptions()` (line 51),
     which reads via `GetRequiredConnectionString(...)` / ConfigurationManager.
- The repository style to COPY: Kor.Operations.App/StandardDetails/StandardDetailsRepository.cs -
  `internal sealed class`, `internal sealed record` row types at the top, plain
  Microsoft.Data.SqlClient, `_connectionString` ctor arg, `CommandTimeout = SqlTimeouts.UiFacing`,
  `await using` connections, and the `AddParam` / `AddNVarChar` / reader-extension helpers it uses
  (find where those helpers live and reuse them; do not duplicate them).
- The KorStandards read surface (already granted to the login below - SELECT on these two only):
  * detail.vw_PaletteCatalog - columns: DetailNumber, Title, Discipline, Confidence,
    VariantsDiverge, ViewName, ViewKind, SizeToken, IsPlaceable. 1,079 rows, 612 distinct
    DetailNumber, all Confidence='unverified' / IsPlaceable=0 right now.
  * detail.vw_DetailPlaceable - the currently-placeable rows (0 today by design).

DO THIS

1. App.config: add a new connection string named KorStandardsDb:
   Server=KOR-APP01\SQLEXPRESS;Database=KorStandards;User Id=standards_reader;
   Password=KorStandardsRead!2026;Encrypt=True;TrustServerCertificate=True;
   (This login is READ-ONLY on the two views above. It is the app's only KorStandards access.)

2. AppConfigKeys.ConnectionStrings: add `public const string KorStandardsDb = "KorStandardsDb";`.

3. DatabaseOptions (AppOptions.cs): add `public string KorStandardsDb { get; init; } = "";`.

4. CompositionHelpers.GetDatabaseOptions(): bind KorStandardsDb from the new connection string.
   ⚠ It must be OPTIONAL, not required: if the connection string is absent, leave it "" and do
   NOT throw - the module must still open for KorTransmittals-only. (Use the nullable
   ConfigurationManager path like the `KorTransmittals` line does, NOT GetRequiredConnectionString.)

5. New file Kor.Operations.App/StandardDetails/KorStandardsReadRepository.cs:
   - namespace Kor.Operations.StandardDetails, internal sealed class KorStandardsReadRepository,
     ctor takes the connection string, same SqlClient style as StandardDetailsRepository.
   - A record: internal sealed record PaletteDetailRow(string DetailNumber, string Title,
     string Discipline, string Confidence, bool IsPlaceable, int VariantCount).
   - Method `Task<IReadOnlyList<PaletteDetailRow>> LoadPaletteDetailsAsync(string query)` that
     returns ONE ROW PER DISTINCT DetailNumber from detail.vw_PaletteCatalog:
        SELECT DetailNumber, MIN(Title), MIN(Discipline),
               MIN(Confidence), MAX(CAST(IsPlaceable AS int)), COUNT(*) AS VariantCount
        FROM detail.vw_PaletteCatalog
        WHERE (@q = '' OR DetailNumber LIKE @like OR Title LIKE @like)
        GROUP BY DetailNumber ORDER BY DetailNumber;
     (parameterised @q/@like exactly like LoadDocumentsAsync does; MAX(IsPlaceable)=1 means at
     least one placeable variant.)
   - A method `Task<int> CountDistinctDetailsAsync()` returning
     COUNT(DISTINCT DetailNumber) from the view - used only as a health smoke.
   - Read-only: no INSERT/UPDATE/DELETE anywhere in this class.

CONSTRAINTS
- No XAML, no window/code-behind changes, no new buttons, no DI wiring beyond steps 1-4.
- Do not touch StandardDetailsRepository, the workflow, or any KorTransmittals code path.
- Do not reference DocumentVariants/DetailNumber-linkage yet - that is Task B.
- Match the existing helper methods; do not add a second SqlClient style or a new base class.

END your reply with an adversarial self-review: confirm (a) the app still starts if KorStandardsDb
is missing from config, (b) nothing in the new class can write, (c) the connection string is the
only new secret and it is the read-only login, and (d) exact file+line of each of the 5 edits.

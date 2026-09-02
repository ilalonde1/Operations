TASK B - LINK A DOCUMENT RECORD TO A KOR-D DETAIL (picker + write + show on row)

GOAL
Let a user link a Standard Details document record to exactly one KorStandards detail
(KOR-D-#####). A new picker dialog lists the 612 details (from Task A's read repository); choosing
one writes it into dbo.Documents.DetailNumber (the column migration 002 already added); the linked
number then shows on the document row. This is the first visible half of the governance bridge.

Do NOT build, run tests, execute SQL, or change git state. Claude verifies after you finish.

WHAT EXISTS (read and match - do not invent new styles)
- Task A repository (READ side, already merged): Kor.Operations.App/StandardDetails/
  KorStandardsReadRepository.cs - `LoadPaletteDetailsAsync(string query)` returns
  `PaletteDetailRow(DetailNumber, Title, Discipline, Confidence, IsPlaceable, VariantCount)`,
  one row per distinct detail. Use it to populate the picker. Its connection comes from
  DatabaseOptions.KorStandardsDb (may be "" - handle that, see below).
- Dialog pattern to COPY exactly: Kor.Operations.App/StandardDetails/CreateStandardDocumentWindow
  .xaml(.cs) - a modal Window, Width ~560, WindowStartupLocation CenterOwner, Border+Card style,
  DialogHero gradient header, a body, and a footer with Cancel + primary button; sets
  `DialogResult = true`, exposes the result via public properties. Match this look and structure.
- The main window: Kor.Operations.App/StandardDetails/StandardDetailsWindow.xaml(.cs) +
  .Logic.cs. Note:
  * `_repo` (StandardDetailsRepository, KorTransmittals) is created in
    StandardDetailsWindow_Loaded (xaml.cs:46). Create a NEW field `_korStandardsRepo`
    (KorStandardsReadRepository) there too, ONLY when DatabaseOptions.KorStandardsDb is non-empty;
    leave it null otherwise.
  * The documents grid binds to the private `DocumentRow` class (Logic.cs:490) and is loaded by
    `LoadDocumentsUiAsync` (Logic.cs:187) from `StandardDetailsDocumentRow` (Repository.cs:14).
  * Buttons live in the XAML with Click handlers in Logic.cs; enablement is centralised in
    `UpdateActionStates` (xaml.cs:119). Add the Link button's enablement THERE, next to
    UploadVersionButton (contributor + a document selected + `_korStandardsRepo != null`).
  * `EnsureCanContribute("...")` is the permission gate used by CreateDocument/Upload.

DO THIS

1. StandardDetailsRepository.cs (KorTransmittals side):
   - Add `DetailNumber` (nvarchar, nullable) to the `StandardDetailsDocumentRow` record and to
     the SELECT list in BOTH queries inside LoadDocumentsAsync (groupedSql and fallbackSql):
     add `d.DetailNumber` and read it (IsDBNull -> null).
   - Add `internal async Task SetDocumentDetailNumberAsync(long documentId, string? detailNumber,
     Guid actorUserId)`:
        UPDATE dbo.Documents
        SET DetailNumber=@detail, UpdatedByUserId=@uid, UpdatedUtc=SYSUTCDATETIME()
        WHERE DocumentId=@id;
     @detail is NULL when clearing the link. Pass DBNull for null. Match existing AddParam usage.
   - The filtered unique index UX_Documents_DetailNumber makes a duplicate link throw
     SqlException (2601/2627). Do NOT swallow it in the repo - let it surface so the UI can show a
     friendly collision message (step 4).

2. New picker: Kor.Operations.App/StandardDetails/LinkDetailWindow.xaml + .xaml.cs
   - Modal Window styled exactly like CreateStandardDocumentWindow (hero header
     "Link Standard Detail", subtitle "Choose the KOR-D detail this record is the issued document
     for.").
   - A search TextBox at top; a read-only DataGrid below bound to PaletteDetailRow with columns:
     Detail # (DetailNumber), Title, Discipline, Confidence, and Placeable (Yes/No from
     IsPlaceable). AutoGenerateColumns=False, IsReadOnly=True, single-row select.
   - Constructor takes a KorStandardsReadRepository; on load call LoadPaletteDetailsAsync("") to
     fill the grid; the search box re-queries via LoadPaletteDetailsAsync(searchText) (a Search
     button is fine - no need for live-as-you-type).
   - Footer: Cancel + "Link Selected". Link requires a selected row; expose
     `public string? SelectedDetailNumber { get; private set; }` and set DialogResult=true.
   - Also expose a way to CLEAR: a secondary "Remove Link" button that sets
     SelectedDetailNumber=null, a separate `public bool ClearRequested { get; private set; }=true`,
     and DialogResult=true - so the caller can distinguish "link to X" from "clear the link".

3. StandardDetailsWindow.xaml: add a "Link Detail" button in the record-actions area next to the
   New Record / Add Revision buttons (ActionButton style). Add a "Detail #" column to
   DocumentsGrid bound to DocumentRow.DetailNumber (show blank when null).

4. StandardDetailsWindow.Logic.cs:
   - Add `DetailNumber` to the private `DocumentRow` class and map it in LoadDocumentsUiAsync.
   - Add `Link_Click`: guard EnsureCanContribute("link details"); require a selected DocumentRow;
     require `_korStandardsRepo != null` (else info message "KorStandards catalog is not
     configured"); open LinkDetailWindow; on DialogResult true call
     `_repo.SetDocumentDetailNumberAsync(doc.DocumentId, dlg.ClearRequested ? null :
     dlg.SelectedDetailNumber, _actorUserId)` inside try/catch; on SqlException whose Number is
     2601 or 2627 show "That detail (KOR-D-#####) is already linked to another record." as a
     Warning banner + MessageBox and do not crash; on success reload documents and show a success
     banner. Reuse the SetActivityMessage / MessageBox pattern the other handlers use.

5. StandardDetailsWindow.xaml.cs UpdateActionStates: add
   `LinkDetailButton.IsEnabled = canContribute && selectedDoc is not null && _korStandardsRepo is
   not null;`

CONSTRAINTS
- Only the StandardDetails module changes. Do not touch the workflow (submit/approve/publish),
  watermarking, KorTransmittals group code, or KorStandardsReadRepository.
- No promotion, no confidence writes, no variants - those are later tasks. Task B only records
  Documents.DetailNumber and displays it.
- The KorStandards side is READ-only; the only WRITE is Documents.DetailNumber in KorTransmittals.
- Keep the picker read-only over the catalog. Match existing naming, spacing, and helpers.

END your reply with an adversarial self-review: (a) what happens when KorStandardsDb is not
configured (Link button disabled, no crash); (b) the exact collision path and that the message is
friendly, not a stack trace; (c) that clearing a link (Remove Link) writes NULL and is
distinguishable from linking; (d) that a duplicate link cannot silently overwrite another record's
link; (e) file+line of every edit.

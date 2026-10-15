DEEP ADVERSARIAL AUDIT v2 - THE WHOLE STANDARD DETAILS GOVERNANCE MODULE

You are a hostile reviewer. Break this. The bridge (Tasks A-D) was audited once already and six
findings were fixed; since then three more tasks landed (E registers, F publish watermark, G
variants) plus the fixes. Audit EVERYTHING now, with the NEW work as the primary target. Read the
real code and live behaviour - do not trust these notes or prior "verified" claims. READ-ONLY: no
build, run, SQL execution, or edits. Report findings ranked by severity with file:line evidence,
CONFIRMED vs PLAUSIBLE, then a one-line verdict: SHIP / SHIP WITH FIXES (list) / DO NOT SHIP.

SURFACE
App module: Kor.Operations.App/StandardDetails/ (KorStandardsReadRepository, KorStandardsPromoterRepository,
StandardDetailsRepository, LinkDetailWindow, PromotionQueueWindow, RegistersWindow,
StandardDetailsFileStore, StatusWatermarkRenderer, StandardDetailsWindow.xaml/.xaml.cs/.Logic.cs).
Config chain: App.config, AppConfigKeys.cs, Options/AppOptions.cs, CompositionModules/CompositionHelpers.cs.
SQL (Operations): db/KorTransmittals/001-006*.sql. SQL (KOR.Drafter repo): db/066 (proc + promoter),
db/067 (component view).

PRIMARY TARGETS - THE NEW WORK

G. VARIANTS (highest risk - core write paths changed).
  - CreateDocumentAsync now wraps doc insert + DEFAULT variant insert in a transaction and returns 1.
    Prove atomicity, prove SCOPE_IDENTITY reads the DOCUMENT id (not the variant), and that a failure
    rolls back both. Any caller that depended on the old ExecuteNonQuery return value?
  - ResolveDefaultVariantIdAsync (resolve-or-create) inside UploadVersionAsync's serializable
    transaction: can two concurrent uploads create TWO DEFAULT variants for one document (race on the
    IF @vid IS NULL INSERT)? Does the UNIQUE index UX_DocumentVariants_Document_VariantKey prevent it,
    and if so is the failure handled or does it surface raw? Is the UPDLOCK/HOLDLOCK enough?
  - Version numbering moved to per-variant MAX; PublishAsync current-official moved to per-variant.
    Prove one-official-per-variant still holds, and that numbering cannot collide under the unique index.
  - Migration 006 flips DocumentVariantId NOT NULL, drops the filtered indexes, creates full unique
    ones, drops the doc-scoped indexes. DEPLOY ORDER: it must run AFTER the app writes variants.
    Prove the NULL-guard actually refuses a bad order; prove it is re-runnable; prove dropping
    IX_DocumentVersions_DocumentId_VersionNumber and UX_..._OneCurrentOfficialPerDocument breaks no
    query or assumption. Is there any window where the app inserts a NULL variant (legacy path,
    a version insert that bypasses UploadVersionAsync)?

E. REGISTERS.
  - RegistersWindow joins KorStandards details (standards_reader) with KorTransmittals linkage
    (transmittals_app) IN MEMORY. Prove the join key (DetailNumber) cannot double-count or mismatch on
    case; prove a detail linked by a since-deleted document does not crash; prove exceptions from either
    repo are contained (the window must not throw on load).
  - detail.vw_ComponentRegister (db/067): the correlated subqueries for InstanceCount / UsedInDetails -
    correct against detail.DetailComponentUsage? Any way they double count or NULL out? Is the
    standards_reader grant correct and does it over- or under-expose?

F. PUBLISH WATERMARK.
  - OpenVersionFile now stamps "TYPICAL <detailNumber>" for status==4 AND a non-empty detailNumber.
    Prove a NON-published linked version still gets its working stamp, an unlinked published version
    opens unstamped (as before), and the detailNumber comes from the SELECTED document not a stale one.
    Any injection/format risk from detailNumber in the watermark text or temp filename?

REGRESSION - confirm the six earlier fixes did NOT regress
  1 DecideAsync wrapped in the approve handler. 2 DeleteRecordAsync clears outbox+variants in FK-safe
  order (now that CreateDocument ALSO makes a variant, does delete still remove it?). 3 mark-done/failed
  require Status=0. 4 Retry Failed resets Status=2->0. 5 picker warns on divergent. 6 promoter assertion
  checks role membership.

ALSO STILL IN SCOPE
  - The promotion loop atomicity and the security boundary (standards_promoter EXECUTE-only; app never
    writes a KorStandards table directly - re-confirm across the NEW code too).
  - Graceful degradation: module opens and approve/upload/publish still work if KorStandardsDb and/or
    KorStandardsPromoterDb are absent. Registers button + promotion simply unavailable, no crash.
  - Dossier claims vs code: Desktop/KOR-StandardDetails-Governance-Module-2026-09-02-web.pdf. Flag any
    overstatement.

RULES
- Read-only. file:line for every finding. Rank by severity. CONFIRMED vs PLAUSIBLE. Do not pad - if a
  thing is sound, say so. End with the one-line verdict.

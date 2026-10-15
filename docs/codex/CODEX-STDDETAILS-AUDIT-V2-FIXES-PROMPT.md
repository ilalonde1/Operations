TASK - APPLY THE THREE CODE/SQL FINDINGS FROM THE v2 AUDIT

From the v2 adversarial audit. Fix exactly these; minimal and additive; match existing style. Do
NOT build, run, execute SQL, or touch git - Claude verifies after. (Audit finding #1 credential
posture is deliberately out of scope; finding #5 dossier text is handled separately.)

FIX A (audit #2) - the promoter permission assertion misses schema/database-scope grants.
File: C:\VIsual Studio Projects\KOR.Drafter\db\066_PromoteDetailProcAndPromoter.sql. Today it checks
class=1 (object) grants and role membership, but a GRANT SELECT ON SCHEMA::detail (class=3) or a
database-scope grant (class=0, e.g. SELECT/CONTROL) to standards_promoter would pass. Replace the
object-only "other permissions" check with a WHITELIST across ALL classes: standards_promoter may
hold EXACTLY two permissions -
   (a) EXECUTE on detail.PromoteDetail  (class=1, major_id = OBJECT_ID('detail.PromoteDetail','P'), permission_name='EXECUTE'), and
   (b) database-scope CONNECT           (class=0, permission_name='CONNECT').
Any other row in sys.database_permissions for that grantee (any class, including 0 non-CONNECT and 3
schema) must FAIL with a RAISERROR that first SELECTs the offending rows (class_desc,
permission_name, state_desc, and OBJECT/SCHEMA name where resolvable). Keep the existing
role-membership check (roles other than public). Keep it re-runnable. The final "EXECUTE only"
confirmation row stays.

FIX B (audit #3) - the version list is document-scoped and would mislead once a document has more
than one variant. Make the version display variant-explicit so it is correct regardless of variant
count (there is no variant selector yet; every document currently has one DEFAULT variant).
  - StandardDetailsRepository.cs, StandardDetailsVersionRow: add `long DocumentVariantId` and
    `string VariantKey`.
  - LoadVersionsAsync: join dbo.DocumentVariants to fetch DocumentVariantId + VariantKey per version;
    ORDER BY VariantKey, VersionNumber DESC (so versions group by variant, newest first within each).
  - StandardDetailsWindow.Logic.cs: add DocumentVariantId + VariantKey to the private VersionRow and
    map them in LoadVersionsUiAsync.
  - StandardDetailsWindow.xaml: add a "Variant" column to the versions DataGrid bound to VariantKey
    (place it before Version). Keep it compact.
  - The document-summary logic (ApplyVersionSummaryToSelectedDocument) may stay as-is for the
    single-variant case, BUT change its "current official" pick to be explicit: if more than one
    distinct DocumentVariantId is present in the loaded versions, show "(per variant)" instead of a
    single version label, so it never claims one official across variants. Latest-status can remain
    the newest row.

FIX C (audit #4) - migration 006's header overstates its deploy-order guard.
File: C:\VIsual Studio Projects\Operations\db\KorTransmittals\006_VariantScopeTighten.sql. The guard
only refuses when a current DocumentVersion has a NULL DocumentVariantId; with zero rows (or after
002 backfilled), it passes even if the variant-aware app is not deployed. Do NOT claim more than it
does. Update the header comment to state accurately: this script cannot detect whether the Task G
app code is deployed; its guard only refuses pre-existing NULL-variant rows. Note that if 006 runs
before the variant-aware upload code, the NOT NULL column will simply REJECT inserts from the old
upload path (a safe, loud failure - not corruption), and the operator is responsible for running
006 after deploying the Task G code. Keep the existing NULL-variant guard. (Wording only in this
file; no logic change required.)

CONSTRAINTS
- Only the three files above. Do not change the proc's promotion logic, the workflow, or unrelated code.
- FIX B must not break the single-variant present behaviour (create/upload/publish already verified).
- Keep helpers/naming consistent.

END with an adversarial self-review: (a) prove the FIX A whitelist rejects a class=3 schema grant and
a class=0 non-CONNECT grant while still passing a freshly created promoter (EXECUTE + CONNECT only);
(b) prove FIX B's ORDER BY and new columns do not change what a single-variant document shows today;
(c) confirm FIX C changed comment text only; (d) file+line of every edit.

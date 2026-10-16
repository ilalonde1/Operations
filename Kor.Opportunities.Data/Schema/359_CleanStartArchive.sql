/*
    KorOpportunitiesDb migration 359: the clean start for BD Director version 2.

    Ian's decision (2026-10-15): the regional managers have never seen the app, so it starts clean and builds forward. On the
    9 October BD call he committed to "purge the old CRM data and re-import it so it's fresh". Measured 2026-10-15 23:15: all
    101 live pursuits are stale — 69 loaded in one batch in May, 6 in June, 23 from the 3 September meeting notes, 2 from email
    threads, 1 grabbed bid (Shackan); none has a logged touch in 90 days; the newest update to any of them is a 7 September sweep.

    Nothing is deleted. In one transaction:
      1. A new stage, Archived = 8 (8 last meant Withdrawn, remapped to Lost on 2026-05-15, and no row holds it). The live
         CK_CrmEngagements_Stage admits only 1, 3, 6, 7, so it is rebuilt to admit 8 first. Every live pursuit (Stage 1 or 3) becomes Archived, with ClosedAtUtc, a note, and a stage-history row.
         Opportunities.Status is not touched.
      2. UX_CrmEngagements_BdRelationship is rebuilt to exclude archived rows, so the tracking-spreadsheet import can create the
         current pursuits without colliding with the archived ones.
      3. One key per person: OwnerStaffId and every AssignedStaffIds token that is a short BdStaff key ("Omar") becomes that
         person's mailbox key ("omara@korstructural.com"), on every pursuit, archived or not. The short-key BdStaff rows are
         deactivated.
      4. Regions take the CRM's own names (BdTrackingViewModel.Regions): "Vancouver Island" -> "VancouverIsland",
         "Okanagan" -> "Okanagan-BcInterior".
      5. BdStaff rows that are not KOR staff are deactivated: the accountants, the contact inbox, a personal gmail address, the
         four old bmzse.com mailboxes and an outside consultant.

    Restore one pursuit: UPDATE opportunities.CrmEngagements SET Stage = <previous stage from CrmEngagementStageHistory>,
    ClosedAtUtc = NULL WHERE Id = <id>.

    Run PART A, look, then PART B, then PART C.
*/
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- ===== PART A: preview, no writes =====
SELECT COUNT(*) AS live_to_archive FROM opportunities.CrmEngagements WHERE Stage IN (1, 3);
SELECT COUNT(*) AS rows_with_short_owner_key
FROM opportunities.CrmEngagements e JOIN opportunities.BdStaff s ON s.StaffKey = e.OwnerStaffId
WHERE s.Mailbox IS NOT NULL AND s.StaffKey <> s.Mailbox;
SELECT Region, COUNT(*) AS n FROM opportunities.CrmEngagements WHERE Region IN (N'Vancouver Island', N'Okanagan') GROUP BY Region;
GO

-- ===== PART B: apply =====
DECLARE @now DATETIMEOFFSET = SYSDATETIMEOFFSET();
DECLARE @n INT;
-- Runs once. A second run would archive every pursuit rebuilt since the clean start.
IF EXISTS (SELECT 1 FROM opportunities.CrmEngagementStageHistory WHERE ByStaffId = N'migration-359')
    THROW 50359, N'Migration 359 has already run (stage-history rows by migration-359 exist). Nothing was changed.', 1;
BEGIN TRANSACTION;

-- 0. the stage constraint admits Archived (live definition on 2026-10-15: Stage IN (1, 3, 6, 7))
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CrmEngagements_Stage'
           AND parent_object_id = OBJECT_ID(N'opportunities.CrmEngagements') AND definition NOT LIKE N'%(8)%')
BEGIN
    ALTER TABLE opportunities.CrmEngagements DROP CONSTRAINT CK_CrmEngagements_Stage;
    ALTER TABLE opportunities.CrmEngagements WITH CHECK ADD CONSTRAINT CK_CrmEngagements_Stage CHECK (Stage IN (1, 3, 6, 7, 8));
END;

-- 1. archive every live pursuit, with a stage-history row
DECLARE @archived TABLE (Id BIGINT PRIMARY KEY);
UPDATE opportunities.CrmEngagements
   SET Stage = 8,
       ClosedAtUtc = @now,
       OutcomeNotes = LEFT(CONCAT(OutcomeNotes, CASE WHEN OutcomeNotes IS NULL THEN N'' ELSE N' | ' END,
                      N'Archived in the 16 Oct 2026 clean start (migration 359); the current pipeline is rebuilt from the regional tracking spreadsheet.'), 4000),
       UpdatedAtUtc = @now,
       UpdatedBy = N'migration-359'
OUTPUT inserted.Id INTO @archived (Id)
 WHERE Stage IN (1, 3);
SET @n = @@ROWCOUNT;
INSERT INTO opportunities.CrmEngagementStageHistory (EngagementId, Stage, EnteredAtUtc, ByStaffId)
SELECT Id, 8, @now, N'migration-359' FROM @archived;
SELECT @n AS pursuits_archived;

-- 2. the relationship uniqueness rule excludes archived rows
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'opportunities.CrmEngagements') AND name = N'UX_CrmEngagements_BdRelationship')
    DROP INDEX UX_CrmEngagements_BdRelationship ON opportunities.CrmEngagements;
CREATE UNIQUE INDEX UX_CrmEngagements_BdRelationship
    ON opportunities.CrmEngagements (BuyerCanonicalOrgId, OwnerStaffId, Region)
    WHERE OpportunityId IS NULL
      AND BuyerCanonicalOrgId IS NOT NULL
      AND OwnerStaffId IS NOT NULL
      AND Region IS NOT NULL
      AND Stage <> 8;

-- 3. one key per person: short key -> mailbox key, owners and assigned seats
DECLARE @keymap TABLE (ShortKey NVARCHAR(150) COLLATE SQL_Latin1_General_CP1_CI_AS PRIMARY KEY, Mailbox NVARCHAR(256) NOT NULL);
INSERT INTO @keymap (ShortKey, Mailbox)
SELECT s.StaffKey, s.Mailbox FROM opportunities.BdStaff s
WHERE s.Mailbox IS NOT NULL AND s.StaffKey <> s.Mailbox
  AND EXISTS (SELECT 1 FROM opportunities.BdStaff m WHERE m.StaffKey = s.Mailbox COLLATE SQL_Latin1_General_CP1_CI_AS);

UPDATE e SET e.OwnerStaffId = k.Mailbox
FROM opportunities.CrmEngagements e JOIN @keymap k ON k.ShortKey = e.OwnerStaffId COLLATE SQL_Latin1_General_CP1_CI_AS;
SELECT @@ROWCOUNT AS owner_keys_remapped;

UPDATE e SET e.AssignedStaffIds = x.Remapped
FROM opportunities.CrmEngagements e
CROSS APPLY (
    SELECT STRING_AGG(COALESCE(k.Mailbox COLLATE DATABASE_DEFAULT, LTRIM(RTRIM(t.value)) COLLATE DATABASE_DEFAULT), N',') AS Remapped
    FROM STRING_SPLIT(e.AssignedStaffIds, N',') t
    LEFT JOIN @keymap k ON k.ShortKey = LTRIM(RTRIM(t.value)) COLLATE SQL_Latin1_General_CP1_CI_AS
    WHERE LTRIM(RTRIM(t.value)) <> N'') x
WHERE e.AssignedStaffIds IS NOT NULL
  AND EXISTS (SELECT 1 FROM STRING_SPLIT(e.AssignedStaffIds, N',') t JOIN @keymap k ON k.ShortKey = LTRIM(RTRIM(t.value)) COLLATE SQL_Latin1_General_CP1_CI_AS);
SELECT @@ROWCOUNT AS assigned_lists_remapped;

UPDATE s SET s.IsActive = 0, s.UpdatedAtUtc = @now
FROM opportunities.BdStaff s JOIN @keymap k ON k.ShortKey = s.StaffKey WHERE s.IsActive = 1;
SELECT @@ROWCOUNT AS short_key_rows_deactivated;

-- 4. regions take the CRM's names
UPDATE opportunities.CrmEngagements SET Region = N'VancouverIsland' WHERE Region = N'Vancouver Island';
UPDATE opportunities.CrmEngagements SET Region = N'Okanagan-BcInterior' WHERE Region = N'Okanagan';

-- 5. not KOR staff
UPDATE opportunities.BdStaff SET IsActive = 0, UpdatedAtUtc = @now
WHERE IsActive = 1 AND (Mailbox NOT LIKE N'%@korstructural.com' OR Mailbox IN (N'clewis@korstructural.com', N'contact@korstructural.com'));
SELECT @@ROWCOUNT AS non_staff_deactivated;

COMMIT TRANSACTION;
GO

-- ===== PART C: check =====
SELECT Stage, COUNT(*) AS pursuits FROM opportunities.CrmEngagements GROUP BY Stage ORDER BY Stage;
SELECT OwnerStaffId, COUNT(*) AS pursuits FROM opportunities.CrmEngagements GROUP BY OwnerStaffId ORDER BY COUNT(*) DESC;
SELECT ISNULL(Region, N'(none)') AS region, COUNT(*) AS pursuits FROM opportunities.CrmEngagements GROUP BY Region ORDER BY COUNT(*) DESC;
SELECT COUNT(*) AS active_staff FROM opportunities.BdStaff WHERE IsActive = 1;
SELECT name, filter_definition FROM sys.indexes WHERE name = N'UX_CrmEngagements_BdRelationship';
GO

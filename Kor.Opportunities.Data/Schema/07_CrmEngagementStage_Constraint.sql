/*
    Kor.OpportunitiesDb - migration 07.
    Tightens opportunities.CrmEngagements CK_CrmEngagements_Stage to the
    post-collapse set {1, 3, 6, 7} (Drafting / Submitted / Won / Lost).

    Prerequisite: 06_CrmStageCollapse.sql must have run so no rows hold
    the removed values {2, 4, 5, 8, 9}.

    Idempotent: drops the old constraint by name if present, asserts the
    data set, then re-adds. Safe to re-run.
*/

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF EXISTS (
    SELECT 1
      FROM opportunities.CrmEngagements
     WHERE Stage NOT IN (1, 3, 6, 7))
BEGIN
    THROW 50000,
          'opportunities.CrmEngagements has Stage values outside {1,3,6,7}. Run 06_CrmStageCollapse.sql first.',
          1;
END;

IF EXISTS (
    SELECT 1
      FROM sys.check_constraints
     WHERE name = 'CK_CrmEngagements_Stage'
       AND parent_object_id = OBJECT_ID('opportunities.CrmEngagements'))
BEGIN
    ALTER TABLE opportunities.CrmEngagements
        DROP CONSTRAINT CK_CrmEngagements_Stage;
END;

ALTER TABLE opportunities.CrmEngagements
    ADD CONSTRAINT CK_CrmEngagements_Stage CHECK (Stage IN (1, 3, 6, 7));

COMMIT TRANSACTION;
GO

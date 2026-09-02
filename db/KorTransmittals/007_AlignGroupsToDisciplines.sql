/*
007_AlignGroupsToDisciplines.sql

Realign the Standard Details document groups to the four detail DISCIPLINES, so the app's
categories match how details are actually classified (detail.Discipline) and how a job is
stripped (concrete job -> drop wood frame). The old set mixed platform (CAD/Revit) with
discipline (Concrete/Wood Frame) and was missing Steel and General.

Before: Concrete, CAD, Revit, Wood Frame.  After: Concrete, Wood Frame, Steel, General.
Safe: the groups hold zero documents (records were purged in 004), so renaming reassigns nothing.
Run in KorTransmittals (transmittals_app or sa).
*/

USE KorTransmittals;
IF DB_NAME() <> N'KorTransmittals' BEGIN RAISERROR('Wrong database on server %s. This runs ONLY in KorTransmittals.', 20, 1, @@SERVERNAME) WITH LOG; END;
SELECT DB_NAME() AS [You are here];
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    -- guard: no documents should be assigned to any group (records were purged)
    IF EXISTS (SELECT 1 FROM dbo.Documents WHERE DocumentGroupId IS NOT NULL)
        RAISERROR('Refused: some documents are assigned to a group - review before renaming.', 16, 1);

    SELECT 'BEFORE' AS phase, DocumentGroupId, Name FROM dbo.DocumentGroups ORDER BY DocumentGroupId;

    UPDATE dbo.DocumentGroups SET Name = N'Steel',   UpdatedUtc = SYSUTCDATETIME() WHERE Name = N'CAD';
    UPDATE dbo.DocumentGroups SET Name = N'General', UpdatedUtc = SYSUTCDATETIME() WHERE Name = N'Revit';
    -- Concrete and Wood Frame keep their names (already disciplines).

    -- assert the four groups are exactly the four disciplines
    IF (SELECT COUNT(*) FROM dbo.DocumentGroups WHERE IsActive = 1 AND Name IN (N'Concrete', N'Wood Frame', N'Steel', N'General')) <> 4
        RAISERROR('Assertion failed: the four active groups are not exactly the four disciplines.', 16, 1);

    SELECT 'AFTER' AS phase, DocumentGroupId, Name FROM dbo.DocumentGroups ORDER BY DocumentGroupId;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

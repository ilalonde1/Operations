-- Allow IT doctrine and synthesis runs; no card text or existing data changes.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'opportunities.DoctrineCard', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'opportunities.CK_DoctrineCard_Role', N'C') IS NOT NULL
        ALTER TABLE opportunities.DoctrineCard DROP CONSTRAINT CK_DoctrineCard_Role;
    ALTER TABLE opportunities.DoctrineCard WITH CHECK ADD CONSTRAINT CK_DoctrineCard_Role
        CHECK (Role IN (N'BD', N'Operations', N'IT'));
END;

IF OBJECT_ID(N'opportunities.BdPromptRun', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'opportunities.CK_BdPromptRun_Role', N'C') IS NOT NULL
        ALTER TABLE opportunities.BdPromptRun DROP CONSTRAINT CK_BdPromptRun_Role;
    ALTER TABLE opportunities.BdPromptRun WITH CHECK ADD CONSTRAINT CK_BdPromptRun_Role
        CHECK (Role IN (N'BD', N'Operations', N'IT'));
END;

COMMIT TRANSACTION;

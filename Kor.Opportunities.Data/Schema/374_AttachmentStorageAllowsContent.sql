/*
    KorOpportunitiesDb migration 374: a pursuit attachment stored in the database satisfies the storage check (20 October 2026).

    Migration 354 moved attachments into the database (OpportunityFiles.Content) and made LocalPath nullable, but left
    CK_Opp_Files_OneStorage requiring SharePointItemId or LocalPath. SqlPursuitFileStore.AddAsync stores Content and sets
    neither, so every attachment since 354 was refused with SQL 547: the Director's Mark submitted with a file, the fee
    proposal launcher, and the CLI's pursuit files add (found storing the Leduc RFP package on pursuit 1112).

    The check now accepts any one of the three places a file can live. Idempotent; the check is replaced only while it still
    has the old two-way definition.
*/
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints
           WHERE name = N'CK_Opp_Files_OneStorage'
             AND parent_object_id = OBJECT_ID(N'opportunities.OpportunityFiles')
             AND definition NOT LIKE N'%Content%')
BEGIN
    ALTER TABLE opportunities.OpportunityFiles DROP CONSTRAINT CK_Opp_Files_OneStorage;
    ALTER TABLE opportunities.OpportunityFiles ADD CONSTRAINT CK_Opp_Files_OneStorage
        CHECK (SharePointItemId IS NOT NULL OR LocalPath IS NOT NULL OR Content IS NOT NULL);
END
GO

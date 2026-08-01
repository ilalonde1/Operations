/*
    Kor.OpportunitiesDb migration 48.

    BD-Tracking spreadsheet integration. Extends CrmEngagements to host the
    BD touchpoint log that partners maintain in
    "KOR Structural BD Tracking 2026.xlsx" (Vancouver/LM, Vancouver Island,
    Alberta, Okanagan/BC Interior, USA, Eastern Canada sheets).

    Two structural changes:

    1) CrmEngagements.OpportunityId becomes NULLABLE. Most BD touchpoints
       (a meeting at a UDI tour, an email about potential hotel work) are
       NOT formal RFPs and have no Opportunity row behind them. The old
       UNIQUE INDEX UX_CrmEngagements_Opportunity becomes a FILTERED unique
       index so it still rejects two engagements pointing at the same
       Opportunity, but allows N null-OpportunityId rows.

    2) Five new columns capture the BD-tracking dimensions the spreadsheet
       has but the existing schema lacks:
         BuyerCanonicalOrgId  -> the firm/buyer this engagement is with
                                  (the "Company" column on the spreadsheet),
                                  needed because OpportunityId can now be null
         Region               -> one of the spreadsheet's regional sheets
                                  (Vancouver/LM, Vancouver Island, etc.)
         ProposalsSubmittedCad -> sum of bids submitted on this engagement
                                  (some engagements have multiple rounds)
         ProposalsAcceptedCad  -> sum of wins
         PotentialProjects     -> freeform "what they're working on / what
                                  we discussed" — the spreadsheet's most
                                  valuable column for BD intel and the one
                                  we want the research-agent cross-link to
                                  match against MPI / Opportunities later.

    Idempotent. Each step gated on sys.columns / sys.indexes existence checks.
    Existing engagement rows survive untouched (they all have OpportunityId
    populated so the NOT NULL relaxation is transparent to them).
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- Step 1: relax CrmEngagements.OpportunityId NOT NULL -> NULL
-- ============================================================
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE Name = N'OpportunityId'
             AND Object_ID = Object_ID(N'opportunities.CrmEngagements')
             AND is_nullable = 0)
BEGIN
    -- Drop the unique index first; ALTER COLUMN won't run while a unique
    -- index references the column.
    IF EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'UX_CrmEngagements_Opportunity'
                 AND object_id = Object_ID(N'opportunities.CrmEngagements'))
    BEGIN
        DROP INDEX UX_CrmEngagements_Opportunity ON opportunities.CrmEngagements;
    END;

    ALTER TABLE opportunities.CrmEngagements
        ALTER COLUMN OpportunityId BIGINT NULL;
END;
GO

-- Recreate the unique index as FILTERED (only enforce uniqueness on
-- non-null OpportunityId; allow many null rows).
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'UX_CrmEngagements_Opportunity'
                 AND object_id = Object_ID(N'opportunities.CrmEngagements'))
BEGIN
    CREATE UNIQUE INDEX UX_CrmEngagements_Opportunity
        ON opportunities.CrmEngagements (OpportunityId)
        WHERE OpportunityId IS NOT NULL;
END;
GO

-- Step 2: new columns
-- ============================================================
IF COL_LENGTH(N'opportunities.CrmEngagements', N'BuyerCanonicalOrgId') IS NULL
    ALTER TABLE opportunities.CrmEngagements ADD BuyerCanonicalOrgId BIGINT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys
               WHERE name = N'FK_CrmEngagements_BuyerCanonical')
BEGIN
    ALTER TABLE opportunities.CrmEngagements
        ADD CONSTRAINT FK_CrmEngagements_BuyerCanonical
            FOREIGN KEY (BuyerCanonicalOrgId)
            REFERENCES opportunities.CanonicalOrg (Id);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_CrmEngagements_BuyerCanonical'
                 AND object_id = Object_ID(N'opportunities.CrmEngagements'))
BEGIN
    CREATE INDEX IX_CrmEngagements_BuyerCanonical
        ON opportunities.CrmEngagements (BuyerCanonicalOrgId)
        WHERE BuyerCanonicalOrgId IS NOT NULL;
END;
GO

IF COL_LENGTH(N'opportunities.CrmEngagements', N'Region') IS NULL
    ALTER TABLE opportunities.CrmEngagements ADD Region NVARCHAR(40) NULL;
GO

IF COL_LENGTH(N'opportunities.CrmEngagements', N'ProposalsSubmittedCad') IS NULL
    ALTER TABLE opportunities.CrmEngagements ADD ProposalsSubmittedCad DECIMAL(18,2) NULL;
GO

IF COL_LENGTH(N'opportunities.CrmEngagements', N'ProposalsAcceptedCad') IS NULL
    ALTER TABLE opportunities.CrmEngagements ADD ProposalsAcceptedCad DECIMAL(18,2) NULL;
GO

IF COL_LENGTH(N'opportunities.CrmEngagements', N'PotentialProjects') IS NULL
    ALTER TABLE opportunities.CrmEngagements ADD PotentialProjects NVARCHAR(MAX) NULL;
GO

-- Step 3: filtered index on Region so the regional-tab UI can grid-load
-- one region without scanning the whole table.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_CrmEngagements_Region'
                 AND object_id = Object_ID(N'opportunities.CrmEngagements'))
BEGIN
    CREATE INDEX IX_CrmEngagements_Region
        ON opportunities.CrmEngagements (Region)
        INCLUDE (OwnerStaffId, BuyerCanonicalOrgId, Stage, ProposalsSubmittedCad, ProposalsAcceptedCad)
        WHERE Region IS NOT NULL;
END;
GO

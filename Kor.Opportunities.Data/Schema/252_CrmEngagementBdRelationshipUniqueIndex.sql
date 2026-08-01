USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 252: BD audit 2026-06-19 C1.
  Enforce the BD-tracking relationship-level natural key after migration 251
  removes verified duplicates.

  Scope is null-Opportunity BD engagements only. Formal opportunity-backed CRM
  engagements remain governed by UX_CrmEngagements_Opportunity.
*/
BEGIN TRAN;

IF EXISTS
(
    SELECT 1
    FROM opportunities.CrmEngagements
    WHERE OpportunityId IS NULL
      AND BuyerCanonicalOrgId IS NOT NULL
      AND OwnerStaffId IS NOT NULL
      AND Region IS NOT NULL
    GROUP BY BuyerCanonicalOrgId, OwnerStaffId, Region
    HAVING COUNT(*) > 1
)
BEGIN
    THROW 51252, 'Cannot create UX_CrmEngagements_BdRelationship: duplicate BD relationship keys remain.', 1;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'opportunities.CrmEngagements')
      AND name = N'UX_CrmEngagements_BdRelationship'
)
BEGIN
    CREATE UNIQUE INDEX UX_CrmEngagements_BdRelationship
        ON opportunities.CrmEngagements (BuyerCanonicalOrgId, OwnerStaffId, Region)
        WHERE OpportunityId IS NULL
          AND BuyerCanonicalOrgId IS NOT NULL
          AND OwnerStaffId IS NOT NULL
          AND Region IS NOT NULL;
    PRINT 'Created UX_CrmEngagements_BdRelationship on BuyerCanonicalOrgId + OwnerStaffId + Region.';
END
ELSE
BEGIN
    PRINT 'UX_CrmEngagements_BdRelationship already exists; skipped.';
END;

PRINT 'Migration 252 complete: BD relationship unique index enforced.';
COMMIT TRAN;
GO

-- 380: retire the "opportunities" the BD alerts mailbox made out of portal mail (2026-10-22). Every one of the 100 rows
-- keyed BDALERTS-* was an Alberta Purchasing Connection system email: the daily "Notification of New Postings" digest
-- (80), status notices for a posting (12), bidder lists (5), account notices (2) and a test RFP (1). The newest digest
-- headed the Opportunities list the morning the regional leads first opened it. The postings those emails list arrive
-- through the APC source itself. GenericEmailFormatAdapter now declines portal notices, so no more are made.
-- Dismissed, never deleted (the house rule; Restore puts one back). None is linked to a pursuit (checked 2026-10-22: 0).
-- Only rows not already dismissed are touched, so re-running changes nothing.
SET XACT_ABORT ON;
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;   -- Opportunities carries a filtered index; sqlcmd starts with this OFF.
BEGIN TRANSACTION;

UPDATE o
   SET DismissedAtUtc  = SYSDATETIMEOFFSET(),
       DismissedBy     = N'migration-380',
       DismissedReason = N'A portal''s own email (digest, status notice, bidder list or account notice), not an opportunity. Its postings arrive through the portal''s own source.'
  FROM opportunities.Opportunities o
 WHERE o.OpportunityKey LIKE N'BDALERTS-%'
   AND o.DismissedAtUtc IS NULL
   AND (   o.Name LIKE N'%Notification of New Postings%'
        OR o.Name LIKE N'APC Opportunity %'
        OR o.Name LIKE N'%Bidders list%'
        OR o.Name LIKE N'%Bidders and Pre-qualified Suppliers List%'
        OR o.Name LIKE N'Your APC %'
        OR o.Name LIKE N'Test RFP %')
   AND NOT EXISTS (SELECT 1 FROM opportunities.CrmEngagements e WHERE e.OpportunityId = o.Id);

COMMIT;

SELECT (SELECT COUNT(*) FROM opportunities.Opportunities WHERE OpportunityKey LIKE N'BDALERTS-%') AS BdAlertRows,
       (SELECT COUNT(*) FROM opportunities.Opportunities WHERE OpportunityKey LIKE N'BDALERTS-%' AND DismissedAtUtc IS NULL) AS StillShown;

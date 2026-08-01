/*
  291_CrmOwnerDirectoryView.sql  (2026-07-17)

  CRM Neural Gap Register G6: CrmEngagements.OwnerStaffId is a free-text
  first name ('Jim', 'Omar', 'Islam' — set by the app and the Deltek
  backfill), while the D2 BdStaff directory holds the real staff identities
  (ids 1-6 = the BD principals). Rather than a risky in-place FK rewrite of
  a column two write-paths feed, this view is the ONE mapping every consumer
  joins through (doctrine: one predicate, one place). If a new BD owner name
  appears unmapped, BdStaffId comes back NULL and the digest routing already
  falls back to the manual map — visible, not silent.
*/

CREATE OR ALTER VIEW opportunities.vw_CrmEngagementOwners
AS
SELECT e.Id            AS EngagementId,
       e.OwnerStaffId  AS OwnerName,
       s.BdStaffId,
       s.CanonicalName
FROM opportunities.CrmEngagements e
OUTER APPLY (SELECT CASE LOWER(LTRIM(RTRIM(ISNULL(e.OwnerStaffId, N''))))
                    WHEN N'conor'  THEN 1
                    WHEN N'islam'  THEN 2
                    WHEN N'jim'    THEN 3
                    WHEN N'john'   THEN 4
                    WHEN N'omar'   THEN 5
                    WHEN N'rory'   THEN 6
                    END AS BdStaffId) m
LEFT JOIN (VALUES (1, N'Conor Murtagh'), (2, N'Islam Shabana'), (3, N'Jim DesRoches'),
                  (4, N'John Markulin'), (5, N'Omar Alcazar Pastrana'), (6, N'Rory Beirne')) AS s (BdStaffId, CanonicalName)
       ON s.BdStaffId = m.BdStaffId;
GO

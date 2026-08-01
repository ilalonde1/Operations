USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO
/* Migration 245: retire generic single-word PLACEHOLDER orgs with NO project
   linkage (e.g. 30 orgs literally named "Architect", + Builder/Construction/
   Contractor/Developer/etc.). They pollute the graph, are selectable for briefs
   (-> barren), and cause false name-containment matches. Retire them + their
   stray affiliations. KEEPS Private/Various/N/A (they carry real MPI proponent
   refs = intentional "undisclosed" catch-alls). */
BEGIN TRAN;
DECLARE @ph TABLE (Id bigint);
INSERT INTO @ph
SELECT o.Id FROM opportunities.CanonicalOrg o
WHERE o.RetiredAtUtc IS NULL
  AND LOWER(o.DisplayName) IN ('architect','architects','builder','construction','consultant','contractor','developer','general contractor','multiple','owner','none','tbd','unknown','engineering')
  AND NOT EXISTS (SELECT 1 FROM opportunities.MajorProjectsInventory m WHERE m.ArchitectCanonicalOrgId=o.Id OR m.ProponentCanonicalOrgId=o.Id OR m.GeneralContractorCanonicalOrgId=o.Id OR m.StructuralEngineerCanonicalOrgId=o.Id)
  AND o.ClendorClientId IS NULL;
UPDATE opportunities.IntelPersonAffiliation SET RetiredAtUtc=sysdatetimeoffset(), RetiredReason=N'Affiliated to generic placeholder org, retired (migration 245)', IsCurrent=0 WHERE CanonicalOrgId IN (SELECT Id FROM @ph) AND RetiredAtUtc IS NULL;
UPDATE opportunities.CanonicalOrg SET RetiredAtUtc=sysdatetimeoffset(), RetiredReason=N'Generic placeholder org, no project linkage (migration 245)', UpdatedAtUtc=sysdatetimeoffset() WHERE Id IN (SELECT Id FROM @ph);
DECLARE @n int = (SELECT COUNT(*) FROM @ph);
PRINT 'Migration 245: retired ' + CAST(@n AS varchar(10)) + ' generic placeholder orgs.';
COMMIT TRAN;
GO

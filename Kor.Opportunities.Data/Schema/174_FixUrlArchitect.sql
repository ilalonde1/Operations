USE [KorOpportunitiesDb];
GO

/* =====================================================================
   174 — Fix one egregious data-quality defect surfaced building the
   architect matrix: the BC MPI feed stored a Livabl URL in the ARCHITECT
   field for "Lodana Condominium", so the resolver minted a canonical org
   named "https://www.livabl.com/architect/integra-architecture". Relink the
   project to the real firm (Integra Architecture Inc., 71250) and retire the
   URL org. (Upstream: provider should reject non-name ARCHITECT values.)
   ===================================================================== */
SET NOCOUNT ON;
DECLARE @now DATETIMEOFFSET = SYSDATETIMEOFFSET();

UPDATE opportunities.MajorProjectsInventory
SET ArchitectCanonicalOrgId = 71250, ArchitectName = N'Integra Architecture Inc.', UpdatedAtUtc = @now
WHERE Id = 7529 AND ArchitectCanonicalOrgId = 76117;

UPDATE opportunities.CanonicalOrg
SET RetiredAtUtc = @now, RetiredReason = N'URL-as-name junk; project relinked to Integra Architecture 71250 — migration 174', UpdatedAtUtc = @now
WHERE Id = 76117 AND RetiredAtUtc IS NULL;
GO

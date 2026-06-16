USE [KorOpportunitiesDb];
GO

/* =====================================================================
   159 — Link major-GC canonical ids from the GeneralContractorName text.
   ---------------------------------------------------------------------
   The marquee open-seat mega-projects (Annacis WWTP, Richmond Hospital via GC
   Graham; Burnaby Hospital via GC PCL) carried their GC only as free text, so
   vOpenSeatPursuit.GcWarmScore was NULL and KOR's strongest warm-path (KOR is a
   Graham client) didn't surface. This resolves the two major GCs KOR has a
   relationship with to their canonical ids on active, unlinked projects:
     Graham Construction  -> 69232 (KOR Deltek client, 11 contacts)
     PCL Construction     -> 13243 (25 contacts)
   Conservative: only active rows where GeneralContractorCanonicalOrgId IS NULL
   and the text clearly names the firm. Idempotent.
   ===================================================================== */

UPDATE opportunities.MajorProjectsInventory
SET GeneralContractorCanonicalOrgId = 69232, UpdatedAtUtc = SYSDATETIMEOFFSET()
WHERE RetiredAtUtc IS NULL AND GeneralContractorCanonicalOrgId IS NULL
  AND GeneralContractorName LIKE N'Graham Construction%';

UPDATE opportunities.MajorProjectsInventory
SET GeneralContractorCanonicalOrgId = 13243, UpdatedAtUtc = SYSDATETIMEOFFSET()
WHERE RetiredAtUtc IS NULL AND GeneralContractorCanonicalOrgId IS NULL
  AND (GeneralContractorName LIKE N'PCL Construction%' OR GeneralContractorName LIKE N'PCL Constructors%');
GO

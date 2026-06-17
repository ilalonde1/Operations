USE [KorOpportunitiesDb];
GO

/* =====================================================================
   179 — Execute the data-enrichment actions that were (incorrectly) listed
   inside the circulated dossiers. Most were already done by prior enrichment
   (Design Works Eng / Timber Engineering / Aspect / ROC / Stack canonicals
   exist; UBC Gateway->RJC and BCIT->Fast+Epp SE links exist; 293 projects
   already tagged 'Seismic'). Remaining clean items:
     1. add Nomodic (modular manufacturer/CM) as a canonical
     2. reclassify ROC Modular from Vendor -> Modular
     3. tag the modular/prefab pipeline (KorPipelineTag) for sliceability
     4. tag the mass-timber pipeline
   (Per-project SE backfill on the modular school projects + the Bryson 80+
   seismic list + EGBC registry are research tasks handled separately.)
   ===================================================================== */
SET NOCOUNT ON;
DECLARE @now DATETIMEOFFSET = SYSDATETIMEOFFSET();

/* 1. Nomodic */
IF NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg WHERE NormalizedName = N'nomodic' OR DisplayName = N'Nomodic')
  INSERT opportunities.CanonicalOrg (Kind, DisplayName, NormalizedName, Website, Notes, CreatedAtUtc, UpdatedAtUtc)
  VALUES (N'Modular', N'Nomodic', N'nomodic', N'https://nomodic.com',
          N'Modular manufacturer / construction-manager (Calgary; ~500 BC units: Goodacre, Juniper, Atira, North Saanich) — modular dossier', @now, @now);

/* 2. ROC Modular -> Modular kind */
UPDATE opportunities.CanonicalOrg SET Kind = N'Modular', UpdatedAtUtc = @now
WHERE Id = 14599 AND Kind = N'Vendor';

/* 3. modular/prefab pipeline tag (don't overwrite existing tags) */
UPDATE opportunities.MajorProjectsInventory SET KorPipelineTag = N'ModularPrefab', UpdatedAtUtc = @now
WHERE RetiredAtUtc IS NULL AND KorPipelineTag IS NULL
  AND (ProjectName LIKE N'%modular%' OR ProjectName LIKE N'%prefab%' OR ISNULL(ConstructionType,'') LIKE N'%odular%' OR ISNULL(SubSector,'') LIKE N'%odular%');

/* 4. mass-timber pipeline tag (don't overwrite existing tags) */
UPDATE opportunities.MajorProjectsInventory SET KorPipelineTag = N'MassTimber', UpdatedAtUtc = @now
WHERE RetiredAtUtc IS NULL AND KorPipelineTag IS NULL
  AND (ProjectName LIKE N'%mass timber%' OR ProjectName LIKE N'%mass-timber%' OR ProjectName LIKE N'%CLT%' OR ISNULL(ConstructionType,'') LIKE N'%imber%');
GO

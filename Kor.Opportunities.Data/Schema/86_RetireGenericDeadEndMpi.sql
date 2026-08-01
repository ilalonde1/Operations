SET XACT_ABORT ON;
GO

-- Soft-retire MPI rows with truly-generic project names that the
-- proponent drain confirmed are unresearchable (Sonnet's verdict:
-- "Project name is too generic to identify a proponent without
-- fabricating"). These rows have NO street address, NO unique
-- identifier, and the project name is the only attribute — they'll
-- never have a meaningful proponent attribution.
--
-- Same retire infrastructure used elsewhere (RetiredAtUtc +
-- RetiredReason). Briefs / dashboards / dedup already filter on
-- RetiredAtUtc IS NULL.

BEGIN TRAN;

DECLARE @retired int;

UPDATE opportunities.MajorProjectsInventory
SET RetiredAtUtc = sysdatetimeoffset(),
    RetiredReason = N'R95-extra Phase B: generic-name MPI row, unresearchable (proponent drain confirmed)'
WHERE RetiredAtUtc IS NULL
  -- Replay-safety retrofit (audit 2026-07-01): never retire an MPI a pursuit
  -- is linked to — mirrors DataRetirementJob's exemption from migration-267 era.
  AND NOT EXISTS (SELECT 1 FROM opportunities.CrmEngagementProjectLink l
                  WHERE l.MajorProjectsInventoryId = opportunities.MajorProjectsInventory.Id)
  AND (ProponentName IS NULL OR LEN(LTRIM(RTRIM(ProponentName))) = 0)
  AND ProjectName IN (
      N'Condominium Development',
      N'Residential Condominium',
      N'Highrise Condominiums',
      N'Highrise Condominium',
      N'Lowrise Condominium',
      N'Rental Towers',
      N'Residential Tower',
      N'Mixed-Use Development',
      N'Office Building',
      N'Office Tower',
      N'Midrise Apartment',
      N'Mid-Rise Apartment',
      N'Terraced Condominium',
      N'Waterfront Revitalization Project'
  );
SET @retired = @@ROWCOUNT;
PRINT 'Generic dead-end MPI rows retired: ' + CONVERT(varchar(20), @retired);

-- Post-state — remaining NULL-proponent active rows
SELECT 'Remaining NULL-proponent active MPI rows' AS Stat, COUNT(*) AS Cnt
FROM opportunities.MajorProjectsInventory
WHERE RetiredAtUtc IS NULL
  AND (ProponentName IS NULL OR LEN(LTRIM(RTRIM(ProponentName))) = 0);

PRINT 'Migration 86 generic dead-end retire complete.';

COMMIT TRAN;
GO

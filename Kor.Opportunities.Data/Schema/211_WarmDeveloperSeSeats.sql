USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 211: decompose the warm-developer SEOR research (2026-06-18,
  docs/ca-research-raw/warm-developer-seor-2026-06-18.md).
  - 2nd KOR beachhead: West / 1011 Union St (Holland, SD) — KOR is SEOR (per KOR
    portfolio). Holland is an existing Deltek client => lead qualification.
  - Create IMEG Corp (Competitor) — TCA Architects' default NorCal multifamily SE;
    the displacement target to win TCA-led work (Holland Stevens Creek, Carmel).
  - Annotate SEOR status on existing Holland/Sutter/Related MPI rows.
  - Add open-seat / displacement targets: Carmel Admirals Cove (OPEN), Brookfield
    Gateway Village Irvine (OPEN, wood-frame), Carmel Revery (IMEG, displacement record).
  - Note TCA as the pipeline-key architect (Holland + Carmel both use it; IMEG incumbent).
*/

DECLARE @Holland bigint=68644, @CarrierJohnson bigint=112, @TCA bigint=76956, @Carmel bigint=68641, @BrookfieldRes bigint=53589;

BEGIN TRAN;

-- Create IMEG Corp (Competitor) if missing.
DECLARE @IMEG bigint = (SELECT Id FROM opportunities.CanonicalOrg WHERE NormalizedName=N'imegcorp' AND RetiredAtUtc IS NULL);
IF @IMEG IS NULL
BEGIN
  INSERT INTO opportunities.CanonicalOrg (DisplayName, Kind, CreatedAtUtc, UpdatedAtUtc)
  VALUES (N'IMEG Corp', N'Competitor', sysdatetimeoffset(), sysdatetimeoffset());
  SET @IMEG = SCOPE_IDENTITY();
END;

-- KOR's 2nd CA beachhead: 1011 Union St / "West" (Holland, San Diego). KOR = SEOR.
IF NOT EXISTS (SELECT 1 FROM opportunities.MajorProjectsInventory WHERE Province=N'CA' AND SourceKey=N'korref:west-1011-union')
  INSERT INTO opportunities.MajorProjectsInventory
    (Province, SourceKey, ProjectName, Sector, Stage, ProponentName, ProponentCanonicalOrgId, ArchitectName, ArchitectCanonicalOrgId, MunicipalityName, ScheduleNotes, SourceUrl, RawJson)
  VALUES
    (N'CA', N'korref:west-1011-union', N'1011 Union St / West (San Diego, Little Italy)', N'Residential High-Rise', N'Built - KOR SE of record',
     N'Holland Partner Group', @Holland, N'Carrier Johnson + Culture', @CarrierJohnson, N'San Diego',
     N'37-story, 431 units. KOR Structural = SE of record (per KOR portfolio). 2nd SD beachhead, with an existing Deltek client (Holland) -> lead qualification for the Holland Stevens Creek pursuit.',
     N'http://www.korstructural.com', N'{"source":"warm-dev-seor-research-2026-06-18","korRole":"SE of record"}');

-- Annotate SEOR status on existing MPI rows.
UPDATE opportunities.MajorProjectsInventory SET ProponentCanonicalOrgId=@Holland, ArchitectCanonicalOrgId=@TCA,
  ScheduleNotes=LEFT(N'SE STATUS: OPEN (no public SEOR) - #1 target, just cleared ministerial approval Dec 2025 (procurement now). Architect TCA (incumbent risk IMEG). Warm: Holland is a Deltek client + KOR delivered their 1011 Union tower - lead with that. ' + COALESCE(ScheduleNotes,N''),1000), UpdatedAtUtc=sysdatetimeoffset()
WHERE Id=8282;  -- 3896 Stevens Creek Blvd (Holland San Jose 540u)

UPDATE opportunities.MajorProjectsInventory SET ScheduleNotes=LEFT(N'SEOR: Buehler Engineering (LOCKED via IFOA) - do not chase. ' + COALESCE(ScheduleNotes,N''),1000), UpdatedAtUtc=sysdatetimeoffset()
WHERE Id=3946;  -- Sutter Santa Clara West Campus

UPDATE opportunities.MajorProjectsInventory SET ScheduleNotes=LEFT(N'SE STATUS: OPEN - pre-design (prelim app Mar 2026); the real Sutter opening. IPD/IFOA: owner selects whole team at inception - KOR must attach to a shortlisted architect (SmithGroup/HGA). Contact Larry Kollerer. ' + COALESCE(ScheduleNotes,N''),1000), UpdatedAtUtc=sysdatetimeoffset()
WHERE Id=3947;  -- Sutter Emeryville flagship

UPDATE opportunities.MajorProjectsInventory SET ScheduleNotes=LEFT(N'SEOR: Degenkolb (PROBABLE; incumbent on prior CPMC w/ SmithGroup) - seat filled, construction started Jun 2025. Verify-only. ' + COALESCE(ScheduleNotes,N''),1000), UpdatedAtUtc=sysdatetimeoffset()
WHERE Id=3948;  -- Sutter SF neuroscience/Geary

-- Add open-seat + displacement targets missing from MPI.
MERGE opportunities.MajorProjectsInventory AS T
USING (VALUES
  (N'korref:carmel-admirals-cove', N'Admirals Cove, Alameda (227 units)', N'Pre-construction (planning approved)', @Carmel, NULL, NULL,
     N'SE STATUS: OPEN (no public SEOR). Architect BDE Architecture; civil BKF. Contact Will Cipes (Carmel).'),
  (N'korref:brookfield-gateway-village', N'Gateway Village, Irvine (1,227 units)', N'Pre-construction (vertical ~2027)', @BrookfieldRes, N'KTGY', 76890,
     N'SE STATUS: OPEN (no public SEOR). Architect KTGY (Irvine; picks SE early). Contacts Foley/Roden. CAVEAT: wood-frame/low-rise typology - confirm KOR fit.'),
  (N'korref:carmel-revery', N'Revery, Burlingame (311 units)', N'Complete (2025)', @Carmel, N'TCA Architects', @TCA,
     N'SEOR: IMEG Corp (TCA default NorCal SE) - displacement record. TCA is the pipeline key (Holland Stevens Creek + Carmel both use TCA); win TCA from IMEG.')
) AS S(SourceKey, ProjectName, Stage, PropId, ArchName, ArchId, Notes)
ON T.Province=N'CA' AND T.SourceKey=S.SourceKey
WHEN NOT MATCHED THEN
  INSERT (Province, SourceKey, ProjectName, Sector, Stage, ProponentCanonicalOrgId, ArchitectName, ArchitectCanonicalOrgId, MunicipalityName, ScheduleNotes, SourceUrl, RawJson)
  VALUES (N'CA', S.SourceKey, S.ProjectName, N'Residential', S.Stage, S.PropId, S.ArchName, S.ArchId, NULL, S.Notes, N'https://sfyimby.com', N'{"source":"warm-dev-seor-research-2026-06-18"}');

PRINT 'Migration 211: warm-developer SEOR status + 1011 Union beachhead + IMEG recorded.';
COMMIT TRAN;
GO

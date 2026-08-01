USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 217: follow-the-client layer (research 2026-06-18). KOR's BC developer
  clients' California projects with open SE seats, annotated with KOR's anchor
  reference. Glotman Simpson is the SD/San Jose incumbent on these clients' built
  towers; the open seats are the window to re-enter BEFORE the SE is appointed.
  Concord Pacific + Wesgroup have NO California presence (don't chase).
*/

DECLARE @Bosa bigint=69677, @Pinnacle bigint=53665, @Westbank bigint=75642;

BEGIN TRAN;

-- #1 follow-the-client target: Bosa First & Island (already in MPI 8297).
UPDATE opportunities.MajorProjectsInventory
SET ProponentCanonicalOrgId=@Bosa,
    Stage=N'Permits filed; construction paused',
    ScheduleNotes=LEFT(N'SE OPEN (Glotman is Bosa default but NOT named) - #1 follow-the-client target. 211u/35-story; Bosa paused = window BEFORE SE appointment. KOR anchor: did The Grande + Bayside + Marquee for Bosa. Architect James KM Cheng (Vancouver). ' + COALESCE(ScheduleNotes,N''),1000),
    UpdatedAtUtc=sysdatetimeoffset()
WHERE Id=8297;

-- Andia (Bosa) — Glotman-locked, don't chase.
UPDATE opportunities.MajorProjectsInventory
SET ScheduleNotes=LEFT(N'SEOR: Glotman Simpson (confirmed) - do not chase. ' + COALESCE(ScheduleNotes,N''),1000), UpdatedAtUtc=sysdatetimeoffset()
WHERE Id=8173;

-- Insert the other BC-client open seats.
MERGE opportunities.MajorProjectsInventory AS T
USING (VALUES
 (N'korref:pinnacle-pacific-heights', N'Pinnacle Pacific Heights (492 units, 31-story)', N'Permits under review (filed 2023)', N'San Diego', @Pinnacle, N'Pinnacle International', N'Undisclosed', N'SE OPEN (Glotman is Pinnacle default but NOT named on this one). KOR has BC Pinnacle relationship (Deltek client) + adjacent Bosa SD work. Challenger.'),
 (N'korref:westbank-terraine', N'Westbank Terraine, San Jose (345 units)', N'Site Dev Permit applied Mar 2025', N'San Jose', @Westbank, N'Westbank', N'Studio Gang', N'SE OPEN - Studio Gang has no confirmed West Coast SE; genuine opening. Westbank = Vancouver firm (warm intro via James KM Cheng shared-architect path).'),
 (N'korref:westbank-sofa', N'Westbank SoFa Towers / 300 S First, San Jose (1,147 units)', N'Permits applied Oct 2024', N'San Jose', @Westbank, N'Westbank', N'Steinberg Hart', N'SE OPEN - huge (three 30-story) but no timeline; long-game positioning. Westbank Vancouver relationship.'),
 (N'korref:westbank-arbor', N'Westbank Arbor, San Jose (14-story mass timber)', N'Design development', N'San Jose', @Westbank, N'Westbank', N'Studio Gang', N'SE OPEN - mass timber = strong KOR credential pitch. Westbank Vancouver.'),
 (N'korref:westbank-bank-of-italy', N'Westbank Bank of Italy Tower, San Jose (adaptive reuse)', N'Pre-construction', N'San Jose', @Westbank, N'Westbank', N'Bjarke Ingels Group', N'SE OPEN - historic adaptive reuse (heritage angle). Speculative timeline.')
) AS S(SourceKey, ProjectName, Stage, City, PropId, PropName, Arch, Notes)
ON T.Province=N'CA' AND T.SourceKey=S.SourceKey
WHEN NOT MATCHED THEN
  INSERT (Province, SourceKey, ProjectName, Sector, Stage, ProponentName, ProponentCanonicalOrgId, ArchitectName, MunicipalityName, ScheduleNotes, SourceUrl, RawJson)
  VALUES (N'CA', S.SourceKey, S.ProjectName, N'Residential High-Rise', S.Stage, S.PropName, S.PropId, S.Arch, S.City, S.Notes, N'https://buildsd.org', N'{"source":"bc-client-pipeline-2026-06-18","seStatus":"open","followTheClient":true}');

PRINT 'Migration 217: BC-client CA open-seat pipeline recorded.';
COMMIT TRAN;
GO

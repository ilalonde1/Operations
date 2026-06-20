USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO
/* Migration 240: formalize Tier-1 warm-client open-seat pursuits (per Ian, 2026-06-20).
   Enrich 4 existing MPI rows + create 3 missing + repoint 1700 Alberni proponent
   from the Bosa/Kingswood JV-string (54232) to clean Bosa Properties (38943).
   All SE seats OPEN; tagged KorPipelineTag='tier1-pursuit'. Architect = the
   channel to pursue. Source: warm-client-pursuits.json (PM research). */
BEGIN TRAN;

-- 1) Repoint 1700 Alberni proponent JV-string -> clean Bosa Properties
UPDATE opportunities.MajorProjectsInventory SET ProponentCanonicalOrgId=38943 WHERE Id=2633 AND ProponentCanonicalOrgId=54232;

-- 2) Enrich existing Tier-1 rows
UPDATE opportunities.MajorProjectsInventory SET ArchitectName=COALESCE(ArchitectName,N'JYOM Architecture'), ArchitectCanonicalOrgId=COALESCE(ArchitectCanonicalOrgId,55064), SeatStatus=N'Open', KorPipelineTag=N'tier1-pursuit', Stage=N'Rezoning', ScheduleNotes=N'TIER-1 PURSUIT. 67 storeys; 480 strata + 152 social + 206 hotel. Architect JYOM (channel to Pinnacle''s whole pipeline). KOR warm w/ Pinnacle (Deltek client). SE seat OPEN. [2026-06-20]', UpdatedAtUtc=sysdatetimeoffset() WHERE Id=2416;  -- 601 Beach Crescent (Pinnacle)
UPDATE opportunities.MajorProjectsInventory SET ArchitectName=COALESCE(ArchitectName,N'Henriquez Partners Architects'), SeatStatus=N'Open', KorPipelineTag=N'tier1-pursuit', Stage=N'DP', ScheduleNotes=N'TIER-1 PURSUIT. 613 units; 44 + 41 storey towers. Architect Henriquez. Bosa (warm, Deltek). WARNING: Glotman is Bosa''s recurring incumbent SE - verify no preferred-vendor lock before investing. SE seat OPEN. [2026-06-20]', UpdatedAtUtc=sysdatetimeoffset() WHERE Id=2633;  -- 1700 Alberni (Bosa)
UPDATE opportunities.MajorProjectsInventory SET ArchitectName=COALESCE(ArchitectName,N'DIALOG'), ArchitectCanonicalOrgId=COALESCE(ArchitectCanonicalOrgId,6154), SeatStatus=N'Open', KorPipelineTag=N'tier1-pursuit', Stage=N'DP', ScheduleNotes=N'TIER-1 PURSUIT. 363 market rental; 22 + 18 storey towers / 7-storey podium. Architect DIALOG. Greystar (warm). STRONGEST LEVER: KOR is Greystar''s SE on 6th & Palm, San Diego. SE seat OPEN. [2026-06-20]', UpdatedAtUtc=sysdatetimeoffset() WHERE Id=3381;  -- 1527 Main (Greystar)
UPDATE opportunities.MajorProjectsInventory SET ArchitectName=COALESCE(ArchitectName,N'Michael Green Architecture / Arcadis'), SeatStatus=N'Open', KorPipelineTag=N'tier1-pursuit', Stage=N'DP', ScheduleNotes=N'TIER-1 PURSUIT. 219 rental + 179 hotel; 20 storeys; 8-36 W Cordova (DTES). Architect Michael Green + Arcadis. Proponent BlueSky Properties (Bosa family rental arm). WARNING: Glotman is Bosa''s incumbent SE - verify. Paired with Samuel Tower. SE seat OPEN. [2026-06-20]', UpdatedAtUtc=sysdatetimeoffset() WHERE Id=6613;  -- Cohen Block (BlueSky/Bosa)

-- 3) Create the 3 missing Tier-1 rows
INSERT INTO opportunities.MajorProjectsInventory (Province, SourceKey, ProjectName, ProponentName, ProponentCanonicalOrgId, MunicipalityName, Stage, SeatStatus, ArchitectName, ArchitectCanonicalOrgId, ProjectDescription, ScheduleNotes, KorPipelineTag, FirstSeenAtUtc, LastSeenAtUtc, UpdatedAtUtc)
SELECT * FROM (VALUES
 (N'BC', N'warm-pursuit-2026-06-20:samuel-tower', N'Samuel Tower (15-27 W Hastings St)', N'BlueSky Properties Inc.', CAST(71099 AS bigint), N'Vancouver', N'DP', N'Open', N'Michael Green Architecture / Arcadis', CAST(NULL AS bigint), N'549 rental units, 40 storeys, DTES Vancouver. Bosa-family (BlueSky). Paired with Cohen Block.', N'TIER-1 PURSUIT. Architect Michael Green + Arcadis. WARNING: Glotman is Bosa''s incumbent SE - verify no lock. SE seat OPEN. [2026-06-20]', N'tier1-pursuit', sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset()),
 (N'BC', N'warm-pursuit-2026-06-20:1770-w-12th', N'1770 West 12th Avenue (Burrard & 12th)', N'Greystar Real Estate Partners', CAST(55110 AS bigint), N'Vancouver', N'DP', N'Open', N'DIALOG', CAST(6154 AS bigint), N'244 units (194 market + 49 below-market), 24 storeys / 251 ft.', N'TIER-1 PURSUIT. Architect DIALOG. Greystar (warm). LEVER: KOR is Greystar''s SE on 6th & Palm, San Diego. SE seat OPEN. [2026-06-20]', N'tier1-pursuit', sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset()),
 (N'BC', N'warm-pursuit-2026-06-20:1220-station', N'1220 Station Street (False Creek Flats)', N'GWL Realty Advisors', CAST(69629 AS bigint), N'Vancouver', N'Rezoning', N'Open', N'MCM Architects', CAST(NULL AS bigint), N'470 units; 36 + 28 storey towers / 8-storey podium; False Creek Flats rental.', N'TIER-1 PURSUIT. Architect MCM. GWLRA (warm). SE seat OPEN. [2026-06-20]', N'tier1-pursuit', sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset())
) v(Province,SourceKey,ProjectName,ProponentName,ProponentCanonicalOrgId,MunicipalityName,Stage,SeatStatus,ArchitectName,ArchitectCanonicalOrgId,ProjectDescription,ScheduleNotes,KorPipelineTag,FirstSeenAtUtc,LastSeenAtUtc,UpdatedAtUtc)
WHERE NOT EXISTS (SELECT 1 FROM opportunities.MajorProjectsInventory m WHERE m.SourceKey=v.SourceKey);

PRINT 'Migration 240: Tier-1 warm pursuits formalized (4 enriched, 3 created, 1 proponent repointed).';
COMMIT TRAN;
GO

USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO
/* Migration 239: factual enrichment of BC Housing / BC Builds open-seat projects
   from PM research (docs/enrichment-2026-06-20-pm/bc-housing-seats.json).
   Sets architect/GC/SE text names (COALESCE - no clobber) + ScheduleNotes with
   verified team + SE-seat status. 2 seats confirmed FILLED (9 Dot, Equilibrium);
   3 Tier-A OPEN (architect picks SE - pursue). */
BEGIN TRAN;
-- FILLED seats (no KOR opportunity, but record the SE)
UPDATE opportunities.MajorProjectsInventory SET StructuralEngineerName=COALESCE(StructuralEngineerName,N'9 Dot Engineering'), ArchitectName=COALESCE(ArchitectName,N'Stanley Office of Architecture'), GeneralContractorName=COALESCE(GeneralContractorName,N'Pac Western Builders'), ScheduleNotes=N'SE FILLED: 9 Dot Engineering. 55u modular (Roc Modular); U/C since 2025. No KOR opportunity. [PM research 2026-06-20]', UpdatedAtUtc=sysdatetimeoffset() WHERE Id=7405;
UPDATE opportunities.MajorProjectsInventory SET StructuralEngineerName=COALESCE(StructuralEngineerName,N'Equilibrium Consultants Inc.'), ArchitectName=COALESCE(ArchitectName,N'Formline Architecture + Urbanism'), ScheduleNotes=N'SE FILLED: Equilibrium Consultants Inc. 35u, ~$23M; opening 2027. Owner contact Tony Goulet (Quesnel Tillicum Society). No KOR opportunity. [PM research 2026-06-20]', UpdatedAtUtc=sysdatetimeoffset() WHERE Id=7404;
-- TIER-A OPEN seats (architect picks SE -> pursue via architect)
UPDATE opportunities.MajorProjectsInventory SET ArchitectName=COALESCE(ArchitectName,N'Zeidler Architects'), GeneralContractorName=COALESCE(GeneralContractorName,N'North Mountain Construction'), ScheduleNotes=N'SE SEAT OPEN (architect picks). Architect Zeidler; GC North Mountain; dev-mgr New Commons; operator Elk Valley Family Society. 44u ~$15-20M; U/C, completion 2027. PURSUE via Zeidler. [PM research 2026-06-20]', UpdatedAtUtc=sysdatetimeoffset() WHERE Id=7406;
UPDATE opportunities.MajorProjectsInventory SET ArchitectName=COALESCE(ArchitectName,N'Richard Hunter Architect Inc.'), GeneralContractorName=COALESCE(GeneralContractorName,N'ARPA Investments'), ScheduleNotes=N'SE SEAT likely OPEN. Architect Richard Hunter Architect Inc.; developer/GC ARPA Investments. 85u + 7,600sf commercial, ~$29.2M; occupancy Nov 2027. Interior BC. PURSUE via Richard Hunter. [PM research 2026-06-20]', UpdatedAtUtc=sysdatetimeoffset() WHERE Id=7398;
UPDATE opportunities.MajorProjectsInventory SET ArchitectName=COALESCE(ArchitectName,N'GBL Architects'), GeneralContractorName=COALESCE(GeneralContractorName,N'VanMar Constructors'), ScheduleNotes=N'SE SEAT OPEN/recent. Architect GBL Architects; GC VanMar Constructors; dev-consultant Empacta; operator Aunt Leah''s (ED Jacqueline Dupuis). 89u woodframe; U/C since Sep 2025, completion summer 2027. Lower Mainland. PURSUE via GBL. [PM research 2026-06-20]', UpdatedAtUtc=sysdatetimeoffset() WHERE Id=7408;
-- COMPLETE (record team; nightly retirement handles built status)
UPDATE opportunities.MajorProjectsInventory SET ArchitectName=COALESCE(ArchitectName,N'Low Hammond Rowe Architects'), GeneralContractorName=COALESCE(GeneralContractorName,N'WCPG Construction'), ScheduleNotes=N'COMPLETE - opened May 2026. 163 rental + 80-bed shelter, $145M, 14-storey concrete, 1015 E Hastings. No opportunity. [PM research 2026-06-20]', UpdatedAtUtc=sysdatetimeoffset() WHERE Id=7407;
UPDATE opportunities.MajorProjectsInventory SET ScheduleNotes=N'COMPLETE - opened early 2026 (official June 3 2026). 73u seniors, ~$11.6M. No opportunity. [PM research 2026-06-20]', UpdatedAtUtc=sysdatetimeoffset() WHERE Id=7401;
PRINT 'Migration 239: BC Housing seat enrichment (2 filled, 3 Tier-A open, 2 complete).';
COMMIT TRAN;
GO

USE [KorOpportunitiesDb];
GO

/* =====================================================================
   175 — Normalize the BC interior region taxonomy (clean at source).
   Two defects surfaced building the region landscape:
     (1) Source-split: the live BC MPI feed tags interior projects
         'Okanagan' while the research streams tag them 'Okanagan/Interior'
         — same towns (Kelowna/Kamloops/Penticton). Collapse to 'Okanagan'.
     (2) Kootenay + Cariboo towns (Fernie, Golden, Nelson, Salmo, Nakusp,
         Hosmer / Quesnel, Clinton) were misfiled under the interior bucket.
         Reassign to their real existing regions.
   After this the Okanagan funnel is one honest region; Kootenay/Cariboo
   gain their true projects. (Provider MapRegion alignment queued separately
   so the weekly MPI re-ingest stays consistent.)
   ===================================================================== */
SET NOCOUNT ON;
DECLARE @now DATETIMEOFFSET = SYSDATETIMEOFFSET();

/* (1) collapse the source-split */
UPDATE opportunities.MajorProjectsInventory
SET RegionName = N'Okanagan', UpdatedAtUtc = @now
WHERE Province = 'BC' AND RetiredAtUtc IS NULL AND RegionName = N'Okanagan/Interior';

/* (2a) reassign clear Kootenay towns */
UPDATE opportunities.MajorProjectsInventory
SET RegionName = N'Kootenay', UpdatedAtUtc = @now
WHERE Province = 'BC' AND RetiredAtUtc IS NULL AND RegionName = N'Okanagan'
  AND MunicipalityName IN (N'Golden', N'Fernie', N'Nelson', N'Salmo', N'Nakusp', N'Hosmer',
        N'Kimberley', N'Cranbrook', N'Creston', N'Invermere', N'Trail', N'Castlegar',
        N'Rossland', N'Sparwood', N'Elkford', N'Kaslo', N'Grand Forks');

/* (2b) reassign clear Cariboo towns */
UPDATE opportunities.MajorProjectsInventory
SET RegionName = N'Cariboo', UpdatedAtUtc = @now
WHERE Province = 'BC' AND RetiredAtUtc IS NULL AND RegionName = N'Okanagan'
  AND MunicipalityName IN (N'Quesnel', N'Clinton', N'Williams Lake', N'100 Mile House');
GO

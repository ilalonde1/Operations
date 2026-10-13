-- 339: declare the time zone CanadaBuys publishes its date columns in.
--
-- Every ingestion parse site used to pass DateTimeStyles.AssumeUniversal, which
-- relabels a source's published LOCAL wall clock as UTC. Measured 2026-10-13 on
-- production: 2,010 of 2,377 deadline-bearing opportunities were stored early by
-- their source's UTC offset.
--
-- Most sources name their zone in the row text and are fixed in code
-- (PublishedClock + the scrapers). CanadaBuys does not: its
-- tenderClosingDate-dateFermetureSoumissions column is a naked
-- "2026-10-28T14:00:00" with no offset and no abbreviation, so the zone has to
-- be configuration.
--
-- Which zone, and how it was established: the live notice renders that same
-- field value as "2026/10/28 14:00 EDT" (WS5914161515-Doc5922224048) and
-- "2026/02/18 14:00 EST" (WS5528666888-Doc5528694897). Same wall clock, label
-- following daylight saving -- so the field is a DST-AWARE EASTERN WALL CLOCK.
-- That contradicts the dataset's own documentation, which states closing times
-- are "displayed in Universal Time Coordinated minus 5 hours (UTC-0500)", a
-- fixed offset. The live notice is what bidders read and act on, so the zone
-- wins over the documentation; America/Toronto gives EDT in summer and EST in
-- winter, matching both observations.
--
-- NOTE the absence of a row for LACity_RAMP_OpenBids. Its closedate really is
-- UTC -- 28 of 28 rows that also carry a local clock in their own title matched
-- it at +0h -- and GenericJson/GenericCsv treat "no configured zone" as UTC, so
-- leaving it unconfigured keeps it correct. Do not add one.

SET NOCOUNT ON;

DECLARE @zone nvarchar(64) = N'America/Toronto';

MERGE opportunities.OpportunitySourceMappings AS target
USING (
    SELECT s.Id AS OpportunitySourceId, N'csv.timeZone' AS [Key], @zone AS ValueJson
    FROM opportunities.OpportunitySources s
    WHERE s.Name IN (N'CanadaBuys', N'CanadaBuysNew')
) AS src
    ON target.OpportunitySourceId = src.OpportunitySourceId
   AND target.[Key] = src.[Key]
WHEN MATCHED AND ISNULL(target.ValueJson, N'') <> src.ValueJson THEN
    UPDATE SET ValueJson = src.ValueJson, UpdatedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED BY TARGET THEN
    INSERT (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
    VALUES (src.OpportunitySourceId, src.[Key], src.ValueJson, sysdatetimeoffset());

SELECT s.Name, m.[Key], m.ValueJson
FROM opportunities.OpportunitySourceMappings m
JOIN opportunities.OpportunitySources s ON s.Id = m.OpportunitySourceId
WHERE m.[Key] = N'csv.timeZone'
ORDER BY s.Name;

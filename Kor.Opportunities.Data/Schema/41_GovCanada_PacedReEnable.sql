USE [KorOpportunitiesDb];
GO

DECLARE @ResourceId nvarchar(100) = N'fac950c0-00d5-4ec1-a4d3-9cbebf98a305';
DECLARE @ApiBase nvarchar(500) = N'https://open.canada.ca/data/api/3/action/datastore_search?resource_id=' + @ResourceId;
DECLARE @ConstructionBaseUrl nvarchar(2000) =
    @ApiBase + N'&filters={"commodity_type":"C"}';
DECLARE @EngineeringBaseUrl nvarchar(2000) =
    @ApiBase + N'&filters={"commodity_code":["R008","R008A","R019","R019E","C119F","C123","C123A","C129","C129A","C211","C211D","C219","C219A","C219BB","C219BK","C219C","B109","B109A","B219","B219A","E199C","81100000","81101500","81101508","81101513","81101515","81101606","81101701"]}';

UPDATE opportunities.OpportunitySources
SET    SourceType = 17, -- GenericJsonAward
       BaseUrl = @ConstructionBaseUrl,
       IsEnabled = 1,
       CrawlDelaySeconds = 86400,
       UpdatedAtUtc = sysdatetimeoffset()
WHERE  Name = N'GovCanada_Construction';

IF @@ROWCOUNT = 0
BEGIN
    PRINT 'Migration 41: GovCanada_Construction source missing - source settings not updated.';
END;

UPDATE opportunities.OpportunitySources
SET    SourceType = 17, -- GenericJsonAward
       BaseUrl = @EngineeringBaseUrl,
       IsEnabled = 1,
       CrawlDelaySeconds = 86400,
       UpdatedAtUtc = sysdatetimeoffset()
WHERE  Name = N'GovCanada_EngineeringServices';

IF @@ROWCOUNT = 0
BEGIN
    PRINT 'Migration 41: GovCanada_EngineeringServices source missing - source settings not updated.';
END;

DECLARE @Sources table
(
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    Name nvarchar(200) NOT NULL
);

INSERT INTO @Sources (Id, Name)
SELECT Id, Name
FROM opportunities.OpportunitySources
WHERE Name IN (N'GovCanada_Construction', N'GovCanada_EngineeringServices');

DELETE m
FROM opportunities.OpportunitySourceMappings m
JOIN @Sources s ON s.Id = m.OpportunitySourceId
WHERE m.[Key] = N'json.sqlQuery';

MERGE opportunities.OpportunitySourceMappings AS t
USING
(
    SELECT s.Id AS OpportunitySourceId, v.[Key], v.ValueJson
    FROM @Sources s
    CROSS JOIN
    (
        VALUES
            (N'json.pageSize',       N'1000'),
            (N'json.pageDelayMs',    N'1500'),
            (N'json.maxPagesPerRun', N'25'),
            (N'json.maxRowsPerRun',  N'25000')
    ) AS v([Key], ValueJson)
) AS src
ON t.OpportunitySourceId = src.OpportunitySourceId
AND t.[Key] = src.[Key]
WHEN MATCHED THEN
    UPDATE SET ValueJson = src.ValueJson,
               UpdatedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED THEN
    INSERT (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
    VALUES (src.OpportunitySourceId, src.[Key], src.ValueJson, sysdatetimeoffset());

PRINT 'Migration 41: Gov Canada construction + engineering sources re-enabled with paced JSON mappings.';
GO

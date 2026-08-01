USE [KorOpportunitiesDb];
GO

DECLARE @Sources table
(
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    Name nvarchar(200) NOT NULL
);

INSERT INTO @Sources (Id, Name)
SELECT Id, Name
FROM opportunities.OpportunitySources
WHERE Name IN (N'GovCanada_Construction', N'GovCanada_EngineeringServices');

IF NOT EXISTS (SELECT 1 FROM @Sources WHERE Name = N'GovCanada_Construction')
BEGIN
    PRINT 'Migration 42: GovCanada_Construction source missing - json.sort not updated.';
END;

IF NOT EXISTS (SELECT 1 FROM @Sources WHERE Name = N'GovCanada_EngineeringServices')
BEGIN
    PRINT 'Migration 42: GovCanada_EngineeringServices source missing - json.sort not updated.';
END;

MERGE opportunities.OpportunitySourceMappings AS t
USING
(
    SELECT Id AS OpportunitySourceId, N'json.sort' AS [Key], N'_id desc' AS ValueJson
    FROM @Sources
) AS src
ON t.OpportunitySourceId = src.OpportunitySourceId
AND t.[Key] = src.[Key]
WHEN MATCHED THEN
    UPDATE SET ValueJson = src.ValueJson,
               UpdatedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED THEN
    INSERT (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
    VALUES (src.OpportunitySourceId, src.[Key], src.ValueJson, sysdatetimeoffset());

PRINT 'Migration 42: Gov Canada JSON award sources sort newest-first by _id.';
GO

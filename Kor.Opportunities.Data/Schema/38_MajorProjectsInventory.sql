USE [KorOpportunitiesDb];
GO

IF OBJECT_ID(N'opportunities.MajorProjectsInventory', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.MajorProjectsInventory (
        Id                          bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Province                    nvarchar(2)    NOT NULL,
        SourceKey                   nvarchar(200)  NOT NULL,
        ProjectName                 nvarchar(500)  NOT NULL,
        Sector                      nvarchar(100)  NULL,
        SubSector                   nvarchar(100)  NULL,
        EstimatedCostCad            decimal(18,0)  NULL,
        EstimatedCostText           nvarchar(200)  NULL,
        Stage                       nvarchar(50)   NULL,
        ProponentName               nvarchar(500)  NULL,
        ProponentCanonicalOrgId     bigint         NULL,
        MunicipalityName            nvarchar(200)  NULL,
        RegionName                  nvarchar(200)  NULL,
        StartYear                   smallint       NULL,
        CompletionYear              smallint       NULL,
        ScheduleNotes               nvarchar(1000) NULL,
        SourceUrl                   nvarchar(1000) NULL,
        RawJson                     nvarchar(max)  NULL,
        FirstSeenAtUtc              datetimeoffset NOT NULL CONSTRAINT DF_MPI_FirstSeen DEFAULT sysdatetimeoffset(),
        LastSeenAtUtc               datetimeoffset NOT NULL CONSTRAINT DF_MPI_LastSeen DEFAULT sysdatetimeoffset(),
        UpdatedAtUtc                datetimeoffset NOT NULL CONSTRAINT DF_MPI_Updated DEFAULT sysdatetimeoffset(),
        CONSTRAINT UX_MPI_Province_SourceKey UNIQUE (Province, SourceKey),
        CONSTRAINT FK_MPI_ProponentCanonicalOrg FOREIGN KEY (ProponentCanonicalOrgId)
            REFERENCES opportunities.CanonicalOrg (Id)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MPI_Province_Stage' AND object_id = OBJECT_ID(N'opportunities.MajorProjectsInventory'))
BEGIN
    CREATE INDEX IX_MPI_Province_Stage
        ON opportunities.MajorProjectsInventory (Province, Stage);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MPI_ProponentCanonicalOrgId' AND object_id = OBJECT_ID(N'opportunities.MajorProjectsInventory'))
BEGIN
    CREATE INDEX IX_MPI_ProponentCanonicalOrgId
        ON opportunities.MajorProjectsInventory (ProponentCanonicalOrgId)
        WHERE ProponentCanonicalOrgId IS NOT NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MPI_CompletionYear' AND object_id = OBJECT_ID(N'opportunities.MajorProjectsInventory'))
BEGIN
    CREATE INDEX IX_MPI_CompletionYear
        ON opportunities.MajorProjectsInventory (CompletionYear)
        WHERE CompletionYear IS NOT NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MPI_LastSeen' AND object_id = OBJECT_ID(N'opportunities.MajorProjectsInventory'))
BEGIN
    CREATE INDEX IX_MPI_LastSeen
        ON opportunities.MajorProjectsInventory (LastSeenAtUtc DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM opportunities.OpportunitySources WHERE Name = N'AB_MajorProjectsInventory')
BEGIN
    INSERT INTO opportunities.OpportunitySources
        (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds, CreatedAtUtc, UpdatedAtUtc, IsHistorical)
    VALUES
        (NEWID(),
         N'AB_MajorProjectsInventory',
         18,
         N'https://open.alberta.ca/dataset/3e4efd44-7a00-46d1-9c7c-171028a01066/resource/b69a4239-06fb-4e02-b011-7de59ced8fa3/download/majorprojects.csv',
         1,
         86400,
         300,
         sysdatetimeoffset(),
         sysdatetimeoffset(),
         0);
END;
GO

GRANT SELECT, INSERT, UPDATE ON opportunities.MajorProjectsInventory TO opportunities_app;
GO

PRINT 'Migration 38: MajorProjectsInventory table and AB source seeded.';
GO

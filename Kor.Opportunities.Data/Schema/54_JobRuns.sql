IF OBJECT_ID(N'opportunities.JobRuns', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.JobRuns
    (
        Id bigint IDENTITY(1, 1) NOT NULL
            CONSTRAINT PK_JobRuns PRIMARY KEY,
        JobName nvarchar(100) NOT NULL,
        StartedAtUtc datetimeoffset NOT NULL
            CONSTRAINT DF_JobRuns_StartedAtUtc DEFAULT sysdatetimeoffset(),
        EndedAtUtc datetimeoffset NULL,
        Success bit NULL,
        Summary nvarchar(1000) NULL,
        Error nvarchar(2000) NULL
    );
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_JobRuns_JobName_StartedAtUtc'
      AND object_id = OBJECT_ID(N'opportunities.JobRuns')
)
BEGIN
    CREATE INDEX IX_JobRuns_JobName_StartedAtUtc
        ON opportunities.JobRuns (JobName, StartedAtUtc DESC);
END;
GO

PRINT '54_JobRuns complete';
GO

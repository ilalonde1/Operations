IF OBJECT_ID(N'opportunities.JobSchedules', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.JobSchedules
    (
        JobName nvarchar(100) NOT NULL
            CONSTRAINT PK_JobSchedules PRIMARY KEY,
        CronSchedule nvarchar(50) NULL,
        Enabled bit NOT NULL
            CONSTRAINT DF_JobSchedules_Enabled DEFAULT 1,
        UpdatedAtUtc datetimeoffset NOT NULL
            CONSTRAINT DF_JobSchedules_UpdatedAtUtc DEFAULT sysdatetimeoffset()
    );
END;
GO

PRINT '55_JobSchedules complete';
GO

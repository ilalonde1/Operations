-- Migration 57  Admin Control: triggerable jobs + grouping + report links
-- Adds the JobTriggerRequests queue table (WPF UI writes here; Worker poller drains)
-- and grows JobSchedules with Category + LastReportPath columns surfaced in the
-- admin grid.

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('opportunities.JobSchedules') AND name = 'Category')
BEGIN
  ALTER TABLE opportunities.JobSchedules ADD Category NVARCHAR(40) NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('opportunities.JobSchedules') AND name = 'LastReportPath')
BEGIN
  ALTER TABLE opportunities.JobSchedules ADD LastReportPath NVARCHAR(500) NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('opportunities') AND name = 'JobTriggerRequests')
BEGIN
  CREATE TABLE opportunities.JobTriggerRequests (
    Id              BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_JobTriggerRequests PRIMARY KEY,
    JobName         NVARCHAR(100) NOT NULL,
    RequestedAtUtc  DATETIMEOFFSET NOT NULL CONSTRAINT DF_JobTriggerRequests_RequestedAtUtc DEFAULT sysdatetimeoffset(),
    RequestedBy     NVARCHAR(100) NULL,
    Status          NVARCHAR(20) NOT NULL CONSTRAINT DF_JobTriggerRequests_Status DEFAULT 'Pending',
    ProcessedAtUtc  DATETIMEOFFSET NULL,
    ErrorMessage    NVARCHAR(MAX) NULL,
    CONSTRAINT CK_JobTriggerRequests_Status CHECK (Status IN ('Pending','Fired','Failed'))
  );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('opportunities.JobTriggerRequests') AND name = 'IX_JobTriggerRequests_Pending')
BEGIN
  CREATE INDEX IX_JobTriggerRequests_Pending
    ON opportunities.JobTriggerRequests (RequestedAtUtc)
    WHERE Status = 'Pending';
END
GO

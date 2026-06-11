-- 128_QueueRefreshReport.sql
-- Bridge table between the dev-box nightly batch trigger (Nightly-Refresh.ps1,
-- scheduled task "KOR BD Nightly Refresh" at 21:30) and the Worker's
-- BdMorningReportJob email. The trigger's report previously lived only in
-- a file on the dev box (refresh-report.txt) that the Worker on KOR-APP01
-- cannot read; each run now also inserts a row here, and the morning email
-- embeds the most recent one (pending queues + per-kind generation stats).
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID('opportunities.QueueRefreshReport', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.QueueRefreshReport
    (
        Id            bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_QueueRefreshReport PRIMARY KEY,
        RunAtUtc      datetimeoffset NOT NULL CONSTRAINT DF_QueueRefreshReport_RunAtUtc DEFAULT (sysdatetimeoffset()),
        PendingQueues nvarchar(400) NULL,   -- comma list of queues with work, '' when all fresh
        ReportText    nvarchar(max) NOT NULL -- full human-readable report (same content as refresh-report.txt)
    );

    CREATE INDEX IX_QueueRefreshReport_RunAtUtc ON opportunities.QueueRefreshReport (RunAtUtc DESC);
END
GO

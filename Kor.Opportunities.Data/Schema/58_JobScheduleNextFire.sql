-- Migration 58  Add NextFireAtUtc to JobSchedules so the admin grid
-- can show 'next run in Xm' without parsing Quartz cron client-side.

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('opportunities.JobSchedules') AND name = 'NextFireAtUtc')
BEGIN
  ALTER TABLE opportunities.JobSchedules ADD NextFireAtUtc DATETIMEOFFSET NULL;
END
GO

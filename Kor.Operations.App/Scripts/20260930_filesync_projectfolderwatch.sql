-- =============================================================================
-- ProjectFolderWatch -- nightly check that every project folder is where the firm
-- expects it (FileSync, KOR-APP01, 06:00 daily).
--
-- Why: on 2026-09-30 eleven project folders were found dragged INTO a neighbouring
-- project's folder (30459-02 inside 30427-05, ...). Nothing was deleted, but the
-- projects looked gone for months. This finds, every night:
--   - a project folder sitting directly inside another job's project folder, and
--   - a project folder the email index files into that no longer exists.
--
-- Read-only on the share and on KorEmailIndex. Live emails AlertTo only when
-- something NEW turns up; Shadow writes its report to
--   %ProgramData%\KorOperations\FileSync\ProjectFolderWatch\shadow-<stamp>.txt
-- and emails nobody. Seeded in SHADOW, like every new FileSync job.
-- =============================================================================
USE KorTransmittals;
GO

IF NOT EXISTS (SELECT 1 FROM FileSync.Jobs WHERE JobName = 'ProjectFolderWatch')
BEGIN
    INSERT INTO FileSync.Jobs (JobName, DisplayName, Mode, CronExpression, Notes)
    VALUES ('ProjectFolderWatch',
            'Project folders in the right place',
            'Shadow',
            '0 0 6 * * ?',
            'Nightly: flags a project folder dragged into another project, and any indexed project folder that has vanished. Read-only.');
END;
GO

IF NOT EXISTS (SELECT 1 FROM FileSync.JobKnobs WHERE JobName = 'ProjectFolderWatch' AND KnobName = 'AlertTo')
    INSERT INTO FileSync.JobKnobs (JobName, KnobName, KnobValue) VALUES ('ProjectFolderWatch', 'AlertTo', 'ilalonde@korstructural.com');
IF NOT EXISTS (SELECT 1 FROM FileSync.JobKnobs WHERE JobName = 'ProjectFolderWatch' AND KnobName = 'ProjectsRoot')
    INSERT INTO FileSync.JobKnobs (JobName, KnobName, KnobValue) VALUES ('ProjectFolderWatch', 'ProjectsRoot', '\\Kor-fs01\Projects\Projects');
GO

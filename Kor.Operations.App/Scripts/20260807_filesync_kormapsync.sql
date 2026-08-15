-- =============================================================================
-- KorMapSync -- register the Deltek -> korstructural.com map sync in the
-- FileSync control plane so it appears in the Command Center like every other
-- job (Shadow/Live toggle, cron, manual fire, run history).
--
-- Seeded in SHADOW. A Shadow run reads Deltek, works out exactly what it would
-- create/update, writes the plan to
--   %ProgramData%\KorOperations\FileSync\shadow\KorMapSync\<stamp>\
-- and pushes NOTHING. Review that, then flip Mode to Live in the Command Center.
--
-- SECRETS ARE NOT HERE. They are KOR_FILESYNC_* environment variables on
-- KOR-APP01, bound through FileSyncOptions exactly like the Graph and SQL
-- credentials. The job fails fast naming any that are missing.
--   Deltek creds:  \\KOR-APP01\C$\Program Files\KorOperations\Mcp\appsettings.Production.json -> DeltekOdbc
--   Mapbox token:  korstructural.com theme functions.php -> KOR_MAPBOX_TOKEN
--   Sync secret:   must match KOR_SYNC_SECRET defined in the same functions.php
-- =============================================================================

IF NOT EXISTS (SELECT 1 FROM FileSync.Jobs WHERE JobName = 'KorMapSync')
BEGIN
    INSERT INTO FileSync.Jobs (JobName, DisplayName, Mode, CronExpression, Notes)
    VALUES ('KorMapSync',
            'Sync project map to korstructural.com',
            'Shadow',
            '0 0 3 ? * *',
            'Reads Deltek (ODBC, read-only) on KOR-APP01, geocodes new addresses with city validation, and pushes finished rows to the website. The site holds no Deltek credentials and never geocodes.');
END;
GO

-- Non-secret configuration -----------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM FileSync.JobKnobs WHERE JobName = 'KorMapSync' AND KnobName = 'DeltekDsn')
    INSERT INTO FileSync.JobKnobs (JobName, KnobName, KnobValue) VALUES ('KorMapSync', 'DeltekDsn', 'Deltek');
IF NOT EXISTS (SELECT 1 FROM FileSync.JobKnobs WHERE JobName = 'KorMapSync' AND KnobName = 'DeltekCatalog')
    INSERT INTO FileSync.JobKnobs (JobName, KnobName, KnobValue) VALUES ('KorMapSync', 'DeltekCatalog', 'C0000052267P_1_KOR00000000');
IF NOT EXISTS (SELECT 1 FROM FileSync.JobKnobs WHERE JobName = 'KorMapSync' AND KnobName = 'DeltekUser')
    INSERT INTO FileSync.JobKnobs (JobName, KnobName, KnobValue) VALUES ('KorMapSync', 'DeltekUser', '52267.nucleus.prd');
IF NOT EXISTS (SELECT 1 FROM FileSync.JobKnobs WHERE JobName = 'KorMapSync' AND KnobName = 'WordPressBaseUrl')
    INSERT INTO FileSync.JobKnobs (JobName, KnobName, KnobValue) VALUES ('KorMapSync', 'WordPressBaseUrl', 'https://www.korstructural.com');

-- Cap geocoding per run so one bad batch cannot burn the Mapbox quota.
IF NOT EXISTS (SELECT 1 FROM FileSync.JobKnobs WHERE JobName = 'KorMapSync' AND KnobName = 'GeocodeBatchLimit')
    INSERT INTO FileSync.JobKnobs (JobName, KnobName, KnobValue) VALUES ('KorMapSync', 'GeocodeBatchLimit', '400');
IF NOT EXISTS (SELECT 1 FROM FileSync.JobKnobs WHERE JobName = 'KorMapSync' AND KnobName = 'PushChunkSize')
    INSERT INTO FileSync.JobKnobs (JobName, KnobName, KnobValue) VALUES ('KorMapSync', 'PushChunkSize', '250');

-- Secrets are NOT knobs. They are environment variables on KOR-APP01, the same
-- as every other FileSync credential (KOR_FILESYNC_CLIENTSECRET etc.):
--   KOR_FILESYNC_DELTEKUSER, KOR_FILESYNC_DELTEKPASSWORD,
--   KOR_FILESYNC_MAPBOXTOKEN, KOR_FILESYNC_KORSYNCSECRET
-- The job fails fast and names any that are missing.
DELETE FROM FileSync.JobKnobs
 WHERE JobName = 'KorMapSync'
   AND KnobName IN ('DeltekPassword', 'MapboxToken', 'WordPressSyncSecret', 'DeltekUser');
GO

SELECT JobName, DisplayName, Mode, CronExpression, Enabled FROM FileSync.Jobs WHERE JobName = 'KorMapSync';
SELECT KnobName, KnobValue FROM FileSync.JobKnobs WHERE JobName = 'KorMapSync' ORDER BY KnobName;
GO

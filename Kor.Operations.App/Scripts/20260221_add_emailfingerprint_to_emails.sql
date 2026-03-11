IF COL_LENGTH('dbo.Emails', 'EmailFingerprint') IS NULL
BEGIN
    ALTER TABLE dbo.Emails ADD EmailFingerprint varchar(64) NULL;
END;

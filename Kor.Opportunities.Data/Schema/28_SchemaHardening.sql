/*
    Kor.OpportunitiesDb migration 28.
    Schema hardening for Round 14c: pursuit checks, news mention multiplicity,
    permit-source naming cleanup, and stranded trigger reclaim tracking.
*/

DECLARE @badStageRows int;
SELECT @badStageRows = COUNT(*)
FROM opportunities.KorPursuits
WHERE Stage NOT IN ('Considering', 'Pursuing', 'Submitted', 'Won', 'Lost', 'Withdrawn', 'Declined');

UPDATE opportunities.KorPursuits
SET    Stage = 'Considering'
WHERE  Stage NOT IN ('Considering', 'Pursuing', 'Submitted', 'Won', 'Lost', 'Withdrawn', 'Declined');

PRINT CONCAT('KorPursuits invalid Stage rows normalized: ', @badStageRows);
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_KorPursuits_Stage')
BEGIN
    ALTER TABLE opportunities.KorPursuits
        ADD CONSTRAINT CK_KorPursuits_Stage
        CHECK (Stage IN ('Considering', 'Pursuing', 'Submitted', 'Won', 'Lost', 'Withdrawn', 'Declined'));
END;
GO

DECLARE @badRoleRows int;
SELECT @badRoleRows = COUNT(*)
FROM opportunities.KorPursuits
WHERE OurRole IS NOT NULL
  AND OurRole NOT IN ('Prime', 'Sub', 'JV', 'Support');

UPDATE opportunities.KorPursuits
SET    OurRole = NULL
WHERE  OurRole IS NOT NULL
  AND  OurRole NOT IN ('Prime', 'Sub', 'JV', 'Support');

PRINT CONCAT('KorPursuits invalid OurRole rows nulled: ', @badRoleRows);
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_KorPursuits_OurRole')
BEGIN
    ALTER TABLE opportunities.KorPursuits
        ADD CONSTRAINT CK_KorPursuits_OurRole
        CHECK (OurRole IS NULL OR OurRole IN ('Prime', 'Sub', 'JV', 'Support'));
END;
GO

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UX_NewsMention_ArticleOrg' AND parent_object_id = OBJECT_ID(N'opportunities.NewsArticleOrgMention'))
BEGIN
    ALTER TABLE opportunities.NewsArticleOrgMention
        DROP CONSTRAINT UX_NewsMention_ArticleOrg;
END;
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_NewsMention_ArticleOrg' AND object_id = OBJECT_ID(N'opportunities.NewsArticleOrgMention'))
BEGIN
    DROP INDEX UX_NewsMention_ArticleOrg ON opportunities.NewsArticleOrgMention;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'opportunities.NewsArticleOrgMention') AND name = 'MentionTypeKey')
BEGIN
    ALTER TABLE opportunities.NewsArticleOrgMention
        ADD MentionTypeKey AS ISNULL(MentionType, N'') PERSISTED;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_NewsMention_ArticleOrg_Type' AND object_id = OBJECT_ID(N'opportunities.NewsArticleOrgMention'))
BEGIN
    CREATE UNIQUE INDEX UX_NewsMention_ArticleOrg_Type
        ON opportunities.NewsArticleOrgMention (NewsArticleId, CanonicalOrgId, MentionTypeKey);
END;
GO

UPDATE opportunities.PermitSource
SET    Name = N'City of Vancouver — issued-building-permits'
WHERE  Name = N'City of Vancouver  issued-building-permits';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'opportunities.IngestionTriggers') AND name = 'ReclaimedCount')
BEGIN
    ALTER TABLE opportunities.IngestionTriggers
        ADD ReclaimedCount int NOT NULL CONSTRAINT DF_IngestionTriggers_ReclaimedCount DEFAULT 0;
END;
GO

PRINT 'Migration 28 complete.';
GO

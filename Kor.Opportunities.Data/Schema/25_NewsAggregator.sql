/*
    Kor.OpportunitiesDb migration 25.
    News-aggregator tables: feeds (sources), articles (polled), mentions
    (canonical-org links - populated by 12b classifier). Idempotent.
    Seeds 4 trade publications.
*/

-- Feeds: one row per RSS/Atom source
IF OBJECT_ID(N'opportunities.NewsFeed', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.NewsFeed (
        Id                bigint           IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name              nvarchar(120)    NOT NULL,
        FeedUrl           nvarchar(800)    NOT NULL,
        SiteUrl           nvarchar(800)    NULL,
        Region            nvarchar(40)     NULL,           -- 'CA-BC' | 'CA' | 'US' | etc.
        Discipline        nvarchar(40)     NULL,           -- 'construction' | 'architecture' | 'engineering' | etc.
        IsActive          bit              NOT NULL DEFAULT 1,
        LastPolledAtUtc   datetimeoffset   NULL,
        LastErrorMessage  nvarchar(1000)   NULL,
        CreatedAtUtc      datetimeoffset   NOT NULL DEFAULT sysdatetimeoffset()
    );

    CREATE UNIQUE INDEX UX_NewsFeed_FeedUrl ON opportunities.NewsFeed (FeedUrl);
END;
GO

-- Articles: one row per RSS item
IF OBJECT_ID(N'opportunities.NewsArticle', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.NewsArticle (
        Id                   bigint           IDENTITY(1,1) NOT NULL PRIMARY KEY,
        FeedId               bigint           NOT NULL,
        ExternalId           nvarchar(500)    NOT NULL,       -- RSS GUID, falls back to article URL
        Title                nvarchar(500)    NOT NULL,
        Url                  nvarchar(800)    NOT NULL,
        Author               nvarchar(200)    NULL,
        PublishedAtUtc       datetimeoffset   NULL,
        Summary              nvarchar(max)    NULL,
        Content              nvarchar(max)    NULL,
        Categories           nvarchar(max)    NULL,           -- JSON array
        ClassifiedAtUtc      datetimeoffset   NULL,           -- set by 12b
        ClassificationStatus nvarchar(20)     NOT NULL DEFAULT 'pending',  -- 'pending' | 'ok' | 'failed' | 'skipped'
        IngestedAtUtc        datetimeoffset   NOT NULL DEFAULT sysdatetimeoffset(),
        CONSTRAINT FK_NewsArticle_NewsFeed FOREIGN KEY (FeedId) REFERENCES opportunities.NewsFeed (Id)
    );

    CREATE UNIQUE INDEX UX_NewsArticle_FeedId_ExternalId
        ON opportunities.NewsArticle (FeedId, ExternalId);

    CREATE INDEX IX_NewsArticle_PublishedAt
        ON opportunities.NewsArticle (PublishedAtUtc DESC);

    CREATE INDEX IX_NewsArticle_PendingClassification
        ON opportunities.NewsArticle (FeedId, IngestedAtUtc)
        WHERE ClassificationStatus = 'pending';
END;
GO

-- Mentions: many-to-many between articles and canonical orgs (populated in 12b)
IF OBJECT_ID(N'opportunities.NewsArticleOrgMention', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.NewsArticleOrgMention (
        Id                bigint           IDENTITY(1,1) NOT NULL PRIMARY KEY,
        NewsArticleId     bigint           NOT NULL,
        CanonicalOrgId    bigint           NOT NULL,
        MentionType       nvarchar(40)     NULL,           -- 'project_win' | 'm_and_a' | 'hiring' | 'leadership' | 'award' | 'expansion' | 'other'
        Confidence        int              NOT NULL DEFAULT 50,  -- 0-100
        Excerpt           nvarchar(2000)   NULL,
        CreatedAtUtc      datetimeoffset   NOT NULL DEFAULT sysdatetimeoffset(),
        CONSTRAINT FK_NewsMention_Article FOREIGN KEY (NewsArticleId) REFERENCES opportunities.NewsArticle (Id),
        CONSTRAINT FK_NewsMention_CanonicalOrg FOREIGN KEY (CanonicalOrgId) REFERENCES opportunities.CanonicalOrg (Id)
    );

    CREATE UNIQUE INDEX UX_NewsMention_ArticleOrg
        ON opportunities.NewsArticleOrgMention (NewsArticleId, CanonicalOrgId);

    CREATE INDEX IX_NewsMention_CanonicalOrg
        ON opportunities.NewsArticleOrgMention (CanonicalOrgId, CreatedAtUtc DESC);
END;
GO

-- Seed 4 trade-pub feeds. Idempotent via FeedUrl unique index.
IF NOT EXISTS (SELECT 1 FROM opportunities.NewsFeed WHERE Name = 'Daily Commercial News')
BEGIN
    INSERT INTO opportunities.NewsFeed (Name, FeedUrl, SiteUrl, Region, Discipline)
    VALUES ('Daily Commercial News', 'https://canada.constructconnect.com/dcn/feed',
            'https://canada.constructconnect.com/dcn', 'CA', 'construction');
END;
GO

IF NOT EXISTS (SELECT 1 FROM opportunities.NewsFeed WHERE Name = 'Journal of Commerce')
BEGIN
    INSERT INTO opportunities.NewsFeed (Name, FeedUrl, SiteUrl, Region, Discipline)
    VALUES ('Journal of Commerce', 'https://canada.constructconnect.com/joc/feed',
            'https://canada.constructconnect.com/joc', 'CA-BC', 'construction');
END;
GO

IF NOT EXISTS (SELECT 1 FROM opportunities.NewsFeed WHERE Name = 'Canadian Architect')
BEGIN
    INSERT INTO opportunities.NewsFeed (Name, FeedUrl, SiteUrl, Region, Discipline)
    VALUES ('Canadian Architect', 'https://www.canadianarchitect.com/feed/',
            'https://www.canadianarchitect.com', 'CA', 'architecture');
END;
GO

IF NOT EXISTS (SELECT 1 FROM opportunities.NewsFeed WHERE Name = 'Building Magazine')
BEGIN
    INSERT INTO opportunities.NewsFeed (Name, FeedUrl, SiteUrl, Region, Discipline)
    VALUES ('Building Magazine', 'https://building.ca/feed/', 'https://building.ca', 'CA', 'construction');
END;
GO

PRINT 'Migration 25 complete.';
GO

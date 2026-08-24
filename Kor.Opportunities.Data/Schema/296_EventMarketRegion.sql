/*
    Kor.OpportunitiesDb migration 296.

    Fixes ingested events inheriting their source's blanket DefaultMarket: the
    ICBA source covers BC and AB, so "Meet the Generals - Victoria" landed with
    Market = 'British Columbia / Alberta'.

    City -> Market is now a lookup table rather than a constant in code, so an
    operator can add a city or retune a market with an UPDATE instead of a
    rebuild and redeploy. IndustryEventIngestService reads it on every run and
    falls back to the source's DefaultMarket when a city is unknown.

    Markets match KOR's four operating regions (per the firm's own signature
    block: Vancouver, Okanagan, Vancouver Island, Alberta), plus catch-alls for
    BC interior/north.

    Idempotent, including the seed.
*/

IF OBJECT_ID(N'opportunities.EventMarketRegion', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.EventMarketRegion
    (
        Id int IDENTITY(1, 1) NOT NULL
            CONSTRAINT PK_EventMarketRegion PRIMARY KEY,
        CityName nvarchar(200) NOT NULL,
        Market nvarchar(100) NOT NULL,
        CreatedAtUtc datetimeoffset NOT NULL
            CONSTRAINT DF_EventMarketRegion_CreatedAtUtc DEFAULT sysdatetimeoffset(),
        UpdatedAtUtc datetimeoffset NOT NULL
            CONSTRAINT DF_EventMarketRegion_UpdatedAtUtc DEFAULT sysdatetimeoffset()
    );

    CREATE UNIQUE INDEX UX_EventMarketRegion_CityName
        ON opportunities.EventMarketRegion (CityName);
END;
GO

-- Seed: guarded so re-running never disturbs an operator's edits.
MERGE opportunities.EventMarketRegion WITH (HOLDLOCK) AS target
USING
(
    VALUES
        (N'Vancouver',      N'Lower Mainland'),
        (N'Burnaby',        N'Lower Mainland'),
        (N'Surrey',         N'Lower Mainland'),
        (N'Richmond',       N'Lower Mainland'),
        (N'Coquitlam',      N'Lower Mainland'),
        (N'North Vancouver', N'Lower Mainland'),
        (N'Abbotsford',     N'Fraser Valley'),
        (N'Langley',        N'Fraser Valley'),
        (N'Chilliwack',     N'Fraser Valley'),
        (N'Victoria',       N'Vancouver Island'),
        (N'Nanaimo',        N'Vancouver Island'),
        (N'Duncan',         N'Vancouver Island'),
        (N'Courtenay',      N'Vancouver Island'),
        (N'Campbell River', N'Vancouver Island'),
        (N'Kelowna',        N'Okanagan'),
        (N'Vernon',         N'Okanagan'),
        (N'Penticton',      N'Okanagan'),
        (N'Kamloops',       N'British Columbia (Interior)'),
        (N'Prince George',  N'Northern BC'),
        (N'Calgary',        N'Alberta'),
        (N'Edmonton',       N'Alberta'),
        (N'Red Deer',       N'Alberta'),
        (N'Lethbridge',     N'Alberta')
) AS source (CityName, Market)
    ON target.CityName = source.CityName
WHEN NOT MATCHED THEN
    INSERT (CityName, Market)
    VALUES (source.CityName, source.Market);
GO

-- Backfill the ICBA rows ingested 2026-08-24 before this lookup existed.
UPDATE e
SET Market = m.Market,
    UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IndustryEvents e
JOIN opportunities.EventMarketRegion m
    ON m.CityName = e.City
WHERE e.IndustryEventSourceId IS NOT NULL
  AND e.City IS NOT NULL
  AND (e.Market IS NULL OR e.Market <> m.Market);
GO

PRINT '296_EventMarketRegion complete';
GO

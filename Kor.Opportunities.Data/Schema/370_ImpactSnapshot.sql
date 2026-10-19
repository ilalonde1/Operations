/*
    KorOpportunitiesDb migration 370: the impact scoreboard's daily snapshot (Ian, 18 October 2026).

    Ian wants to show, month by month, what changed in dollars: cash collected, DSO, old AR, fees won, projects recovered. Cash
    collected, invoicing, AR and DSO have history in Deltek (Kor.Operations.Financials.ImpactHistory). The rest exists only as a
    reading on the day, so the morning composer records it here once a day:

      opportunities.ImpactSnapshot   one row per (day, metric): AR over 60 and over 90 days, AR on inactive projects, critical
                                     and over-budget projects, CRM won/lost/open counts and fees, actions closed, meetings held.
                                     A month's change is the difference between two days; the first row is the baseline.

    Metric names are free text from the composer (ImpactMetrics in tools/MorningBrief), so a new measure needs no migration.
    Idempotent.
*/
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'opportunities.ImpactSnapshot', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.ImpactSnapshot (
        SnapshotDate DATE NOT NULL,
        Metric NVARCHAR(60) NOT NULL,
        Value FLOAT NOT NULL,
        RecordedAtUtc DATETIMEOFFSET NOT NULL CONSTRAINT DF_ImpactSnapshot_RecordedAt DEFAULT sysdatetimeoffset(),
        CONSTRAINT PK_ImpactSnapshot PRIMARY KEY CLUSTERED (SnapshotDate, Metric)
    );
END
GO

/*
    KorOpportunitiesDb migration 378: the actions an RFP review sets, kept with the review (21 October 2026).

    Ian: "make the results explicit and actionable". A review now reports what to do next: each action with who does it and
    by when. They are kept here as JSON (action, owner, due, why) and also written where people work: each becomes an ask
    on its owner's My day (opportunities.BdAsk, AskedBy 'Claude (RFP review run N)'), and the first becomes the pursuit's
    next step when the pursuit has none or its next step is overdue.

    Idempotent; GO-batched.
*/
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

IF COL_LENGTH(N'opportunities.PursuitReview', N'ActionsJson') IS NULL
    ALTER TABLE opportunities.PursuitReview ADD ActionsJson NVARCHAR(MAX) NOT NULL CONSTRAINT DF_PursuitReview_Actions DEFAULT N'[]';
GO

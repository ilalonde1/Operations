/*
    Kor.OpportunitiesDb migration 20.
    KOR's own pursuit tracker. Captures the act of pursuing an external opportunity.
    Idempotent.
*/
IF OBJECT_ID(N'opportunities.KorPursuits', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.KorPursuits (
        Id                       bigint           IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Stage                    nvarchar(40)     NOT NULL, -- 'Considering' | 'Pursuing' | 'Submitted' | 'Won' | 'Lost' | 'Withdrawn' | 'Declined'
        OpportunityId            bigint           NULL,     -- soft FK to opportunities.Opportunities
        HistoricalOpportunityId  bigint           NULL,     -- soft FK to opportunities.HistoricalOpportunities
        OpportunityAwardId       bigint           NULL,     -- soft FK to opportunities.OpportunityAwards
        SourceExternalRef        nvarchar(200)    NULL,     -- denormalized RFP solicitation # for cross-source joins
        BuyerCanonicalOrgId      bigint           NULL,     -- FK CanonicalOrg
        BuyerName                nvarchar(300)    NOT NULL, -- snapshot at capture time
        Title                    nvarchar(500)    NOT NULL, -- RFP title snapshot
        LostToCanonicalOrgId     bigint           NULL,     -- if Stage='Lost', who won (head-to-head)
        LostToName               nvarchar(300)    NULL,     -- snapshot
        BidFee                   decimal(19,2)    NULL,     -- KOR's submitted fee
        BidCurrency              char(3)          NOT NULL DEFAULT 'CAD',
        EstConstructionValue     decimal(19,2)    NULL,
        OurRole                  nvarchar(30)     NULL,     -- 'Prime' | 'Sub' | 'JV' | 'Support'
        KorBusinessDeveloper     nvarchar(100)    NULL,     -- Ian/Jim/JM/JB/etc.
        KorProposalManager       nvarchar(100)    NULL,
        PursuitOpenedDate        date             NULL,
        DueDate                  date             NULL,
        SubmittedDate            date             NULL,
        AwardDate                date             NULL,
        ClosedReason             nvarchar(120)    NULL,     -- 'Lost-Price' | 'Lost-Qualifications' | 'Won' | etc.
        Strengths                nvarchar(max)    NULL,
        Weaknesses               nvarchar(max)    NULL,
        Notes                    nvarchar(max)    NULL,
        CreatedByUser            nvarchar(100)    NULL,
        CreatedAtUtc             datetimeoffset   NOT NULL DEFAULT sysdatetimeoffset(),
        UpdatedAtUtc             datetimeoffset   NOT NULL DEFAULT sysdatetimeoffset(),
        CONSTRAINT FK_KorPursuits_BuyerCanonical
            FOREIGN KEY (BuyerCanonicalOrgId) REFERENCES opportunities.CanonicalOrg (Id),
        CONSTRAINT FK_KorPursuits_LostToCanonical
            FOREIGN KEY (LostToCanonicalOrgId) REFERENCES opportunities.CanonicalOrg (Id)
    );

    CREATE INDEX IX_KorPursuits_Stage ON opportunities.KorPursuits (Stage);
    CREATE INDEX IX_KorPursuits_BuyerCanonical ON opportunities.KorPursuits (BuyerCanonicalOrgId) WHERE BuyerCanonicalOrgId IS NOT NULL;
    CREATE INDEX IX_KorPursuits_LostTo ON opportunities.KorPursuits (LostToCanonicalOrgId) WHERE LostToCanonicalOrgId IS NOT NULL;
    CREATE INDEX IX_KorPursuits_OpportunityId ON opportunities.KorPursuits (OpportunityId) WHERE OpportunityId IS NOT NULL;
    CREATE INDEX IX_KorPursuits_Historical ON opportunities.KorPursuits (HistoricalOpportunityId) WHERE HistoricalOpportunityId IS NOT NULL;
    CREATE INDEX IX_KorPursuits_Award ON opportunities.KorPursuits (OpportunityAwardId) WHERE OpportunityAwardId IS NOT NULL;
END;
GO

PRINT 'Migration 20 complete.';
GO

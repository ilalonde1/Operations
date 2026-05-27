USE [KorOpportunitiesDb];
GO

/* =====================================================================
   Stale-opp expiry.
   ---------------------------------------------------------------------
   1) PrimePipeline view: an open prime RFP must be non-terminal (not Won/Lost)
      AND its submission deadline must not have passed. This is read-time and
      SELF-MAINTAINING — opps drop out automatically as deadlines pass, with no
      recurring job (the MPI pipeline side is pre-tender, unchanged).
   2) One-time expiry: New(1) opps whose deadline has passed -> Lost(7) with
      WonLostOutcome=NoBid(3) (accurate: deadline passed, no bid) + an audit
      reason. Leaves human-advanced opps (Pursuing/Submitted/Won/Lost) untouched.
      78 such rows at authoring time.

   Status enum: New=1, Pursuing=4, Submitted=5, Won=6, Lost=7.
   WonLostOutcome: Won=1, Lost=2, NoBid=3, Withdrawn=4.
   ===================================================================== */
CREATE OR ALTER VIEW opportunities.PrimePipeline AS
SELECT
    CAST('Open RFP' AS nvarchar(20))            AS PipelineType,
    CAST(o.Id AS nvarchar(40))                   AS SourceRef,
    CAST(o.Name AS nvarchar(500))                AS ProjectName,
    CAST(o.BuyerName AS nvarchar(500))           AS BuyerOrOwner,
    CAST(o.PrimeProjectSector AS nvarchar(120))  AS Sector,
    CAST(o.Status AS nvarchar(120))              AS Stage,
    CAST(o.EstimatedValue AS decimal(18,2))      AS EstimatedValueCad,
    CAST(o.ProjectProvince AS nvarchar(40))      AS Province,
    CAST(o.ProjectCity AS nvarchar(200))         AS City,
    CAST(NULL AS nvarchar(500))                  AS ArchitectName,
    CAST(NULL AS nvarchar(1000))                 AS SourceUrl
FROM opportunities.Opportunities o
WHERE o.IsPrimeConsultantRfp = 1
  AND o.Status NOT IN (6, 7)   -- exclude Won / Lost (terminal)
  AND (o.SubmissionDeadlineUtc IS NULL OR o.SubmissionDeadlineUtc >= SYSDATETIMEOFFSET())

UNION ALL

SELECT
    CAST('Pipeline Project' AS nvarchar(20)),
    CAST(m.Id AS nvarchar(40)),
    CAST(m.ProjectName AS nvarchar(500)),
    CAST(m.ProponentName AS nvarchar(500)),
    CAST(m.Sector AS nvarchar(120)),
    CAST(m.Stage AS nvarchar(120)),
    CAST(m.EstimatedCostCad AS decimal(18,2)),
    CAST(m.Province AS nvarchar(40)),
    CAST(m.MunicipalityName AS nvarchar(200)),
    CAST(m.ArchitectName AS nvarchar(500)),
    CAST(m.SourceUrl AS nvarchar(1000))
FROM opportunities.MajorProjectsInventory m
WHERE m.Province IN ('BC','AB','CA','WA','OR')
  AND (
        m.Sector LIKE '%school%' OR m.Sector LIKE '%hospital%' OR m.Sector LIKE '%health%'
     OR m.Sector LIKE '%recreation%' OR m.Sector LIKE '%civic%' OR m.Sector LIKE '%cultural%'
     OR m.Sector LIKE '%universit%' OR m.Sector LIKE '%college%' OR m.Sector LIKE '%library%'
     OR m.Sector LIKE '%communit%' OR m.Sector LIKE '%education%' OR m.Sector LIKE '%housing%'
     OR m.Sector LIKE '%institution%' OR m.Sector LIKE '%care%'
     OR m.Sector IN ('Civic','Tourism / Recreation','Government','Mixed-use')
      )
  AND (m.Stage IS NULL OR NOT (
        m.Stage LIKE '%complet%' OR m.Stage LIKE 'construction%' OR m.Stage LIKE '%under construction%'
     OR m.Stage LIKE '%construction started%' OR m.Stage LIKE '%in construction%'
     OR m.Stage LIKE '%construction phase%' OR m.Stage LIKE '%in-service%' OR m.Stage LIKE '%in service%'
     OR m.Stage LIKE '%operating%' OR m.Stage LIKE '%occupancy%' OR m.Stage LIKE '%built%'
     OR m.Stage LIKE '%in progress%' OR m.Stage LIKE '%underway%' OR m.Stage LIKE '%demolition%'
      ));
GO

-- One-time expiry of past-deadline New opps (auto-ingested, never human-touched).
UPDATE opportunities.Opportunities
SET Status         = 7,          -- Lost
    WonLostOutcome = 3,          -- NoBid
    OutcomeReason  = N'Auto-expired: submission deadline passed (no bid).',
    OutcomeAtUtc   = SYSDATETIMEOFFSET(),
    UpdatedAtUtc   = SYSDATETIMEOFFSET()
WHERE Status = 1
  AND SubmissionDeadlineUtc IS NOT NULL
  AND SubmissionDeadlineUtc < SYSDATETIMEOFFSET();
GO

PRINT 'Migration 50: PrimePipeline open-prime guard + one-time past-deadline expiry (New -> Lost/NoBid).';
GO

/*
  289_OrgFact.sql  (2026-07-17)

  CRM Neural Gap Register G4: typed, queryable org facts. Decompose passes
  were appending dated prose to CanonicalOrg.Notes — human-readable, machine-
  opaque. OrgFact makes the same knowledge SELECT-able ("orgs that self-
  perform structural", "warm channels opened this month") for /ask, the
  dossier engine, and the weekly sheet. Notes stays as the narrative surface;
  facts are the neurons.

  FactType closed vocabulary (CHECK) — extend by migration, not ad hoc:
    SelfPerformsStructural  org has in-house structural (competitor signal)
    WarmChannel             a live relationship route into the org
    DeliveryModel           how they procure/deliver (DB/CM/IPD/P3 posture)
    CompetitorNote          competitive posture/marketshare intel
    DeltekLink              tie to KOR's books (client id, shared history)
    DuplicateOf             org-graph hygiene marker (pending/duplicate merge)
    MarketFocus             sectors/geographies the org actually plays in
    RiskNote                litigation/reputation/delivery risk on record

  NaturalKey = SHA1(OrgId|FactType|normalized-body-head) → decompose re-runs
  upsert. Supersede, retire — never overwrite history.

  Backfill: the hand-authored dated Notes blocks from the 2026-07-16/17
  decompose passes (Arcadis 153, Ledcor Construction 69671, DIALOG 6154),
  re-expressed as typed facts by the author of those blocks.
*/

IF OBJECT_ID(N'opportunities.OrgFact', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.OrgFact (
        Id                 BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        NaturalKey         CHAR(40)        NOT NULL,
        CanonicalOrgId     BIGINT          NOT NULL
            CONSTRAINT FK_OrgFact_Org REFERENCES opportunities.CanonicalOrg (Id),
        FactType           NVARCHAR(30)    NOT NULL
            CONSTRAINT CK_OrgFact_Type CHECK (FactType IN
                (N'SelfPerformsStructural', N'WarmChannel', N'DeliveryModel', N'CompetitorNote',
                 N'DeltekLink', N'DuplicateOf', N'MarketFocus', N'RiskNote')),
        Body               NVARCHAR(MAX)   NOT NULL,
        SourceUrl          NVARCHAR(400)   NULL,
        SourceRef          NVARCHAR(400)   NULL,   -- msg subject / dossier path / ledger query
        ObservedAtUtc      DATETIMEOFFSET  NOT NULL, -- when the fact was true/observed
        Confidence         NVARCHAR(10)    NOT NULL
            CONSTRAINT CK_OrgFact_Confidence CHECK (Confidence IN (N'High', N'Medium', N'Low')),
        SupersededByFactId BIGINT          NULL
            CONSTRAINT FK_OrgFact_SupersededBy REFERENCES opportunities.OrgFact (Id),
        CreatedAtUtc       DATETIMEOFFSET  NOT NULL CONSTRAINT DF_OrgFact_Created DEFAULT sysdatetimeoffset(),
        CreatedBy          NVARCHAR(100)   NOT NULL,
        RetiredAtUtc       DATETIMEOFFSET  NULL,
        RetiredReason      NVARCHAR(200)   NULL,
        CONSTRAINT UQ_OrgFact_NaturalKey UNIQUE (NaturalKey)
    );
    CREATE INDEX IX_OrgFact_OrgType ON opportunities.OrgFact (CanonicalOrgId, FactType) WHERE RetiredAtUtc IS NULL;
END
GO

SET QUOTED_IDENTIFIER ON;
GO
DECLARE @now datetimeoffset = sysdatetimeoffset();
DECLARE @by nvarchar(100) = N'BrainDecompose-2026-07-17';

DECLARE @f TABLE (OrgId bigint, FType nvarchar(30), Body nvarchar(max), SrcUrl nvarchar(400), SrcRef nvarchar(400), Obs datetimeoffset, Conf nvarchar(10));
INSERT @f (OrgId, FType, Body, SrcUrl, SrcRef, Obs, Conf) VALUES
-- ---- Arcadis IBI Group (153) -------------------------------------------
 (153, N'WarmChannel',
  N'Omar Alcazar (KOR) <-> Terry Gray, Assoc Principal / Market Leader Architecture Gov&Civic Canada, Edmonton (terry.gray@arcadis.com, Apollo+Hunter 100). Met at Ledcor client event 2026-06-24; warm thread Jun-Jul 2026; Jim DesRoches endorsed partnering. Plays: Alberta joint BD; Mexico City intro (Terry digging up Arcadis Mexico names).',
  NULL, N'msg: RE KOR/Arcadis Edmonton and Mexico Collaboration', '2026-07-15', N'High'),
 (153, N'SelfPerformsStructural',
  N'Arcadis runs an in-house Building Structural Group in Western Canada — actively hiring APEGA-registered senior structural P.Eng.s in Calgary (2026). Competitor on some pursuits; KOR lane = overflow / Edmonton-specific / seismic-heritage specialty. Confirm project routing with Terry, never assume the sub role.',
  N'https://jobs.arcadis.com/careers/job/563671518523230', N'Arcadis dossier research 2026-07-15', '2026-07-15', N'High'),
 (153, N'DeltekLink',
  N'Deltek client CL00483 (Station Square Site 6 BMO Build-Out, 2022, direct). Shared history rides DEVELOPER clients: 11 KOR portfolio projects with IBI/Arcadis as architect, 9 Deltek-reconciled (~$3.6M base fees, 14k+ logged hrs): SOCO 5 phases ~$1.53M; South Yards 30867+31100 $681k; The Grand 30958 $373.8k; Kings Crossing 30589 $550k+; One Burrard 30500; West Pender 30259; 564 Beatty 90064; Park Point 30614; Sovereign 30370. Unreconciled: Parkside-Calgary, Avalon 3.',
  NULL, N'Deltek ODBC reconciliation 2026-07-16', '2026-07-16', N'High'),
 (153, N'MarketFocus',
  N'Post-IBI-acquisition (Sept 2022) Arcadis carries IBI''s Vancouver residential/mixed-use architecture book (Anthem, Cressey, Reliance, Qualex relationships) plus Gov&Civic Canada practice (Edmonton: Terry Gray, Brad Kimball). Alberta civic pipeline publicly thin as of Jul 2026 — real pipeline likely private; ask Terry.',
  NULL, N'Arcadis dossier research 2026-07-15', '2026-07-15', N'Medium'),
-- ---- Ledcor Construction (69671) ---------------------------------------
 (69671, N'WarmChannel',
  N'Omar Alcazar (KOR) met Barry Murphy (Director of BD) at Ledcor client event 2026-06-24; Barry introduced Elliot Wood (Manager BD, Kelowna — elliot.wood@ledcor.com, Apollo+Hunter 90) in under 24h. First meeting: Jim DesRoches @ Ledcor Kelowna (Landmark 4) Mon 2026-07-20 10:00. CRM engagement 375.',
  NULL, N'msg: RE: Introduction to Ledcor Kelowna', '2026-07-17', N'High'),
 (69671, N'DeliveryModel',
  N'Ledcor picks the structural engineer ONLY on design-build / ECI work; on GC/CM the owner or architect holds the SE seat; there is NO consultant roster to register on — relationship entry via BD/precon is the sole path. UBCO Downtown (marquee Kelowna job) is CM-AS-AGENT for UBC Properties Trust, so that SE seat is owner-held.',
  NULL, N'KOR-Ledcor-Dossier-2026-07-01 + Kelowna research 2026-07-17', '2026-07-17', N'High'),
 (69671, N'MarketFocus',
  N'Kelowna branch (#700-1628 Dickson Ave) serves Okanagan + Interior BC (Kootenays + North). Focus: institutional, industrial, commercial. Clients: UBC(O), Interior Health, smaller municipalities, "more recently BC Housing" (Elliot Wood''s own words). Live marquee: UBCO Downtown 43-storey ~$262M (slipping to ~2028). Also Stratosphere Business Park w/ Beedie (5th Ledcor-Beedie job).',
  NULL, N'msg: RE: Introduction to Ledcor Kelowna + web research 2026-07-17', '2026-07-17', N'High'),
 (69671, N'CompetitorNote',
  N'Bird Construction is winning the Interior''s IPD civic work: Parkinson Rec Centre $242M (Kelowna, team locked Apr 2025) and Kamloops Arena Multiplex $140M (team locked Mar 2026) are both Bird-led. Ledcor lost or sat out both — a competitor problem KOR can empathize with in the room.',
  NULL, N'Okanagan pipeline research 2026-07-17', '2026-07-17', N'High'),
 (69671, N'RiskNote',
  N'UBCO Downtown has shoring/ground-movement trouble: damage to neighbouring buildings, a class action, schedule slip 2027 -> ~2028. Know, don''t raise — let Ledcor bring it up.',
  N'https://www.westerninvestor.com/british-columbia/cracks-showing-as-ubco-undertakes-ambitious-43-storey-kelowna-tower-8039202', N'Okanagan pipeline research 2026-07-17', '2026-07-17', N'Medium'),
-- ---- DIALOG (6154) ------------------------------------------------------
 (6154, N'SelfPerformsStructural',
  N'In-house structural since the Jones Kwong Kishi (JKK) merger 2013-10-14; self-performs in Calgary/Vancouver/Edmonton/Toronto studios. Named structural partners: Ralph Hildenbrandt (Calgary), Mehrak Razavi (Vancouver), Steven Oosterhof + Neil Robson (Edmonton). Proprietary Hybrid Timber Floor System (mass timber). Was architect of record AND structural engineer on Rogers Place (ICE District).',
  NULL, N'DIALOG dossier research 2026-07-14', '2026-07-14', N'High'),
 (6154, N'CompetitorNote',
  N'VERDICT: competitor, NOT a route to structural seats — do not pitch as sub on DIALOG-prime work. The ONE KOR angle: DIALOG has NO Vancouver Island / BC Interior studio (KOR primary markets) — possible local structural EOR / boots-on-ground play there only.',
  NULL, N'DIALOG dossier research 2026-07-14', '2026-07-14', N'High');

MERGE opportunities.OrgFact WITH (HOLDLOCK) AS T
USING (SELECT OrgId, FType, Body, SrcUrl, SrcRef, Obs, Conf,
              CONVERT(char(40), HASHBYTES('SHA1', CAST(
                CAST(OrgId AS varchar(20)) + '|' + FType + '|' +
                LOWER(REPLACE(LEFT(Body, 120), ' ', '')) AS varchar(8000))), 2) AS NK
       FROM @f) AS S ON T.NaturalKey = S.NK
WHEN MATCHED THEN UPDATE SET Body = S.Body, ObservedAtUtc = S.Obs, Confidence = S.Conf
WHEN NOT MATCHED THEN INSERT
   (NaturalKey, CanonicalOrgId, FactType, Body, SourceUrl, SourceRef, ObservedAtUtc, Confidence, CreatedBy)
   VALUES (S.NK, S.OrgId, S.FType, S.Body, S.SrcUrl, S.SrcRef, S.Obs, S.Conf, @by);

SELECT FactsBanked = COUNT(*) FROM opportunities.OrgFact WHERE RetiredAtUtc IS NULL;
GO

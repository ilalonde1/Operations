USE [KorOpportunitiesDb];
GO

/* =====================================================================
   172 — Post-ingest decomposition + dedup of the composite/variant orgs
   the BC MPI provider minted (raw DataBC DEVELOPER/ARCHITECT strings the
   resolver couldn't split). Surgical, reviewed pairs — no fuzzy guessing.

   MERGE (variant/word-order dupes → existing canonical): repoint MPI links
   + aliases to survivor, retire loser.
     76716 -> 76371  (BC Housing/Lookout — word-order variant)
     76685 -> 76391  (Providence Living/Northern Health — spacing variant)
     76680 -> 142    (Capital Region Housing Corporation)
     76674 -> 72211  (PHSA (Incl. BCCSS))
   DECOMPOSE (architect JV -> actionable Prime constituent):
     76742 -> 69688  (Herzog & de Meuron AND Perkins+Will -> Perkins+Will)
     76729 -> 153    (IBI Architects AND Integra -> Arcadis IBI)
     76711 -> 54190  (JYOM/GBL composite, 0 refs -> GBL, retire)
   OUT-OF-LANE: 76730 (Nisga'a/Rockies LNG/Western LNG) — energy/LNG, not a
     building; retire the org AND the MPI project (relevance-gate miss).
   KEEP+RECLASSIFY: 76718 (BC Housing / Vancouver Aboriginal Friendship Soc)
     is a legit BC-Housing partnership owner (matches existing convention) —
     just set Kind Unknown->Buyer.
   ===================================================================== */
SET NOCOUNT ON;
DECLARE @now DATETIMEOFFSET = SYSDATETIMEOFFSET();

IF OBJECT_ID('tempdb..#m') IS NOT NULL DROP TABLE #m;
CREATE TABLE #m (Loser BIGINT, Survivor BIGINT);
INSERT INTO #m VALUES (76716,76371),(76685,76391),(76680,142),(76674,72211),(76742,69688),(76729,153),(76711,54190);

/* repoint MPI proponent + architect links to survivors */
UPDATE mp SET mp.ProponentCanonicalOrgId = m.Survivor, mp.UpdatedAtUtc=@now
FROM opportunities.MajorProjectsInventory mp JOIN #m m ON m.Loser = mp.ProponentCanonicalOrgId;
UPDATE mp SET mp.ArchitectCanonicalOrgId = m.Survivor, mp.UpdatedAtUtc=@now
FROM opportunities.MajorProjectsInventory mp JOIN #m m ON m.Loser = mp.ArchitectCanonicalOrgId;

/* repoint aliases so the composite string resolves to the survivor next time */
UPDATE a SET a.CanonicalOrgId = m.Survivor
FROM opportunities.OrgAlias a JOIN #m m ON m.Loser = a.CanonicalOrgId
WHERE NOT EXISTS (SELECT 1 FROM opportunities.OrgAlias a2 WHERE a2.RawName=a.RawName AND a2.Source=a.Source AND a2.CanonicalOrgId=m.Survivor);

/* retire the losers */
UPDATE co SET co.RetiredAtUtc=@now,
   co.RetiredReason=N'Merged/decomposed into ' + CAST(m.Survivor AS NVARCHAR(20)) + N' — BC MPI composite cleanup (migration 172)',
   co.UpdatedAtUtc=@now
FROM opportunities.CanonicalOrg co JOIN #m m ON m.Loser = co.Id WHERE co.RetiredAtUtc IS NULL;

/* out-of-lane LNG: retire the project then the org */
UPDATE opportunities.MajorProjectsInventory SET RetiredAtUtc=@now,
   RetiredReason=N'Out-of-lane (LNG/energy, not a building) — migration 172', UpdatedAtUtc=@now
WHERE ProponentCanonicalOrgId=76730 AND RetiredAtUtc IS NULL;
UPDATE opportunities.CanonicalOrg SET RetiredAtUtc=@now,
   RetiredReason=N'Out-of-lane LNG composite — migration 172', UpdatedAtUtc=@now
WHERE Id=76730 AND RetiredAtUtc IS NULL;

/* keep BC Housing/VAFCS as a legit owner — fix Kind only */
UPDATE opportunities.CanonicalOrg SET Kind=N'Buyer', UpdatedAtUtc=@now
WHERE Id=76718 AND Kind=N'Unknown' AND RetiredAtUtc IS NULL;

DROP TABLE #m;
GO

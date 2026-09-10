SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

-- Retire the WarmChannel fact written this morning: it said "nine projects" and
-- named three Harry Jerome phases as work done. Verified against billing, seven
-- were billed and two Harry Jerome phases have never been invoiced at all.
UPDATE opportunities.OrgFact
SET RetiredAtUtc = SYSUTCDATETIME(),
    RetiredReason = N'Superseded 2026-09-10: counted project records, not billed work. Replaced by the billed-only version.'
WHERE CanonicalOrgId = 69688 AND FactType = N'WarmChannel'
  AND CreatedBy = N'BrainDecompose-2026-09-10' AND RetiredAtUtc IS NULL;

INSERT INTO opportunities.OrgFact
    (NaturalKey, CanonicalOrgId, FactType, Body, SourceUrl, SourceRef, ObservedAtUtc, Confidence, CreatedAtUtc, CreatedBy)
SELECT CONVERT(varchar(40), HASHBYTES('SHA1', '69688|WarmChannel|billed-verified|PW-2026-09-10b'), 2),
 69688, N'WarmChannel',
 N'⭐ KOR AND PERKINS&WILL: TEN project records, SEVEN BILLED, $536,544 total - tested the same way as the Island file (a record is not a job unless money or time moved). Only ONE Perkins entity exists in Deltek (CL00499 "Perkins + Will"), so there is no Perkins Eastman confusion in these figures. THE FOUR THAT MATTER, 98% of the money: River District Parcel 16.2 (Wesgroup) fee $235,000 / billed $236,125 · 600 Robson St (Bonnis) $140,000 / $120,460 · 900 Granville St (Bonnis) $92,500 / $91,770 · 5696 Alberta St (Nicola Wealth) $90,000 / $76,146. Thin: Harry Jerome Ph1 M4 (Darwin) $97,000 fee but only $9,840 billed; 357-475 West 41st (Coromandel) $142,000 fee but only $1,700; Harry Jerome Ph1 signage $500. ⚠ HARRY JEROME PHASES 2 AND 3 CARRY $430,000 OF SIGNED CONTRACT AND HAVE BILLED NOTHING - no invoices, no labour. Ask what happened; do not claim it as work done. ⛔ P&W HAS NEVER BEEN OUR PAYING CLIENT - all seven were invoiced to the DEVELOPER; the one record under their own account (90068-01 ICBC Head Office North Vancouver) has no fee, no time, no invoice. Say "we have worked alongside you", never "you have been our client". All opened 2018-19; nothing since.',
 NULL, N'Perkins&Will brief for Omar Alcazar, verified 2026-09-10',
 CAST('2026-09-10' AS datetimeoffset), N'High', SYSUTCDATETIME(), N'BrainDecompose-2026-09-10'
WHERE NOT EXISTS (SELECT 1 FROM opportunities.OrgFact e WHERE e.CanonicalOrgId = 69688
                  AND e.NaturalKey = CONVERT(varchar(40), HASHBYTES('SHA1','69688|WarmChannel|billed-verified|PW-2026-09-10b'),2)
                  AND e.RetiredAtUtc IS NULL);

INSERT INTO opportunities.OrgFact
    (NaturalKey, CanonicalOrgId, FactType, Body, SourceUrl, SourceRef, ObservedAtUtc, Confidence, CreatedAtUtc, CreatedBy)
SELECT CONVERT(varchar(40), HASHBYTES('SHA1', '69688|RiskNote|stale-titles|PW-2026-09-10b'), 2),
 69688, N'RiskNote',
 N'⛔ OUR VANCOUVER TITLES WERE A DECADE STALE. Verified against perkinswill.com/studio/vancouver on 2026-09-10: the Managing Director is DEREK NEWBY (also Calgary, and Regional Managing Director for Canada). Our records still carried SUSAN GUSHE and DAVID DOVE as "Managing Director, Vancouver" - Gushe held it after Peter Busby moved to San Francisco, which was 2012 - and JIM HUFFMAN at an old title. None of the three appears on the current studio leadership page. PETER BUSBY is still listed as "Principal, Vancouver" on his own person page, so that record is good. MISSING FROM OUR DATA ENTIRELY, all on the current leadership page: Shauna Bryce (Cultural and Civic), Jeff Doble (Transportation), Kerri Henderson (Advisory Services). Current leadership: Newby MD, Busby Principal, Jana Foit Operations Director, Adrian Watson Design Director, Ryan Bragg Corporate and Commercial, Bryce, Doble, Henderson, Jason LeBlanc Urban Design, Max Richter Sports Recreation and Entertainment, Rufina Wu Interior Design Director. Studio: 1075 West Georgia St Suite 2200, +1 604 684 5446. ⚠ We hold 35 DISTINCT PEOPLE at P&W, not 66 - 66 was the affiliation-row count, the same records-not-things error as the Deltek job counts.',
 N'https://perkinswill.com/studio/vancouver/', N'Perkins&Will brief for Omar Alcazar, verified 2026-09-10',
 CAST('2026-09-10' AS datetimeoffset), N'High', SYSUTCDATETIME(), N'BrainDecompose-2026-09-10'
WHERE NOT EXISTS (SELECT 1 FROM opportunities.OrgFact e WHERE e.CanonicalOrgId = 69688
                  AND e.NaturalKey = CONVERT(varchar(40), HASHBYTES('SHA1','69688|RiskNote|stale-titles|PW-2026-09-10b'),2)
                  AND e.RetiredAtUtc IS NULL);

SELECT 'live facts on Perkins&Will now' AS Section;
SELECT FactType, LEFT(Body, 60) AS Body FROM opportunities.OrgFact
WHERE CanonicalOrgId = 69688 AND RetiredAtUtc IS NULL ORDER BY FactType, Id;

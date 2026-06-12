-- 132_CompetitorKindHygiene.sql
-- honing-competitors batch-003 flagged ~35 misclassified records; the full
-- evidence scan over ALL competitor hone briefs found 125 firms whose own
-- researched narrative states a non-structural discipline (electrical,
-- geotechnical, mechanical/M&E, manufacturers, building science) with no
-- structural practice. They are NOT competitors: Kind -> Unknown +
-- enrichment suppressed (research rows preserved). MNA (11923) excluded —
-- 'Structural' appears in its own name. Several are flagged 'potential
-- teaming partners' in the hone summaries; a teaming-partner taxonomy is
-- future work — suppression reason carries the breadcrumb.
SET XACT_ABORT ON;
GO
BEGIN TRAN;
UPDATE co SET Kind = N'Unknown',
  EnrichmentSuppressedAtUtc = COALESCE(co.EnrichmentSuppressedAtUtc, sysdatetimeoffset()),
  EnrichmentSuppressedReason = COALESCE(co.EnrichmentSuppressedReason, N'm132: allied-discipline (non-structural) — was misclassified Competitor; teaming-partner taxonomy TBD'),
  UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.CanonicalOrg co
WHERE co.Id IN (10076,10086,1063,10727,10914,11222,11418,11507,11508,11533,11534,11671,11823,12641,12649,12940,12941,13030,13240,13382,13477,13565,14089,14320,14676,14768,15071,15159,15177,15323,15459,15535,15548,1601,16354,16659,16692,1711,1741,17411,17509,1769,17713,1775,1781,17896,17919,18101,18398,18802,2134,2137,2138,22508,2257,2396,2498,2499,2617,2685,3008,3027,3035,3036,3085,3116,3118,3737,3831,3832,3848,44919,45287,46654,46908,47388,47701,4937,49400,4978,4979,5007,50448,50537,52063,55850,5609,5741,5747,6096,61179,63310,63323,63377,63381,63444,63830,6596,66214,66251,66823,6769,6841,69519,69595,70074,70416,71876,71947,71958,72021,72299,72414,7344,73916,7459,8225,8536,8856,8928,9009,9020,9566,9570,9834) AND co.RetiredAtUtc IS NULL AND co.Kind = N'Competitor';
PRINT 'Reclassified: ' + CAST(@@ROWCOUNT AS varchar(10));
COMMIT TRAN;
GO
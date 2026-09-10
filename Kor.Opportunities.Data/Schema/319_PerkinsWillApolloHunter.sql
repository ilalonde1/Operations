SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

INSERT INTO opportunities.OrgFact
    (NaturalKey, CanonicalOrgId, FactType, Body, SourceUrl, SourceRef, ObservedAtUtc, Confidence, CreatedAtUtc, CreatedBy)
SELECT CONVERT(varchar(40), HASHBYTES('SHA1', v.k), 2), 69688, v.ft, v.body, v.url,
       N'Perkins&Will brief for Omar Alcazar, Apollo + Hunter pass 2026-09-10',
       CAST('2026-09-10' AS datetimeoffset), N'High', SYSUTCDATETIME(), N'BrainDecompose-2026-09-10'
FROM (VALUES
 ('69688|scale|PW-2026-09-10c', N'MarketFocus',
  N'FIRM SCALE, commercial BI pull 2026-09-10: ~2,600 people, revenue $795.5M, founded 1935, HQ 410 N Michigan Ave Chicago, second-largest architecture firm in the US. HEADCOUNT GROWTH +5.0% six months, +8.4% twelve, +14.8% twenty-four - they are growing. ⭐OWNER CONFIRMED: DAR AL-HANDASAH (SHAIR AND PARTNERS), which appears as the named investor in their funding record - this had been unverifiable from public sources and is now sourced. SUBSIDIARIES: Portland Design (UK, 64) - so January''s "five retail principals hired from an Amsterdam firm" was effectively an internal move, Portland Design is theirs - plus Schmidt Hammer Lassen (Denmark, 170), Penoyre & Prasad, lauckgroup, Sink Combs Dethlefs, Pfau Long. Departmental headcount: arts and design 1,063, education 139, marketing 94, consulting 77, operations 74. Runs DELTEK VANTAGEPOINT, the same ERP as KOR, plus Revit, Rhino/Grasshopper, Bluebeam, ArcGIS, Newforma Konekt and One Click LCA for embodied carbon. ⚠ That source counts 28 studios where the firm''s own release says 32 - use 32. Revenue and headcount are modelled: good for scale, not for quoting to the dollar.',
  NULL),
 ('69688|hiring|PW-2026-09-10c', N'MarketFocus',
  N'⭐ THE FORWARD SIGNAL, 2026-09-10: 128 OPEN ROLES, OF WHICH TWO ARE IN CANADA (Vancouver 1, Calgary 1). BY LOCATION: Dallas 18, San Francisco 14, Atlanta 10, Minneapolis 10, Seattle 9, London 8, Charlotte 7, Chicago 7, Houston 6, Denver 5, Los Angeles 5, Boston 5, Shanghai 3, New York 3. BY DISCIPLINE: workplace and corporate interiors 35, HEALTHCARE 29, digital and computational 11, higher ed and K-12 8, urban design and landscape 5, science and labs 3, sports/civic/transit 3, sustainability 1. THE INFERENCE: their growth is American, not Canadian - so this is not a "grow with them into new markets" relationship, it is about the Canadian work that already exists. ⚠ TWO postings are Regional Practice Leader, Transit & Transportation Architecture (Chicago and San Francisco) - they are standing up a transit practice the same way they stood up retail in January, leadership first. They already hold Michal Mrowiec as Transportation Practice Leader, and Vancouver already lists Jeff Doble under transportation. ⚠ Only ONE sustainability role in 128, for a firm that markets hard on carbon.',
  NULL),
 ('69688|gushe|PW-2026-09-10c', N'WarmChannel',
  N'⭐ SUSAN GUSHE IS NOW CHIEF OPERATIONS OFFICER of Perkins&Will (roster refresh 2026-09-10, 98% confidence, susan.gushe@perkinswill.com). Our records had carried her as "Managing Director, Vancouver" - a title she held after Peter Busby moved to San Francisco, which was 2012. A VANCOUVER-ROOTED PERSON NOW RUNS GLOBAL OPERATIONS at a 2,600-person firm; that is the strongest relationship fact we hold on them. Other decision-makers now held: Tyson Curcio Chief Practice Officer; Jim Bynum Healthcare Practice Leader (29 of their 128 open roles are healthcare); Lisa Pool Director of Workplace Strategy (35 are workplace/interiors); David Damon Higher Education Practice Leader; Heidi Costello Health Education Practice Leader; Michal Mrowiec Transportation Practice Leader; Gilad Rosenzweig Director of R&D (the ex-MIT-accelerator hire); John Haymaker Director of Research; Regional Directors Mary Dickinson, Richard Herring, Tom Reisenbichler. We now hold 135 DISTINCT PEOPLE. ⚠ Still no email for Shauna Bryce (Cultural and Civic), Jeff Doble (Transportation) or Kerri Henderson (Advisory Services), all on the Vancouver leadership page.',
  N'https://perkinswill.com/studio/vancouver/')
) AS v(k, ft, body, url)
WHERE NOT EXISTS (SELECT 1 FROM opportunities.OrgFact e WHERE e.CanonicalOrgId = 69688
                  AND e.NaturalKey = CONVERT(varchar(40), HASHBYTES('SHA1', v.k), 2) AND e.RetiredAtUtc IS NULL);

SELECT 'live facts on Perkins&Will' AS Section;
SELECT COUNT(*) AS LiveFacts FROM opportunities.OrgFact WHERE CanonicalOrgId = 69688 AND RetiredAtUtc IS NULL;

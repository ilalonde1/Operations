/*
  decompose-to-brain.sql   MVE six-market research, 28 August 2026

  Banks this research into the graph as typed facts. Ian, 28 Aug:
  "it's supposed go in a DB not damn MD files" -- correct, and the standing
  rule already said so: every research run ends with a decompose-to-Brain
  pass, automatically.

  Before this ran there were ZERO OrgFacts from any of this work. The findings
  existed only in a PDF and in markdown, which is dead knowledge.

  Orgs were created first through the dup-safe path
  (tools/BdResearchImport --ingest-canonical, dry-run then live):
    886811 Howard Hughes Holdings      886818 Crosland Southeast
    886812 Host Hotels & Resorts       886819 DreamKey Partners
    886813 Vintage Partners            886820 Kittle Property Group
    886814 Mid-America Apartment Com.  886821 Creation Equity
    886815 Middleburg Communities      886822 Ryan Companies US
    886816 AREG AC Makena Propco       886823 StreetLights Residential
    886817 Ho'onani Development         54091 Ovation Development (matched)
                                        76952 MVE + Partners (existing)

  NaturalKey = SHA1(OrgId|FactType|lower(first 120 chars of Body, spaces
  stripped)) per Schema/289_OrgFact.sql, so re-running upserts rather than
  duplicating.

  ⚠ FactType is a CLOSED vocabulary. "will not hire an outside architect" is
    filed as DeliveryModel, not as a new type -- extending the vocabulary is a
    migration, not something a decompose does on the way past.

  RUN:  sqlcmd -S KOR-APP01\SQLEXPRESS -d KorOpportunitiesDb
              -U opportunities_app -P <pw> -N -C -I
              -i docs/audit-2026-08/mve-pipeline/decompose-to-brain.sql
*/

SET NOCOUNT ON;

DECLARE @by nvarchar(100) = N'BrainDecompose-MVE-SixMarket-2026-08-28';
DECLARE @src nvarchar(400) = N'KOR-MVE-Six-Market-Record-2026-08-28; docs/audit-2026-08/mve-pipeline/source/';
DECLARE @obs datetimeoffset = '2026-08-28';

DECLARE @f TABLE (OrgId bigint, FType nvarchar(30), Body nvarchar(max),
                  SrcUrl nvarchar(400), SrcRef nvarchar(400),
                  Obs datetimeoffset, Conf nvarchar(10));

INSERT INTO @f (OrgId, FType, Body, SrcUrl, SrcRef, Obs, Conf) VALUES

-- ---------- MVE + Partners: who they are and who they already build for ----
(76952, N'MarketFocus',
 N'Six named markets outside California, confirmed by Dan Gura on the 2026-08-27 call: Arizona, Nevada, Hawaii, Houston, Charlotte, Miami. Registered in all six. Full plate 18-24 months, mostly wood frame, concrete returning on the luxury condo side. No mass timber to date.',
 NULL, @src, @obs, N'High'),

(76952, N'WarmChannel',
 N'Published client list from mve-architects.com/portfolio (12 projects, client named on each): Howard Hughes (Ward Village), Hines, Toll Brothers, Holland Partner Group, Lowe Property Group, Blaser Ventures, Vestar, SHVO, H&S Ventures, REDA, NAHLA Capital, Lyon Living, Eagle Four Partners. NOTE this is a curated selection for a firm founded 1975 - absence from it proves nothing.',
 N'https://www.mve-architects.com/portfolio/', @src, @obs, N'High'),

(76952, N'RiskNote',
 N'DO NOT PITCH MVE ON WORK THEY ALREADY HAVE. Crossing their published client list against six-market records dropped two of their own clients: Hines appears 3x in the Arizona record, all completed office fit-outs with Phoenix Design One named; Vestar appears on a Phoenix rezoning already naming Butler Design Group. Ward Village, Kalae and Launiu excluded on the same rule.',
 NULL, @src, @obs, N'High'),

-- ---------- Howard Hughes: the cross-market thread -------------------------
(886811, N'WarmChannel',
 N'MVE is their architect at Ward Village, Honolulu (Kalae, Launiu). The same company is filing in three of MVE''s other five markets, which makes this a warm route into markets MVE does not currently serve them in.',
 NULL, @src, @obs, N'High'),

(886811, N'MarketFocus',
 N'Active in FOUR of MVE''s six markets. Las Vegas: Clark County multifamily pre-application 26-101519 filed 2026-08-27, 354 apartments + 6,556 sq ft commercial on 4 acres at Spruce Goose St, Downtown Summerlin; ownership disclosure names David O''Reilly (CEO), L. Jay Cross (President), Carlos A. Olea (CFO). Houston: 39 plat filings, 7,250 acres, 19 Dec 2025 to 10 Aug 2026, all through LJA Engineering - Bridgeland Prairieland Village GP 3,905 ac (2026-0191), Creekland Village GP 2,037 ac (2026-0982), Woodlands Village of Sterling Ridge 450 ac (2026-0973). Phoenix: owns Teravalis at Douglas Ranch, Buckeye. Hawaii: Ward Village.',
 NULL, @src, @obs, N'High'),

(886811, N'RiskNote',
 N'The Houston acreage is NOT an architecture commission. A Chapter 42 general plan divides land into streets, blocks and reserves; Prairieland Village''s ~7,000 homes go to production builders (Highland, David Weekley, Chesmar, Perry, Newmark, Century, Brightland) working from in-house plan books. Treat Houston and Phoenix as capital-placement intelligence; only the Las Vegas multifamily filing is a live commission signal.',
 NULL, @src, @obs, N'High'),

-- ---------- The seven verified openings ------------------------------------
(886812, N'MarketFocus',
 N'OPEN SEAT 2026-08-28. Phoenix rezoning Z-169-25-2, Copper Residences: 72 acres of the Westin Kierland Mesquite golf course. DU1 16.16 ac resort condominium and condo-hotel; DU2 55.64 ac single-family, townhome, duplex. Greey|Pickett site design and landscape, Woodpatel civil, CivTech traffic - NO BUILDING ARCHITECT on file or in trade press. Third application, in review after the May 2026 submittal.',
 NULL, @src, @obs, N'High'),

(886812, N'RiskNote',
 N'Neighbourhood opposition is active and on the record; planners deferred earlier iterations. Say so when raising it.',
 NULL, @src, @obs, N'High'),

(886813, N'MarketFocus',
 N'OPEN SEAT 2026-08-28. Phoenix rezoning Z-24-26-7: 1,000 residential units (single-family attached + multifamily) plus 22 acres commercial on 63 acres at Lower Buckeye Rd / Loop 202 / 63rd Ave. Site was earmarked for a data centre until Phoenix changed its data-centre policy. RVi Planning land planning, Precision Civil engineering. No building architect in the case file or trade press.',
 NULL, @src, @obs, N'High'),

(886814, N'MarketFocus',
 N'OPEN SEAT 2026-08-28. Charlotte rezoning petition 2026-050, 3.65 acres at Philips Place, SouthPark. MUDD-O SPA to RAC(CD). Maximum 275 multi-family stacked units + 15,000 sq ft non-residential in a MAXIMUM OF ONE PRINCIPAL BUILDING. Rezoning plan prepared for Post Properties by Kimley-Horn, dated 2026-07-15. No architect named.',
 NULL, @src, @obs, N'High'),

(886815, N'MarketFocus',
 N'OPEN SEAT 2026-08-28. Charlotte rezoning petition 2026-023, 20.15 acres at 9101 Wilkinson Boulevard. CG to N2-B(CD), up to 364 multi-family stacked units. Site plan titled WILKINSON MULTI-FAMILY lays out buildings at 63 units, four and five storeys, prepared-by block blank. Community meeting 2026-04-27; hearing pending.',
 NULL, @src, @obs, N'High'),

(886816, N'MarketFocus',
 N'OPEN SEAT 2026-08-28. Makena Mauka, Maui: 652 units including 109 onsite workforce, rural and single-family lots plus multi-family, and ~135,000 sq ft of operational support buildings across the Makena North and renovated South golf courses. FINAL EIS ACCEPTED 2026-08-23 - entitlement complete, building work next. Munekiyo Hiraga planning consultant; the architect field in the FEIS''s own permit appendix is blank.',
 NULL, @src, @obs, N'High'),

(886817, N'MarketFocus',
 N'OPEN SEAT 2026-08-28. Ho''onani Village, Maui - mixed-use, at DRAFT EIS before the State Land Use Commission, the earliest stage of any lead in this set. Pioneer Design Group-Hawai''i on planning. No architect named.',
 NULL, @src, @obs, N'High'),

-- ---------- Conditioned, not offered --------------------------------------
(886818, N'RiskNote',
 N'NOT OFFERED AS AN OPENING. Charlotte petition 2026-027, 39.41 acres north of Wilkinson Blvd at Little Rock Rd - the Destination District, 40 acres of restaurants, gas stations and airport-worker services at the Charlotte Douglas entrance. CITY PLANNERS RECOMMENDED DENIAL; deferred over design standards and Silver Line transit coordination. Construction targeted 2027, openings 2029. Engineer''s seal block on the site plan is still a blank placeholder.',
 NULL, @src, @obs, N'High'),

(886819, N'RiskNote',
 N'NOT OFFERED AS AN OPENING. Charlotte petition 2026-035, Beatties Ford Road. Duplex, triplex and quadraplex units by a 35-year housing nonprofit - small-scale attached housing, not a mid-rise commission. Acreage disagrees between sources: request document says 4.44 acres, petition page says 6.00. Public hearing already held 2026-08-17.',
 NULL, @src, @obs, N'High'),

(54091, N'RiskNote',
 N'CONDITIONED. Clark County pre-application 26-101224 filed 2026-07-08 by Alan Molasky; 36 communities, 13,000 units, >$1bn, with six more multifamily communities and 1,650 units due by 2028. No architect named on the filing - BUT Ovation carries an in-house design principal (Josie Molasky), so treat as a question rather than an opening until that is resolved.',
 NULL, @src, @obs, N'Medium'),

-- ---------- Will not hire an outside architect -----------------------------
(886820, N'DeliveryModel',
 N'WILL NOT HIRE AN OUTSIDE ARCHITECT. Their own website states the in-house design team provides conceptual site plans for every project and serves as ARCHITECT OF RECORD in all states their properties are located in. Surfaced on Charlotte petition 2026-051 (21.28 ac north of Morehead Rd) and excluded on that basis.',
 N'https://kittleproperties.com/design/', @src, @obs, N'High'),

(886821, N'DeliveryModel',
 N'WILL NOT HIRE AN OUTSIDE ARCHITECT. Most active developer among the fifty largest Arizona projects, with six. LGE Design Build on all six - five as design-builder, one as contractor alongside GFF Design. No outside architect appears on any Creation Equity project.',
 NULL, @src, @obs, N'High'),

(886822, N'DeliveryModel',
 N'WILL NOT HIRE AN OUTSIDE ARCHITECT. Developer and general contractor on all four Arizona projects and brings its own architect - Butler Design Group on three, Deutsch on the fourth. Butler-Ryan is the only architect-to-contractor pairing recurring more than once across the fifty largest Arizona projects (three together); every other pairing in the state is a one-off.',
 NULL, @src, @obs, N'High'),

(886823, N'DeliveryModel',
 N'WILL NOT HIRE AN OUTSIDE ARCHITECT. Fully vertically integrated: StreetLights Creative Studio is architect and interior designer, SLR Construction is the general contractor. Observed on The Langley, Houston, 134 units. Every seat in-house.',
 NULL, @src, @obs, N'High');

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

SELECT BankedThisRun = COUNT(*)
  FROM opportunities.OrgFact WHERE CreatedBy = @by AND RetiredAtUtc IS NULL;
GO

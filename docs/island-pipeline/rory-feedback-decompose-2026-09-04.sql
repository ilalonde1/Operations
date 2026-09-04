SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

-- 1. THE ANCHOR FIX. Row 80 is our Starlight client and was pointing at an
--    unrelated Caribbean luxury-retreat fund. Everyone on the row is
--    @starlightinvest.com; the projects are BC urban multi-family.
UPDATE opportunities.CanonicalOrg
SET WebsiteDomain = N'starlightinvest.com',
    Website       = N'https://www.starlightinvest.com/'
WHERE Id = 80;

-- 2. 55068 carries adamslakeband.org, a First Nation's domain it has no claim to.
--    Clear it; ResearchIdentityGate self-heals anchor-less rows.
UPDATE opportunities.CanonicalOrg
SET WebsiteDomain = NULL, Website = NULL
WHERE Id = 55068 AND WebsiteDomain = N'adamslakeband.org';

SELECT 'anchors after' AS Section;
SELECT Id, Kind, DisplayName, ISNULL(WebsiteDomain, '-') AS Domain,
       ISNULL(ClendorClientId, '-') AS Deltek, KorProjectsCount AS KorJobs,
       (SELECT COUNT(*) FROM opportunities.IntelPersonAffiliation a WHERE a.CanonicalOrgId = co.Id) AS People
FROM opportunities.CanonicalOrg co WHERE Id IN (80, 55068);

-- 3. Rory's intel, banked as typed org facts so the next pass starts from it.
IF OBJECT_ID('tempdb..#f') IS NOT NULL DROP TABLE #f;
CREATE TABLE #f (OrgId int, FactType nvarchar(60), Body nvarchar(max), SourceUrl nvarchar(400));

INSERT INTO #f (OrgId, FactType, Body, SourceUrl) VALUES
(38926, N'CompetitorNote',
 N'Has a VICTORIA office at 101-19 Dallas Road, Victoria BC V8V 5A6, confirmed on their own contact page 2026-09-04. Rory Beirne flagged that the Island who''s-who missed them entirely. Other offices: Vancouver (head, 1661 West 5th Ave), Kelowna, Calgary, Toronto, Los Angeles, and Surrey as GS Sayers. Carries Deltek CL00573.',
 N'https://glotmansimpson.com/contact/'),
(68998, N'CompetitorNote',
 N'ACQUIRED BY ENGLOBE CORPORATION, May 2025 — runs as a separate division inside Englobe and keeps its structure. Founded 1994, 70+ staff, Nanaimo HQ plus Victoria and Ucluelet offices. Rory Beirne 2026-09-04: "Herold is the incumbent engineer mid island. I am seeing their name a lot and they have the relationships." KOR has nonetheless taken Westmark''s Gracewood at Fairwinds, Nanoose (20330-01, Dec 2025), where Herold is Westmark''s usual structural partner.',
 N'https://www.englobecorp.com/en-ca/about-us/news/englobe-expands-its-canadian-footprint-acquisition-british-columbia-based-herold/'),
(927808, N'CompetitorNote',
 N'NOT A DIRECT COMPETITOR YET — correction from Rory Beirne, 2026-09-04. Sense is strong in building envelope, restoration, inspection and capital planning; lots of small jobs, and they are ON many of the same jobs as KOR, working existing relationships. Expanding fast with money to invest. WARNING: they are building a NEW CONSTRUCTION structural team — their own postings advertise a Structural Engineering Group Lead ($150-175k) and intermediate structural design engineers to "help lead the expansion of their practice into new construction", with BC work "focused on mass timber and hybrid structures", which is KOR''s institutional lane. Treat as a relationship threat now and a bid competitor within 12-24 months.',
 N'https://sense-engineering.breezy.hr/p/75412098ffac-structural-design-engineer-intermediate'),
(69014, N'CompetitorNote',
 N'Rory Beirne, 2026-09-04: RJC are AGGRESSIVE ON FEES lately. Heard through the grapevine that they are buying work, and separately that their fees are lower than or similar to KOR''s low fees. GRAPEVINE, NOT VERIFIED — treat as a pricing signal to test on the next competitive bid, not as fact.',
 NULL),
(80, N'RiskNote',
 N'IDENTITY: this row is Starlight INVESTMENTS'' Western Canada development arm, which Deltek bills as "Starlight Developments" (b8987e23). It was anchored to starlightdevelopment.com — an unrelated Caribbean luxury-retreat fund — until 2026-09-04. That wrong anchor caused three versions of the Island who''s-who to tell the Principal Vancouver Island that the two were different firms and that David Woo was not our client. He is: VP Development Western Canada, dwoo@starlightinvest.com. Daniel Drimmer, CEO of Starlight Investments, is on the same row. Projects for this client include 2740 Spencer Rd Langford (Starlight''s "The District"), 1701 Cedar Hill Rd Saanich and Quadra East/West Victoria.',
 N'https://www.starlightinvest.com/news/starlight-investments-helps-address-bc-housing-shortage-with-completion-of-first-two-residences-at-the-district-a-new-rental-community-in-langford');

INSERT INTO opportunities.OrgFact
    (NaturalKey, CanonicalOrgId, FactType, Body, SourceUrl, SourceRef, ObservedAtUtc, Confidence, CreatedAtUtc, CreatedBy)
SELECT
    CONVERT(varchar(40), HASHBYTES('SHA1',
        CONVERT(varchar(20), f.OrgId) + '|' + f.FactType + '|RoryBeirne2026-09-04'), 2),
    f.OrgId, f.FactType, f.Body, f.SourceUrl,
    N'Rory Beirne feedback on the Island who''s-who, 2026-09-04',
    CAST('2026-09-04' AS datetimeoffset), N'High', SYSUTCDATETIME(), N'BrainDecompose-2026-09-04'
FROM #f f
WHERE NOT EXISTS (
    SELECT 1 FROM opportunities.OrgFact e
    WHERE e.CanonicalOrgId = f.OrgId AND e.FactType = f.FactType
      AND e.CreatedBy = N'BrainDecompose-2026-09-04' AND e.RetiredAtUtc IS NULL);

SELECT 'org facts written' AS Section;
SELECT f.CanonicalOrgId, LEFT(co.DisplayName, 32) AS Org, f.FactType, LEFT(f.Body, 64) AS Body
FROM opportunities.OrgFact f
JOIN opportunities.CanonicalOrg co ON co.Id = f.CanonicalOrgId
WHERE f.CreatedBy = N'BrainDecompose-2026-09-04'
ORDER BY co.DisplayName;

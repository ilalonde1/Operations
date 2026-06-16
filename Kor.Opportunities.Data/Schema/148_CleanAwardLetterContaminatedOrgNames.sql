USE [KorOpportunitiesDb];
GO

/* =====================================================================
   148 — Clean award-letter boilerplate out of CanonicalOrg.DisplayName.
   ---------------------------------------------------------------------
   28 canonical rows (all low-id legacy, Kind=Vendor/GC/Developer, 1 award
   each, 0 MPI) had City-of-Airdrie award-evaluation rationale text absorbed
   into the org name when an early municipal award CSV mapped the awardee
   narrative field to DisplayName, e.g.:
       "RGO Office Products Partnership Thank you for submitting a proposal
        in response to the City of Airdrie's request RFP 217-2011-RJ. We have
        analysed all submissions ..."
   These are non-AEC vendors with no BD relevance, but per "clean at source"
   the names are corrected (not band-aided) to the real firm name. The source
   path is defunct (award enrichment retired 2026-06-10; StructuralRelevanceGate
   now blocks non-building work at intake), so correcting the rows is complete.

   Explicit per-row map (precision over regex — must not mis-truncate a real
   name). Each UPDATE is guarded by the contaminated prefix so re-runs are no-ops.
   ===================================================================== */

DECLARE @fix TABLE (Id BIGINT PRIMARY KEY, CleanName NVARCHAR(400));
INSERT INTO @fix (Id, CleanName) VALUES
 (1905,  N'Agro Equipment'),
 (1943,  N'Airdrie Tractorland'),
 (3049,  N'Base Corp. Learning Systems'),
 (4637,  N'Cervus Contractors Equipment LP'),
 (5697,  N'CWD Inc.'),
 (6894,  N'EMCO Corporation - Waterworks'),
 (9134,  N'Hugh Hamilton'),
 (9411,  N'Industrial Machine Inc.'),
 (10646, N'Lafrentz Road Marking, A Division of Canadian Road Builders Inc.'),
 (10974, N'Lineman Communications Ltd'),
 (11775, N'Micro Computers Plus'),
 (13126, N'Park N Play Design Co.'),
 (14083, N'Rally Software Development Corp.'),
 (14176, N'Reaction Distributing Inc.'),
 (14420, N'RGO Office Products Partnership'),
 (14689, N'Rolta Canada Ltd.'),
 (16271, N'Super Save Disposal (Alberta) Ltd.'),
 (16292, N'Superior Truck Equipment'),
 (16297, N'Supreme Landscaping (1994) Ltd.'),
 (16468, N'Tagish Engineering Ltd'),
 (16631, N'Telvent Canada Ltd.'),
 (16770, N'The City of Red Deer'),
 (16949, N'ThirdWave Corporation'),
 (18114, N'Wayne''s Cool It Refrigeration Ltd. (HVAC/Refrigeration)'),
 (18373, N'Westvac Industrial Ltd.'),
 (18394, N'WFR Wholesale Fire & Rescue Ltd.'),
 (18455, N'Wilco Contractors Southwest Inc.'),
 (18577, N'Wood Wyant Canada Inc.'),
 -- second pass: "evaluated"/"best value" status text appended to the name
 (4676,  N'CH2M HILL Canada Limited (CH2M)'),
 (5962,  N'Deford Contracting Inc.'),
 (6471,  N'Dunsky Energy Consulting'),
 (6536,  N'EAD Development Partnership'),
 (8487,  N'GreenLink Forestry Inc.'),
 (10728, N'Laser Clean Ltd (Furnaceman Don''s Power Vac)'),
 (11590, N'MCW Hemisphere Ltd.'),
 (12665, N'Nucor Systems Inc.'),
 (15519, N'SMA Consulting Ltd.'),
 (15610, N'Soles and Company'),
 (17480, N'U.S. Bank National Association');

UPDATE co
SET co.DisplayName = f.CleanName, co.UpdatedAtUtc = SYSDATETIMEOFFSET()
FROM opportunities.CanonicalOrg co
JOIN @fix f ON f.Id = co.Id
WHERE co.DisplayName <> f.CleanName;  -- idempotent
GO

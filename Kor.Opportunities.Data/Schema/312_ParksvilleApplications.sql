-- Migration 312 (2026-09-04): City of Parksville development applications.
--
-- Parksville is the ONLY market in the mid-Island set that publishes a named
-- APPLICANT per application, and the only one that publishes no feed at all --
-- it is a quarterly PDF. That combination makes it both the richest source in
-- these markets and the one that needs a reader.
--
-- Rows below are parsed by tools/ParksvillePdfParse/parse_parksville.py from
-- the 14 Jan 2026 issue (2024-2026 DEVELOPMENT APPLICATIONS,
-- parksville.ca/cms/wpattachments/wpID41atID12760.pdf).
-- 53 of 53 rows carry an applicant, an address and a description.
--
-- WHY THE PARSER USES WORD COORDINATES: the PDF's rows are visually STAGGERED --
-- a two-line applicant name pushes the description down, so in `pdftotext
-- -layout` output the description for one application sits on the line of the
-- NEXT one. Parsing by character offset silently attaches the wrong applicant to
-- the wrong address. The parser clusters pdfplumber word boxes by y instead.
--
-- These are loaded as a Manual-type source: there is no feed to poll, so a new
-- quarterly issue means re-running the parser and re-running this shape. The
-- source is marked IsEnabled = 0 so the Worker never tries to fetch it.
USE [KorOpportunitiesDb];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

DECLARE @pv uniqueidentifier;
SET @pv = NULL;
SELECT @pv = Id FROM opportunities.OpportunitySources WHERE Name = N'Parksville_DevelopmentApplications';
IF @pv IS NULL
BEGIN
    SET @pv = NEWID();
    INSERT INTO opportunities.OpportunitySources
        (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds,
         CreatedAtUtc, UpdatedAtUtc, IsHistorical, QuartzManaged)
    VALUES (@pv, N'Parksville_DevelopmentApplications', 99,
            N'https://www.parksville.ca/cms.asp?wpID=41',
            0, 86400, 120, sysdatetimeoffset(), sysdatetimeoffset(), 0, 0);
END;

DECLARE @src TABLE (ExternalRef nvarchar(200), Title nvarchar(400), Applicant nvarchar(200),
                    Descr nvarchar(3000), Filed date, Section nvarchar(100));
INSERT INTO @src (ExternalRef, Title, Applicant, Descr, Filed, Section) VALUES
(N'240 Dogwood Street December 8, 2025', N'240 Dogwood Street', N'Momentum Design Build', N'DP to facilitate expansion of the outdoor patio and replacement of an existing retaining wall (3060- PDP172) DEVELOPMENT PERMITS', N'2025-12-08', N'DEVELOPMENT PERMITS'),
(N'3060-PDP171', N'440 Island Hwy W', N'Radcliffe Development Corporation', N'Re-issuance of a DP to facilitate a 79-unit condominium complex (3060-PDP171) DEVELOPMENT PERMITS', N'2025-12-02', N'DEVELOPMENT PERMITS'),
(N'3060-PDP170', N'222 Corfield Street', N'Provincial Rental Housing Corporation', N'DP to faciliate a scooter storage building and refuse enclosure (3060-PDP170) DEVELOPMENT PERMITS', N'2025-11-05', N'DEVELOPMENT PERMITS'),
(N'3060-PDP169', N'1209 Island Highway East', N'Common Ground Consulting', N'DP to facilitate a new commercial building at the Heritage Mall (3060-PDP169) DEVELOPMENT PERMITS', N'2025-09-17', N'DEVELOPMENT PERMITS'),
(N'3060-PDP168', N'Inc. 1020 Herring Gull Way', N'Continuum Architecture', N'Reissuance of a DP to expand on the existing storage facility by adding four single storey storage buildings (3060-PDP168) DEVELOPMENT PERMITS', N'2025-07-03', N'DEVELOPMENT PERMITS'),
(N'3060-PDP167', N'Society 1225 Franklin''s Gull Road', N'Parksville Lions Housing & Daryoush Firouzli Architecture', N'DP to facilitate a non-market housing, 36-unit apartment building (3060-PDP167) DEVELOPMENT PERMITS', N'2025-06-27', N'DEVELOPMENT PERMITS'),
(N'3060-PDP166', N'100 Shelly Road', N'City of Parksville / The Nature Trust of British Columbia', N'DP to construct a multi-use trail within a statutory right of way that will include a boardwalk and gravel surfacing (3060-PDP166) DEVELOPMENT PERMITS', N'2025-05-28', N'DEVELOPMENT PERMITS'),
(N'3060-PDP165', N'266 Moilliet Street South', N'Climate Landscaping Ltd.', N'DP to facilitate a community pavilion and landscaping (3060-PDP165) DEVELOPMENT PERMITS', N'2025-04-15', N'DEVELOPMENT PERMITS'),
(N'3060-PDP164', N'368 Moilliet Street South', N'Daryoush Firouzli Architecture Inc.', N'DP to facilitate an apartment building in DPA No. 4 Multi-Unit and Intensive Residential (3060-PDP164) DEVELOPMENT PERMITS', N'2025-04-14', N'DEVELOPMENT PERMITS'),
(N'3060-PDP163', N'1480 Seaway Drive', N'P. Williams', N'DP application to facilitate an addition to a house in the Coastal Protection DPA 11 (3060-PDP163) DEVELOPMENT PERMITS', N'2025-02-28', N'DEVELOPMENT PERMITS'),
(N'3060-PDP162', N'193 Island Hwy E', N'City of Parksville', N'DP application to facilitate the relocation of the sandcastle area within Community Park (3060-PDP162) DEVELOPMENT PERMITS', N'2025-01-31', N'DEVELOPMENT PERMITS'),
(N'3060-PDP161', N'1116 Herring Gull Way', N'City of Parksville, RDN', N'DP application to facilitate wash pad installation for maintenance of fleet vehicles and a landscaping shed for storage of equipment (3060-PDP161) DEVELOPMENT PERMITS', N'2025-01-30', N'DEVELOPMENT PERMITS'),
(N'3060-PDP160', N'1175 Franklin''s Gull Road', N'Fenrick Construction', N'DP to facilitate an addition on an industrial building (3060-PDP160) DEVELOPMENT PERMITS', N'2025-01-16', N'DEVELOPMENT PERMITS'),
(N'155 Hirst Avenue East December 2, 2024', N'155 Hirst Avenue East', N'Ralph Christianson', N'Re-issuance of a DP to facilitate a 10 unit, multi-family building with commercial on the ground floor (3060- PDP159) DEVELOPMENT PERMITS', N'2024-12-02', N'DEVELOPMENT PERMITS'),
(N'3060-PDP158', N'1122 Herring Gull Way', N'Webb Investments Ltd.', N'DP to facilitate tree removal (3060-PDP158) DEVELOPMENT PERMITS', N'2024-10-24', N'DEVELOPMENT PERMITS'),
(N'3060-PDP157', N'1084 Herring Gull Way', N'dHK Architects', N'DP application to facilitate an addition to an existing building (3060-PDP157) DEVELOPMENT PERMITS', N'2024-10-09', N'DEVELOPMENT PERMITS'),
(N'3060-PDP156', N'365 Moilliet Street South', N'Christine Lintott Architecture', N'DP to facilitate an assisted living/congregate care facility for persons living with brain injuries (two buildings) (3060-PDP156) DEVELOPMENT PERMITS', N'2024-07-22', N'DEVELOPMENT PERMITS'),
(N'1116 Herring Gull Way July 22, 2024', N'1116 Herring Gull Way', N'City of Parksville, RDN', N'DP to facilitate replacement of a salt shed (3060- PDP155) DEVELOPMENT PERMITS', N'2024-07-22', N'DEVELOPMENT PERMITS'),
(N'Inc. 1020 Herring Gull Way July 17, 2024', N'Inc. 1020 Herring Gull Way', N'Continuum Architecture', N'DP to facilitate a storage facility expansion (3060- PDP154) DEVELOPMENT PERMITS', N'2024-07-17', N'DEVELOPMENT PERMITS'),
(N'3060-PDP153', N'625 Pioneer Crescent CIVIC ADDRESS', N'Timberlake-Jones Engineering APPLICANT SUBMISSION', N'DP to authorize the general form and character of a residential development with 10 dwelling units and associated landscaping (3060-PDP153) DESCRIPTION / FILE NO. DEVELOPMENT PERMITS', N'2024-07-11', N'DEVELOPMENT PERMITS'),
(N'3060-PDP152', N'1128 Herring Gull Way', N'Timberlake-Jones Engineering', N'DP to facilitate a two-lot subdivision (3060-PDP152) DEVELOPMENT PERMITS', N'2024-05-10', N'DEVELOPMENT PERMITS'),
(N'3090-PVP073', N'1209 Island Highway East', N'Common Ground Consulting', N'DVP to vary the building height and adjust the rear lot line setback of Building C (3090-PVP073) DEVELOPMENT VARIANCE PERMITS', N'2025-12-09', N'DEVELOPMENT VARIANCE PERMITS'),
(N'3090-PVP072', N'399 Kingsley St', N'Ecocraft Construction', N'DVP to vary setbacks in order to convert an existing detached shop into a dwelling unit (3090-PVP072) DEVELOPMENT VARIANCE PERMITS', N'2025-11-24', N'DEVELOPMENT VARIANCE PERMITS'),
(N'421 Day Place October 27, 2025', N'421 Day Place', N'Owner', N'DVP to vary the height of a residential fence (3090- PVP071) DEVELOPMENT VARIANCE PERMITS', N'2025-10-27', N'DEVELOPMENT VARIANCE PERMITS'),
(N'3090-PVP070', N'360 Hirst Avenue West', N'Creative Axis Drafting', N'Reduce setback requirement for a principal building from the interior lot line to facilitate conversion of an accessory building into a dwelling unit (3090-PVP070) DEVELOPMENT VARIANCE PERMITS', N'2025-08-08', N'DEVELOPMENT VARIANCE PERMITS'),
(N'3090-PVP069', N'446 Harnish Avenue', N'Owner', N'DVP to relax setback requirements in order to convert a garage into a secondary dwelling (3090-PVP069) DEVELOPMENT VARIANCE PERMITS', N'2024-09-06', N'DEVELOPMENT VARIANCE PERMITS'),
(N'133 McMillan St S March 28, 2024', N'133 McMillan St S', N'Village Design & Drafting', N'DVP to vary the setback from the west rear and north interior lot lines and the maximum floor area limit in order to facilitate the siting of a new greenhouse (3090- PVP068) DEVELOPMENT VARIANCE PERMITS', N'2024-03-28', N'DEVELOPMENT VARIANCE PERMITS'),
(N'130 Lee Avenue November 3, 2025', N'130 Lee Avenue', N'Fizzaharris Designs', N'Application to facilitate a 2-lot subdivision (3320- PSU079) SUBDIVISION APPLICATIONS', N'2025-11-03', N'SUBDIVISION APPLICATIONS'),
(N'133 Shelly Road August 26, 2025', N'133 Shelly Road', N'Timberlake-Jones Engineering', N'Application to facilitate a 4-lot subdivision (3320- PSU078) SUBDIVISION APPLICATIONS', N'2025-08-26', N'SUBDIVISION APPLICATIONS'),
(N'/ 450 Stanford Avenue East August 12, 2025', N'/ 450 Stanford Avenue East', N'Prism Land Surveying Ltd. Shelly Enterprises Ltd.', N'Application to facilitate a 2-lot subdivision (3320- PSU077) SUBDIVISION APPLICATIONS', N'2025-08-12', N'SUBDIVISION APPLICATIONS'),
(N'634 Blenkin Avenue March 25, 2025', N'634 Blenkin Avenue', N'Timberlake-Jones Engineering', N'Application to facilitate a 5-lot subdivision (3320- PSU076) SUBDIVISION APPLICATIONS', N'2025-03-25', N'SUBDIVISION APPLICATIONS'),
(N'1465 Greig Road February 14, 2025', N'1465 Greig Road', N'Waterfront Properties Corp', N'Application to split the existing parcel in two (3320- SUBDIVISION APPLICATIONS', N'2025-02-14', N'SUBDIVISION APPLICATIONS'),
(N'360, 364, 368 Moilliet Street South January 15, 2025', N'360, 364, 368 Moilliet Street South', N'Williamson & Associates', N'Application to split the existing parcel in two (3320- PSU074) SUBDIVISION APPLICATIONS', N'2025-01-15', N'SUBDIVISION APPLICATIONS'),
(N'3320-PSU073', N'156 Ford Avenue, 151 Hickey Avenue', N'Owner', N'Application to facilite splitting the existing parcel in two (3320-PSU073) SUBDIVISION APPLICATIONS', N'2024-09-26', N'SUBDIVISION APPLICATIONS'),
(N'3320-PSU072', N'318 Willow Street', N'Timberlake-Jones', N'Application to facilite splitting the existing parcel in two (3320-PSU072) SUBDIVISION APPLICATIONS', N'2024-09-06', N'SUBDIVISION APPLICATIONS'),
(N'560 Tulip Avenue August 29, 2024', N'560 Tulip Avenue', N'Timberlake-Jones', N'Subdivision to facilite a 2-lot subdivision (3320- PSU071) SUBDIVISION APPLICATIONS', N'2024-08-29', N'SUBDIVISION APPLICATIONS'),
(N'3320-PSU070', N'440 Island Highway West', N'Radcliffe Development Corporation', N'Application to facilitate a 2-lot subdivision and park dedication (3320-PSU070) SUBDIVISION APPLICATIONS', N'2024-08-23', N'SUBDIVISION APPLICATIONS'),
(N'3360-PZN064', N'Ltd. 353 Moilliet St S', N'Northland Developments', N'Application to rezone from RS-1 to RS-3 and amend OCP land use designation from Transitional Residential to Multi-unit residential (3360-PZN064) REZONING APPLICATIONS', N'2026-01-14', N'REZONING APPLICATIONS'),
(N'3360-PZN063', N'292 & 302 Moilliet St S', N'Studio PA', N'Application to rezone from RS-1 to RHD-4 and amend OCP land use designation from Single Unit Residential to Multi-unit residential (3360-PZN063) REZONING APPLICATIONS', N'2025-12-04', N'REZONING APPLICATIONS'),
(N'402 & 416 Pioneer Crescent, 405 Island Highway East CIVIC AD June 17, 2025', N'402 & 416 Pioneer Crescent, 405 Island Highway East CIVIC ADDRESS', N'District Developments Corp. APPLICANT SUBMISSION', N'Application to amend the zone from Highway Commercial CS-1 to a CD zone that allows for multi- family residential on a portion of the site (3360- PZN062) DESCRIPTION / FILE NO. REZONING APPLICATIONS', N'2025-06-17', N'REZONING APPLICATIONS'),
(N'520 & 530 Martindale April 22, 2025', N'520 & 530 Martindale', N'MacDonald Gray Consultants', N'Rd Application to amend the zone from Single Family Residential RS-1 to Small Lot Residential SLR-1 (3360- PZN061) REZONING APPLICATIONS', N'2025-04-22', N'REZONING APPLICATIONS'),
(N'3360-PZN060', N'384 Young St', N'Timberlake-Jones Engineering', N'Application to amend the zone from Single Family Residential RS-1 to Small Lot Residential SLR-1 to facilitate a two-lot subdivision (3360-PZN060) REZONING APPLICATIONS', N'2025-04-17', N'REZONING APPLICATIONS'),
(N'3360-PZN059', N'1465 Greig Rd', N'Waterfront Properties Corp', N'Application to rezone from Agricultural A-1 to a CD zone to allow for multi-unit residential (3360-PZN059) REZONING APPLICATIONS', N'2025-02-14', N'REZONING APPLICATIONS'),
(N'3360-PZN058', N'1180 Resort Dr', N'Primex Investments', N'Application to amend the existing CD-29 zone to facilitate subdivision and commercial floor area adjustment (3360-PZN058) REZONING APPLICATIONS', N'2024-11-13', N'REZONING APPLICATIONS'),
(N'3360-PZN057', N'130 Lee Ave', N'FizzaHarris Designs', N'Application to amend the zone from Single Family Residential RS-1 to Small Lot Residential SLR-1 to facilitate a two-lot subdivision (3360-PZN057) REZONING APPLICATIONS', N'2024-11-12', N'REZONING APPLICATIONS'),
(N'3360-PZN056', N'156 Ford Ave', N'Owner', N'Application to amend the zone from Single Family Residential RS-1 to Small Lot Residential SLR-1 to facilitate a two-lot subdivision (3360-PZN056) REZONING APPLICATIONS', N'2024-06-04', N'REZONING APPLICATIONS'),
(N'360 Pym St N April 10, 2024', N'360 Pym St N', N'Seward Developments Inc.', N'Application to amend the zone from the current CD-9 zone to a multi-family zone that allows 76 units (3360- PZN055) REZONING APPLICATIONS', N'2024-04-10', N'REZONING APPLICATIONS'),
(N'3360-PZN054', N'BC Ltd. 386 Hirst Ave W', N'D. Lamoureux, 0932024', N'Zoning amendment from the current RS-1 zone to a multi-family zone to allow a 10-unit residential building (3360-PZN054) REZONING APPLICATIONS', N'2024-04-08', N'REZONING APPLICATIONS'),
(N'1000 Island Hwy E January 3, 2024', N'1000 Island Hwy E', N'1368662 BC Ltd.', N'Zoning and OCP amendment application to facilitate a mixed-use commercial/residential development (3360- PZN053) REZONING APPLICATIONS', N'2024-01-03', N'REZONING APPLICATIONS'),
(N'3360-PZN052', N'Society 1225 Franklin''s Gull Road', N'Parksville Lions Housing / City of Parksville', N'Zoning and OCP amendment application to facilitate non-market housing (3360-PZN052) REZONING APPLICATIONS', N'2023-12-14', N'REZONING APPLICATIONS'),
(N'3360-PZN051', N'/ 367 Jensen Ave W', N'Prism Land Surveying Ltd. Simmons, B. and B.', N'Zoning amendment from RS-1 to a zone to facilitate a duplex (3360-PZN051) REZONING APPLICATIONS', N'2023-10-16', N'REZONING APPLICATIONS'),
(N'3360-PZN050', N'Inc 365 Moilliet St S', N'Christin Lintott Architects', N'Zoning amendment from RS-1 to a CD zone to facilitate residences and support services for individuals affected by brain injuries (3360-PZN050) REZONING APPLICATIONS', N'2023-08-09', N'REZONING APPLICATIONS'),
(N'3360-PZN049', N'423 Alberni Highway', N'Picard Enterprise Ltd.', N'Zoning amendment from Agricultural to Mixed Use Commercial (3360-PZN049) REZONING APPLICATIONS', N'2023-05-31', N'REZONING APPLICATIONS');

-- Opportunities
INSERT INTO opportunities.Opportunities
    (OpportunityKey, Name, BuyerName, BuyerType, Discipline, EstimatedValueCurrency, Status,
     ProjectCity, ProjectProvince, ProjectAddress, BuyerContactName, RfpReleaseDate,
     IdentifiedAtUtc, CreatedAtUtc, CreatedBy, UpdatedAtUtc, UpdatedBy)
SELECT
    LEFT(N'PARKSVIL-' + CASE WHEN s.ExternalRef LIKE '____-[A-Z]%' THEN s.ExternalRef ELSE CONVERT(varchar(32), HASHBYTES('SHA1', s.ExternalRef), 2) END, 64),
    LEFT(s.Title, 800), N'City of Parksville', 0, 0, N'CAD', 0,
    N'Parksville', N'BC', LEFT(s.Title, 400),
    NULLIF(s.Applicant, N''), s.Filed,
    sysdatetimeoffset(), sysdatetimeoffset(), N'Migration312-ParksvillePdf',
    sysdatetimeoffset(), N'Migration312-ParksvillePdf'
FROM @src s
WHERE NOT EXISTS (SELECT 1 FROM opportunities.Opportunities o
                  WHERE o.OpportunityKey = LEFT(N'PARKSVIL-' + CASE WHEN s.ExternalRef LIKE '____-[A-Z]%' THEN s.ExternalRef ELSE CONVERT(varchar(32), HASHBYTES('SHA1', s.ExternalRef), 2) END, 64));

-- Observations, so the rows appear under the source like every other feed.
INSERT INTO opportunities.OpportunityObservations
    (OpportunitySourceId, OpportunityId, Title, Buyer, Url, Description, PostedDateUtc,
     Location, IngestedAtUtc, HashSha256, IsActive)
SELECT @pv, o.Id, LEFT(s.Title, 800), N'City of Parksville',
       N'https://www.parksville.ca/cms.asp?wpID=41', s.Descr, s.Filed,
       LEFT(s.Title, 400), sysdatetimeoffset(),
       HASHBYTES('SHA2_256', s.ExternalRef + N'|' + s.Title + N'|' + s.Descr), 1
FROM @src s
JOIN opportunities.Opportunities o ON o.OpportunityKey = LEFT(N'PARKSVIL-' + CASE WHEN s.ExternalRef LIKE '____-[A-Z]%' THEN s.ExternalRef ELSE CONVERT(varchar(32), HASHBYTES('SHA1', s.ExternalRef), 2) END, 64)
WHERE NOT EXISTS (SELECT 1 FROM opportunities.OpportunityObservations ob
                  WHERE ob.OpportunitySourceId = @pv AND ob.OpportunityId = o.Id);

SELECT 'loaded' AS Section;
SELECT COUNT(*) AS Applications,
       SUM(CASE WHEN o.BuyerContactName IS NOT NULL THEN 1 ELSE 0 END) AS WithApplicant,
       CONVERT(varchar(10), MAX(o.RfpReleaseDate), 23) AS Latest
FROM opportunities.OpportunityObservations ob
JOIN opportunities.Opportunities o ON o.Id = ob.OpportunityId
WHERE ob.OpportunitySourceId = @pv;
GO

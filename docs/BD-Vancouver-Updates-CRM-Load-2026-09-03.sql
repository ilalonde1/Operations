SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

-- BD Vancouver Updates, 3 September 2026: the meeting's pursuits loaded into the CRM.
-- Source: docs/BD-Vancouver-Updates-Synopsis-2026-09-03.md (action items, section 3).
-- Idempotent: each row carries ExternalSourceKey 'BDV-2026-09-03:<orgId>' and is inserted only if
-- that key is absent. Stage 1 = Drafting (a live pursuit). Owner ids are opportunities.BdStaff.Id.
-- Run with @commit = 0 to see the rows; @commit = 1 to write.

DECLARE @commit bit = 1;
DECLARE @meeting datetimeoffset = '2026-09-03 18:02:00 +00:00';
DECLARE @by nvarchar(100) = N'Ian (BD meeting 2026-09-03)';
DECLARE @src nvarchar(50) = N'BdMeeting';

IF OBJECT_ID('tempdb..#rows') IS NOT NULL DROP TABLE #rows;
CREATE TABLE #rows
(
    OrgId bigint NOT NULL,
    Owner nvarchar(100) NULL,
    Assigned nvarchar(200) NULL,
    Region nvarchar(60) NULL,
    ContactId bigint NULL,
    NextDue datetime2 NULL,
    NextNote nvarchar(1000) NOT NULL,
    Notes nvarchar(2000) NOT NULL
);

INSERT INTO #rows (OrgId, Owner, Assigned, Region, ContactId, NextDue, NextNote, Notes) VALUES
(70132, N'Omar', NULL, N'Vancouver/LowerMainland', NULL, '2026-09-19 17:00',
 N'Set up the lunch with the municipal recreation director through the Colliers contact; approach her private-sector lead with a concrete offer; ask what change-of-use and TI due-diligence work Colliers sees.',
 N'Omar''s contact at Colliers (name to confirm) is moving into owner''s-rep roles with a say in team selection: BC Cancer Vancouver expansion (prime RFP already ingested as opportunity 23643), Delta Aquatic Centre (team set; offer VE / peer review), a BCIT lab. Ian to supply the coordinated health-architect target list.'),
(71252, N'Islam', NULL, N'Vancouver/LowerMainland', NULL, '2026-09-19 17:00',
 N'Arrange an intro meeting with KRA''s Vancouver team.',
 N'Kirsten Reite Architecture, healthcare. Islam met them in Edmonton (lunch 3 July); they are open to meeting in Vancouver.'),
(69688, N'Omar', N'Rory,Jim', N'Vancouver/LowerMainland', 4523, '2026-09-10 17:00',
 N'Get at least one seat at the Perkins&Will client-appreciation party on 10 Sept: Omar via an architect friend, Rory via Ike, Jim via the Chief Marketing Officer.',
 N'No invite as of 3 Sept. JM knows Luciano Siffredi (Project Architect) and Ryan Bragg (Principal) there.'),
(38943, N'jbryson@korstructural.com', NULL, N'Vancouver/LowerMainland', 2444, '2026-09-26 17:00',
 N'Reach out to Robert to broker a lunch with Colin Bosa in October (lunch, not dinner).',
 N'JB has been in touch with Robert (most likely Robert Bosa, founder) over recent weeks; JB is tied up until end of September.'),
(69730, N'jbryson@korstructural.com', NULL, N'Vancouver/LowerMainland', 2486, '2026-09-12 17:00',
 N'One-on-one with Gerry Nichele (President and CEO), the decision maker, this week or early next.',
 N'JB is friendly with several people there (Brian Webster, Mike Balza per JB; firms to confirm).'),
(38934, N'Omar', N'Islam', N'Vancouver/LowerMainland', NULL, '2026-09-30 17:00',
 N'Pitch KOR to Stantec''s Vancouver structural group as specialty and overflow support, talking to their engineering group as with DIALOG in Edmonton.',
 N'KOR is on Stantec''s qualified-supplier list (JB). Their structural division is Edmonton-based. Islam knows their Edmonton people. Jim: Omar and Islam own this, not JB.'),
(75853, N'John', N'Omar,ilalonde@korstructural.com', N'Vancouver/LowerMainland', NULL, '2026-09-19 17:00',
 N'Ian re-sends the Port Authority dossier; JM and Omar pursue their ex-employee contact.',
 N'JM suspects opportunities at the Vancouver Fraser Port Authority. Being on the seismic lists is the door.'),
(147, N'Jim', N'John,Omar', N'Vancouver/LowerMainland', NULL, '2026-09-30 17:00',
 N'Prepare the "designing without transfers" presentation and give it at Aquilini with JM and Omar; lunch with Giovanni Gunawan (Development Manager).',
 N'Heather Lands: KOR has Block B; Aquilini says KOR gets a shot at Block A (growing into a large tower); going after Block C too. WHM currently hold A and C. Aquilini wants an A-team it hands most of its work to. The extra claim submitted 29 Aug got everyone''s attention.'),
(54190, N'Jim', NULL, N'Vancouver/LowerMainland', NULL, '2026-09-30 17:00',
 N'Give the "no transfers" presentation at GBL''s office; Thomas Lee asked for it.',
 N'Coffee with Thomas Lee (Principal) on 2 Sept. GBL designed the Heather Lands south parcel (three buildings).'),
(66, N'John', NULL, N'Vancouver/LowerMainland', NULL, '2026-09-12 17:00',
 N'Confirm with Beedie whether KOR is one of the three teams in the Fraser Mills parcel tryout.',
 N'Thomas Lee (GBL) says three parcels, three teams, a tryout. Nobody on the call was sure; KOR bid two Beedie jobs a year or two ago and is doing the Uplands towers in West Vancouver.'),
(927758, N'Rory', N'Jim', N'VancouverIsland', 10383, '2026-09-19 17:00',
 N'Reach Jeremy Beintema (Partner) and Gregory Eeman (Architect AIBC) using Ben Smith''s intro; go and see them.',
 N'Victoria''s oldest practice (KPL James, rebranded 2020). Partners Beintema and Wil Wiens. Founder Tony James died October 2024: do not raise it. They appear to hold most of Trillium''s work. Peter Fair is Continuum Partners (Denver), a different firm.'),
(54443, N'Jim', N'Rory', N'VancouverIsland', NULL, '2026-09-19 17:00',
 N'Follow up the outstanding proposal (wood-frame seniors housing) sent via Ben Smith / BAM.',
 N'Confirm this is Trillium Communities rather than Trillium Projects. Ben Smith (ex-Starlight) has his own group, BAM (company not yet identified); he says Trillium has a lot of property.'),
(76952, N'Jim', N'John', N'USA', NULL, '2026-09-19 17:00',
 N'Check with Daler that MVE''s insurance-update request went back; plan the Irvine visit around the LA Tall Building Conference in November.',
 N'The AI demo landed with MVE''s BD lead; the insurance request reads as requalification. Goal: back on their list.'),
(7, N'Omar', N'Jim', N'Vancouver/LowerMainland', 7911, '2026-09-11 12:00',
 N'Lunch with Mike Alivojvodic (Principal) on 11 Sept.',
 N'Arranged by Omar.'),
(153, N'John', NULL, N'Vancouver/LowerMainland', 19410, '2026-09-19 17:00',
 N'Call Gwyn Vose (Canada West Practice Group Manager) for lunch; keep Clement Pun warm.',
 N'Timely after the Terry Gray meeting; Arcadis is bringing BD to Edmonton. Clement Pun and Mariam are on the Reliance Broadway towers with Rory.'),
(77714, N'John', NULL, N'Vancouver/LowerMainland', NULL, '2026-10-15 17:00',
 N'Not assigned on the call: worth a visit (Jim). JM''s working contact Peter Odegaard is a Partner; a principal-level meeting is the ask.',
 N'MCM are busy with PCI and transit work. Last year''s presentation reached only juniors.'),
(207, N'John', N'Islam', N'Alberta', NULL, '2026-09-26 17:00',
 N'Reach Anthem directly ahead of the late-September Calgary trip.',
 N'Islam is booking the Calgary meetings; JM to get on the plane.'),
(8129, N'John', N'Islam', N'Alberta', NULL, '2026-09-26 17:00',
 N'Reach GGA directly ahead of the late-September Calgary trip.',
 N'Islam is booking the Calgary meetings.'),
(193, N'Conor', NULL, N'Okanagan-BcInterior', NULL, '2026-09-30 17:00',
 N'Follow the departed contact (Stephanie) to her new firm; keep Mission Group warm.',
 N'Conor met them on the last Kelowna visit; Stephanie left two weeks later.'),
(68976, N'Conor', N'John', N'Okanagan-BcInterior', NULL, '2026-09-30 17:00',
 N'JM to introduce Conor to Berry Architecture''s new Kelowna studio (Carlos Gamez Ruiz).',
 N'Red Deer firm; Kelowna studio opened this year.'),
(70926, N'John', NULL, N'Vancouver/LowerMainland', 1473, '2026-09-12 17:00',
 N'Phone Brad Burnett (President).',
 N'Omar''s repeated ask; JM committed on the call.'),
(16, N'Rory', NULL, N'Vancouver/LowerMainland', NULL, '2026-12-01 17:00',
 N'Keep Fred Lin (Development Manager) warm; check in on the Cambie and Marine tower in the new year.',
 N'Tower proposal stalled on offshore capital; Rize is going GC-for-hire meanwhile.'),
(880, NULL, N'John', N'Vancouver/LowerMainland', NULL, '2026-10-15 17:00',
 N'Fraser Health qualified list closes before November; be ready for the notice and warm the prime architects now.',
 N'Owner per the June meeting: John (which John was not said). Confirm.');

SELECT r.OrgId, LEFT(co.DisplayName, 40) AS Org, r.Owner, r.Region, CONVERT(varchar(10), r.NextDue, 23) AS Due, LEFT(r.NextNote, 70) AS NextNote,
       CASE WHEN EXISTS (SELECT 1 FROM opportunities.CrmEngagements e WHERE e.ExternalSourceKey = N'BDV-2026-09-03:' + CAST(r.OrgId AS nvarchar(20))) THEN 'exists' ELSE 'new' END AS State
FROM #rows r JOIN opportunities.CanonicalOrg co ON co.Id = r.OrgId AND co.RetiredAtUtc IS NULL
ORDER BY r.OrgId;

SELECT 'rows whose org is missing or retired' AS Check_, COUNT(*) AS N FROM #rows r WHERE NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = r.OrgId AND co.RetiredAtUtc IS NULL);

IF @commit = 1
BEGIN
    BEGIN TRANSACTION;
    INSERT INTO opportunities.CrmEngagements
        (OpportunityId, Stage, OwnerStaffId, AssignedStaffIds, Notes, OpenedAtUtc, CreatedAtUtc, CreatedBy, UpdatedAtUtc, UpdatedBy,
         BuyerCanonicalOrgId, Region, NextActionDueUtc, NextActionNote, ExternalSource, ExternalSourceKey, ContactIntelPersonId)
    SELECT NULL, 1, r.Owner, r.Assigned, r.Notes, @meeting, sysdatetimeoffset(), @by, sysdatetimeoffset(), @by,
           r.OrgId, r.Region, r.NextDue, r.NextNote, @src, N'BDV-2026-09-03:' + CAST(r.OrgId AS nvarchar(20)), r.ContactId
    FROM #rows r
    WHERE EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = r.OrgId AND co.RetiredAtUtc IS NULL)
      AND NOT EXISTS (SELECT 1 FROM opportunities.CrmEngagements e WHERE e.ExternalSourceKey = N'BDV-2026-09-03:' + CAST(r.OrgId AS nvarchar(20)));
    SELECT 'inserted' AS Result, @@ROWCOUNT AS N;

    -- Existing pursuits touched by the meeting: next action only.
    UPDATE opportunities.CrmEngagements
    SET NextActionDueUtc = '2026-10-01 17:00',
        NextActionNote = N'Conor attends Graham''s client-appreciation night, 1 Oct, Georgia Hotel; trying for two more invites. Talk to everyone.',
        AssignedStaffIds = CASE WHEN ISNULL(AssignedStaffIds, '') = '' THEN N'Conor' WHEN AssignedStaffIds LIKE '%Conor%' THEN AssignedStaffIds ELSE AssignedStaffIds + N',Conor' END,
        UpdatedAtUtc = sysdatetimeoffset(), UpdatedBy = @by
    WHERE Id = 91 AND BuyerCanonicalOrgId = 69232;
    SELECT 'updated #91 Graham' AS Result, @@ROWCOUNT AS N;
    COMMIT TRANSACTION;
END

SELECT e.Id, LEFT(co.DisplayName, 34) AS Org, e.OwnerStaffId AS Owner, ISNULL(e.AssignedStaffIds,'') AS Assigned, e.Region, CONVERT(varchar(10), e.NextActionDueUtc, 23) AS Due, LEFT(e.NextActionNote, 60) AS NextNote
FROM opportunities.CrmEngagements e JOIN opportunities.CanonicalOrg co ON co.Id = e.BuyerCanonicalOrgId
WHERE e.ExternalSource = @src OR e.Id IN (11, 91)
ORDER BY e.Id;

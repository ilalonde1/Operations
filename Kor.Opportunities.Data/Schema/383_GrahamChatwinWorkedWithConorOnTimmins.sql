/*
    KorOpportunitiesDb 383: corrects the Bridgestar warm-channel fact from 382, which said the link was unconfirmed.

    Filed email read 4 Nov 2026 (KorEmailIndex, project 31101-01): Graham Chatwin, then Mission Group's Preconstruction Manager,
    emailed Conor Murtagh directly on 14, 16 and 21 January 2025, and Conor emailed him on 15, 16 and 21 January 2025. On 21 January
    2025 Graham wrote "The structure looks very good from the construction perspective." Jeremy Atkinson dealt with him from
    October 2023. A filed search of 378,146 rows found no email with bridgestar.ca, teamconstruction.ca, icdev.ca,
    ironcladdevelopments.ca or stretchconstruction.ca, and no body naming these firms. That search recorded no check for Sherri Turpin.
    Searched again 5 Nov (review row 510), read-only, 378,230 to 378,324 rows, case-insensitive across From, To, Cc, Bcc, Subject and
    BodyText: Bridgestar, Stretch Construction, stretchconstruction, Team Construction, teamconstruction, Ironclad, icdev.ca: 0 rows each.
    Turpin (which also covers turpinarchitect@outlook.com): 1 row, Carolyn Turpin of the City of Coquitlam on project 30770-01, not
    Sherri Turpin. Mail never filed is not covered.

    The old fact is retired with a reason, not edited, so the record of what was believed stays. Idempotent.
*/
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRAN;

DECLARE @oldKey varchar(40) = CONVERT(varchar(40), HASHBYTES('SHA1', '1838121|warm|2026-11-04'), 2);
DECLARE @newKey varchar(40) = CONVERT(varchar(40), HASHBYTES('SHA1', '1838121|warm-proven|2026-11-04'), 2);

IF NOT EXISTS (SELECT 1 FROM opportunities.OrgFact WHERE NaturalKey = @newKey)
BEGIN
    INSERT INTO opportunities.OrgFact
        (NaturalKey, CanonicalOrgId, FactType, Body, SourceUrl, SourceRef, ObservedAtUtc, Confidence, CreatedAtUtc, CreatedBy)
    VALUES (@newKey, 1838121, N'WarmChannel',
        N'PROVEN from filed email: Graham Chatwin, Director of Construction since Oct 2026, was Mission Group''s Preconstruction Manager on KOR job 31101 (955 Timmins St in Deltek, 1001 Timmins in Mission''s email). He emailed Conor Murtagh directly on 14, 16 and 21 Jan 2025; on 21 Jan 2025: "The structure looks very good from the construction perspective." Conor is PM and principal, 426.5 h. Jeremy Atkinson dealt with him from Oct 2023. Mission Group: $204,949 invoiced on 2 jobs. Cell on his Jan 2025 signature: 778-214-5645.',
        NULL, N'KorEmailIndex project 31101-01, read 2026-11-04', CAST('2026-11-04' AS datetimeoffset), N'High', SYSUTCDATETIME(), N'BrainDecompose-2026-11-04');
END

UPDATE opportunities.OrgFact
   SET RetiredAtUtc = SYSDATETIMEOFFSET(), RetiredReason = N'Corrected 4 Nov 2026: filed email proves the link this fact called unconfirmed', SupersededByFactId = (SELECT Id FROM opportunities.OrgFact WHERE NaturalKey = @newKey)
 WHERE NaturalKey = @oldKey AND RetiredAtUtc IS NULL;

SELECT Id, FactType, Confidence, RetiredAtUtc, LEFT(Body, 60) AS Body FROM opportunities.OrgFact WHERE NaturalKey IN (@oldKey, @newKey) ORDER BY Id;

COMMIT;

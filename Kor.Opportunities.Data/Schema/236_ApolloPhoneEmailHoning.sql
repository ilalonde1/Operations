USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO
/* Migration 236: Apollo honing - email + direct phone for 5 org-verified
   decision-makers (people/match, org-match confirmed per the verify rule).
   Apollo emails recorded as EmailSource='asis' conf 85. */
BEGIN TRAN;
UPDATE opportunities.IntelPerson SET Phone=COALESCE(Phone,N'+1 604-579-0970'), UpdatedAtUtc=sysdatetimeoffset() WHERE Id=959;   -- Sukhi Rai (Jayen, President) - phone only
UPDATE opportunities.IntelPerson SET Email=COALESCE(Email,N'pyammine@pinnacleinternational.ca'), EmailSource=COALESCE(EmailSource,N'asis'), EmailConfidence=COALESCE(EmailConfidence,85), Phone=COALESCE(Phone,N'+1 604-602-7747'), EmailCheckedAtUtc=sysdatetimeoffset(), UpdatedAtUtc=sysdatetimeoffset() WHERE Id=1400;   -- Pascal Yammine (Pinnacle VP Construction)
UPDATE opportunities.IntelPerson SET Email=COALESCE(Email,N'jsivia@maskeen.ca'), EmailSource=COALESCE(EmailSource,N'asis'), EmailConfidence=COALESCE(EmailConfidence,85), Phone=COALESCE(Phone,N'+1 604-502-9096'), EmailCheckedAtUtc=sysdatetimeoffset(), UpdatedAtUtc=sysdatetimeoffset() WHERE Id=13926;  -- Jagdip Sivia (Maskeen Owner)
UPDATE opportunities.IntelPerson SET Email=COALESCE(Email,N'tgevatkoff@itc-group.com'), EmailSource=COALESCE(EmailSource,N'asis'), EmailConfidence=COALESCE(EmailConfidence,85), Phone=COALESCE(Phone,N'+1 604-685-0111'), EmailCheckedAtUtc=sysdatetimeoffset(), UpdatedAtUtc=sysdatetimeoffset() WHERE Id=1476;   -- Tom Gevatkoff (ITC VP Gen Superintendent)
UPDATE opportunities.IntelPerson SET Email=COALESCE(Email,N'annelise@purposedrivenroi.com'), EmailSource=COALESCE(EmailSource,N'asis'), EmailConfidence=COALESCE(EmailConfidence,85), Phone=COALESCE(Phone,N'+1 604-428-1149'), EmailCheckedAtUtc=sysdatetimeoffset(), UpdatedAtUtc=sysdatetimeoffset() WHERE Id=13816;  -- Annelise van der Veen (Purpose Driven) - NOTE dup of "Annelise Veen"
PRINT 'Migration 236: Apollo email+phone honing for 5 decision-makers.';
COMMIT TRAN;
GO

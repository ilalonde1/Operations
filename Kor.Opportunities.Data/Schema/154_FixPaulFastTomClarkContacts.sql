USE [KorOpportunitiesDb];
GO

/* =====================================================================
   154 — Fix the two manually-reviewed wrong-firm contacts (web-confirmed).
   ---------------------------------------------------------------------
   Migration 152 deliberately left these two for manual review because their
   surname is in their CURRENT firm name. Web research confirms BOTH are
   principals of their named firm and the foreign email is the error:

     Paul Fast (6165)  — Founder/Partner of Fast + Epp (confirmed, Wikipedia /
       fastepp.com / IStructE 2021 Gold Medal). Email p.fast@hcma.ca is wrong;
       his two HCMA affiliations (8675, 14063 -> org 8799) are wrong. Keep the
       two Fast + Epp affiliations (7955, 18510).
     Tom Clark (6931)  — Consulting Principal at Clark/Kjos Architects (FAIA,
       healthcare, 30+ yrs, LinkedIn). Email tclark@coarchitects.com is a wrong
       PatternInferred guess; the CO Architects affiliation (9058 -> 68871) is
       wrong. Keep the Clark/Kjos affiliation (9063 -> 68876).

   Action: null the wrong email on each (real verified emails to be sourced via
   the enrichment pass), and retire the wrong affiliations. Idempotent.
   ===================================================================== */

/* Null the wrong emails. */
UPDATE opportunities.IntelPerson
SET Email = NULL, EmailSource = NULL, EmailConfidence = NULL, EmailCheckedAtUtc = NULL
WHERE Id = 6165 AND Email = N'p.fast@hcma.ca';

UPDATE opportunities.IntelPerson
SET Email = NULL, EmailSource = NULL, EmailConfidence = NULL, EmailCheckedAtUtc = NULL
WHERE Id = 6931 AND Email = N'tclark@coarchitects.com';

/* Retire the wrong affiliations (keep the confirmed home firm). */
UPDATE opportunities.IntelPersonAffiliation
SET RetiredAtUtc = SYSDATETIMEOFFSET(),
    RetiredReason = N'Wrong firm — principal of named firm; foreign email error (migration 154)',
    UpdatedAtUtc = SYSDATETIMEOFFSET()
WHERE Id IN (8675, 14063, 9058) AND RetiredAtUtc IS NULL;
GO

/*
    KorOpportunitiesDb migration 376: a portal session a person signed in to once, kept alive by the Worker (21 October 2026).

    Alberta Purchasing Connection serves a posting's documents only to a signed-in supplier, and its sign-in
    (access.alberta.ca) sits behind Cloudflare Turnstile, which refuses an automated browser. So a person signs in once, in a
    plain browser window the app opens; the app reads the session's refresh token from that window and stores it here. The
    Worker renews it (grant_type=refresh_token, client apc-app) on a short schedule so the session does not go idle, stores
    each renewed token back, and uses the access token to download the documents of PURSUED APC tenders only.

    One row per portal. RefreshExpiresUtc is what the sign-in server said the last renewal is good for; when a renewal is
    refused, LastError says so and the row stays until a person signs in again.

    Idempotent; GO-batched.
*/
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'opportunities.PortalSession', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.PortalSession (
        Portal NVARCHAR(40) NOT NULL CONSTRAINT PK_PortalSession PRIMARY KEY CLUSTERED,
        Account NVARCHAR(200) NULL,
        RefreshToken NVARCHAR(MAX) NOT NULL,
        RefreshExpiresUtc DATETIMEOFFSET NULL,
        SignedInBy NVARCHAR(200) NOT NULL,
        SignedInAtUtc DATETIMEOFFSET NOT NULL,
        LastRenewedAtUtc DATETIMEOFFSET NULL,
        LastError NVARCHAR(400) NULL
    );
END
GO

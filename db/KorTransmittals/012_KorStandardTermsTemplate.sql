/*
012_KorStandardTermsTemplate.sql

One block template for the FeeProposal builder: a FreeText block called "Terms and Conditions" in a new
library category "Terms", carrying KOR standard terms assembled from the wording KOR's counsel (TSH)
settled in the executed Starlight agreement of 8 May 2026 (8.4 cap, 8.2 indemnity carve-out and
corporate-only claim, 9.3 IP reservation and licence, 8.1(h) subrogation on CGL only, 2.2 standard of
care) and from the one term KOR's own proposal paper already carries (the right to discontinue work).
Why: the Master Fee Proposal Template carries no limitation of liability, no consequential-damages
exclusion and no IP clause (checked 2026-10-28: 2,415 words, 4 mentions of "liability", all insurance),
so every countersigned proposal leaves KOR uncapped. Ian, 28 Oct, on a terms block: "yes".

Marked DRAFT in its name until Rory has read it; the builder shows it in the library pane under
"Terms" and a proposal takes it as a FreeText block. The block type FreeText already exists
(ProposalBlockType.FreeText; FreeTextBlockContent { Heading, Body }), so no app change is needed.
JSON shape = System.Text.Json of ProposalBlockTemplate with JsonStringEnumConverter, as
SqlProposalBlockLibraryStore writes it. MERGE on Id: re-running updates the same row.
*/
SET XACT_ABORT ON;
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRANSACTION;

DECLARE @Id NVARCHAR(64) = N'7b9c2f4e1d3a4c5b8e6f0a1b2c3d4e5f';
DECLARE @Name NVARCHAR(400) = N'Terms and Conditions - KOR standard (DRAFT from the Starlight agreement, for Rory)';
DECLARE @Category NVARCHAR(200) = N'Terms';
DECLARE @Now NVARCHAR(40) = CONVERT(NVARCHAR(40), SYSUTCDATETIME(), 127) + N'Z';

DECLARE @Body NVARCHAR(MAX) = N'1. Limitation of liability. To the maximum extent permitted by law, the cumulative liability of KOR Structural arising out of or in any way connected with this engagement is limited as follows: (a) for claims for which KOR is required to carry insurance under this agreement, to the limits of that insurance; and (b) for claims for which insurance is not required and/or does not apply, to the lesser of two times the fees payable under this agreement and $500,000. The existence of one or more claims does not increase the liability of KOR beyond the foregoing.\n\n'
 + N'2. Consequential damages. To the fullest extent permitted by law, neither the Client nor KOR is liable to the other for any incidental, indirect, punitive or consequential damages, including loss of revenue or profits, cost of capital, loss of use or opportunity, or cost of substitute facilities, goods or services.\n\n'
 + N'3. Indemnity. KOR will indemnify and save harmless the Client from claims to the extent caused by the negligent acts, errors or omissions of KOR in the performance of the services, excepting always claims to the extent caused by the errors, omissions or negligent acts of the Client, its other consultants or any other person. Any claim against KOR is limited to KOR Structural as a corporation, without liability on the part of any officer, director, employee or agent of KOR.\n\n'
 + N'4. Intellectual property. KOR reserves all right, title and interest in the drawings, specifications, calculations, models and other documents it prepares (the Records), including all copyright. The Client has an irrevocable, royalty-free licence to use the Records for the project for which KOR has been retained. KOR may rescind the licence only if the Client fails to pay the fees relating to the Records when due, on written notice, and not while a notified invoice dispute is unresolved.\n\n'
 + N'5. Insurance. KOR carries commercial general liability and professional liability insurance and will provide a certificate on request. Any waiver of subrogation or additional-insured status the Client requires applies to the commercial general liability policy only.\n\n'
 + N'6. Payment and suspension. Invoices are issued monthly and are due within 30 days. KOR may suspend the services on seven days written notice if an undisputed invoice is more than 30 days overdue, and is not responsible for any delay or damages arising from that suspension.\n\n'
 + N'7. Standard of care. KOR will perform the services with the care and skill that a qualified structural engineer would exercise under similar conditions, in accordance with accepted industry practice. No other warranty, express or implied, is made.';

DECLARE @Json NVARCHAR(MAX) = N'{'
 + N'"Id":"' + @Id + N'","Name":"' + @Name + N'","Category":"' + @Category + N'","BlockType":"FreeText",'
 + N'"CreatedAt":"' + @Now + N'","ModifiedAt":"' + @Now + N'",'
 + N'"Content":{"InstanceId":"' + @Id + N'","TemplateId":"' + @Id + N'","TemplateName":"' + @Name + N'","BlockType":"FreeText",'
 + N'"FreeText":{"Heading":"Terms and Conditions","Body":"' + @Body + N'"}}}';

IF ISJSON(@Json) <> 1 THROW 51012, 'The terms template JSON is not valid JSON.', 1;

MERGE dbo.ProposalBlockTemplates AS t
USING (VALUES (@Id, @Name, @Category, N'FreeText', @Json, SYSUTCDATETIME())) AS s (Id, Name, Category, BlockType, ContentJson, ModifiedAt)
   ON t.Id = s.Id
WHEN MATCHED THEN UPDATE SET Name = s.Name, Category = s.Category, BlockType = s.BlockType, ContentJson = s.ContentJson, ModifiedAt = s.ModifiedAt
WHEN NOT MATCHED THEN INSERT (Id, Name, Category, BlockType, ContentJson, ModifiedAt) VALUES (s.Id, s.Name, s.Category, s.BlockType, s.ContentJson, s.ModifiedAt);

COMMIT TRANSACTION;

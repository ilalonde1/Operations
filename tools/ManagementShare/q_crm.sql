SELECT e.Id, e.OpportunityId, e.Stage, e.OwnerStaffId, e.ProposedFee, e.Notes, e.OpenedAtUtc, e.ClosedAtUtc, e.OutcomeNotes,
       e.WonLostOutcome, e.OutcomeReason, e.WonProjectWbs1, e.ExternalSource, e.ExternalSourceKey, e.SubmittedFileName, e.FolderLink,
       e.Region, o.DisplayName AS BuyerName, op.Name AS OppTitle, op.ProjectAddress AS OppAddress, op.ProjectCity AS OppCity
FROM opportunities.CrmEngagements e
LEFT JOIN opportunities.CanonicalOrg o ON o.Id = e.BuyerCanonicalOrgId
LEFT JOIN opportunities.Opportunities op ON op.Id = e.OpportunityId
FOR JSON PATH

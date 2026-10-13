namespace Kor.Opportunities.Data.BdReports;

/// <summary>
/// SQL population rules shared by the report readers and integrity gates.
/// Fragments use fixed aliases/parameters; no user text is interpolated here.
/// </summary>
public static class BdReportPopulationSql
{
    // Caller SELECTs COUNT(DISTINCT p.Id) and adds the organisation predicate.
    public const string CurrentPeopleFrom = @"
FROM opportunities.IntelPersonAffiliation pa
JOIN opportunities.IntelPerson p ON p.Id = pa.IntelPersonId
WHERE pa.RetiredAtUtc IS NULL AND pa.IsCurrent = 1 AND p.RetiredAtUtc IS NULL";

    // Unknown deadlines cannot establish that an RFP is still live. Both queries
    // bind the same @asOf from the region brief read, including the expiry edge.
    public const string RegionLiveRfp = @"
Status = 1 AND IsPrimeConsultantRfp = 1 AND ProjectProvince = @prov
AND DismissedAtUtc IS NULL AND SubmissionDeadlineUtc > @asOf";

    public const string RegionForward = @"
RetiredAtUtc IS NULL AND Province = @prov
AND ProjectStage IN (N'CapitalPlan', N'FacilityRenewal')";
}

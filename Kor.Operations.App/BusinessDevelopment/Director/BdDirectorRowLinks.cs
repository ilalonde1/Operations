#nullable enable
using Kor.Operations.App.BusinessDevelopment.Workspace;
using Kor.Opportunities.Data.BdReports.Generators;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.MajorProjects;

namespace Kor.Operations.App.BusinessDevelopment.Director;

/// <summary>
/// Where a double-clicked Director row goes: the existing screen for the thing the row is about. A row tied to a pursuit
/// opens the pursuit; one tied only to a company opens its dossier. A row with nothing behind it opens nothing.
/// </summary>
public static class BdDirectorRowLinks
{
    public static string? For(object? row) => row switch
    {
        BdActionView a => PursuitOrOrg(a.Row.EngagementId, a.Row.CanonicalOrgId),
        BdDirectorPursuitRow p => KorReportLinks.Pursuit(p.PursuitId),
        BdDirectorDeadlineRow d => KorReportLinks.Pursuit(d.PursuitId),
        BdDirectorAccountRow a => KorReportLinks.Org(a.Account.CanonicalOrgId),
        BdDirectorAskRow a => PursuitOrOrg(a.Record.EngagementId, a.Record.CanonicalOrgId),
        BdDirectorDecisionRow d => PursuitOrOrg(d.Record.EngagementId, d.Record.CanonicalOrgId),
        BdDirectorSignal s => KorReportLinks.Org(s.OrgId),
        DashboardViewModel.DeadlineRow r => KorReportLinks.Opportunity(r.OpportunityId),
        BdForwardPipelineRow f => KorReportLinks.Mpi(f.Id),
        BdOpenStructuralSeatRow s => KorReportLinks.Org(s.OrgId),
        BdDirectorPendingBidRow b => KorReportLinks.Pursuit(b.Bid.EngagementId),
        BdPursuitLine l => KorReportLinks.Pursuit(l.EngagementId),
        _ => null,
    };

    private static string? PursuitOrOrg(long? engagementId, long? orgId) =>
        engagementId is > 0 ? KorReportLinks.Pursuit(engagementId) : KorReportLinks.Org(orgId);
}

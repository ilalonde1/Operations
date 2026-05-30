#nullable enable
using Kor.Opportunities.Data.Briefs;

namespace Kor.Operations.App.BusinessDevelopment.Briefs;

/// <summary>
/// Renders a Pursuit Brief data record into a styled .pdf at the given path.
/// Brand-matches <c>PursuitBriefPdfExporter</c> so every PDF in the app feels like
/// the same product. <c>IBriefGenerator</c> (DOCX) is the alternate format.
/// </summary>
public interface IBriefPdfGenerator
{
    void WriteOpportunityBrief(OpportunityBriefData data, string outputPath);

    void WriteRegionBrief(RegionBriefData data, string outputPath);

    void WriteOrgBrief(OrgBriefData data, string outputPath);
}

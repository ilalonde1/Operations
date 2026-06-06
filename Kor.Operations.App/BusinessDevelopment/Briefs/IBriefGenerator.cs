#nullable enable
using Kor.Opportunities.Data.Briefs;

namespace Kor.Operations.App.BusinessDevelopment.Briefs;

/// <summary>
/// Renders a Pursuit Brief data record into a styled .docx file at the given path.
/// Phase 1B uses rule-based templates that mirror the hand-built brief; Phase 2 will
/// swap an AI narrative generator in behind the same interface.
/// </summary>
public interface IBriefGenerator
{
    void WriteOpportunityBrief(OpportunityBriefData data, string outputPath);

    void WriteProjectBrief(ProjectBriefData data, string outputPath);

    void WritePersonBrief(PersonBriefData data, string outputPath);

    void WriteRegionBrief(RegionBriefData data, string outputPath);

    void WriteOrgBrief(OrgBriefData data, string outputPath);
}

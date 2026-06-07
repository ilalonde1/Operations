#nullable enable

namespace Kor.Opportunities.Data.MajorProjects;

public static class ProjectStageRouter
{
    public static readonly IReadOnlyList<string> CanonicalLifecycleStages =
    [
        "CapitalPlan",
        "Planned",
        "Concept",
        "Design",
        "Permitting",
        "Procurement",
        "Approved",
        "Announced",
        "Construction",
    ];

    private static readonly Dictionary<string, string> LifecycleStages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CapitalPlan"] = "CapitalPlan",
        ["PublicCapitalPlan"] = "CapitalPlan",
        ["Years 4-5 priority"] = "CapitalPlan",
        ["planning"] = "Planned",
        ["Planned"] = "Planned",
        ["Pre-design"] = "Concept",
        ["Concept"] = "Concept",
        ["Preliminary/Feasibility"] = "Concept",
        ["design"] = "Design",
        ["Design-RFP-Open"] = "Design",
        ["Consultation/Approvals"] = "Permitting",
        ["Permitting"] = "Permitting",
        ["Procurement"] = "Procurement",
        ["PreTender"] = "Procurement",
        ["Tender/Preconstruction"] = "Procurement",
        ["RFP-issued"] = "Procurement",
        ["Approved"] = "Approved",
        ["Approved-Funded"] = "Approved",
        ["Announced-Funded"] = "Approved",
        ["capital-approved"] = "Approved",
        ["funding-approved"] = "Approved",
        ["Announced"] = "Announced",
        ["under construction"] = "Construction",
    };

    private static readonly Dictionary<string, string> PipelineTags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["USMarketResearch"] = "USMarketResearch",
        ["AlbertaMarketResearch"] = "AlbertaMarketResearch",
        ["LowerMainlandPairing"] = "LowerMainlandPairing",
        ["IslandOkanaganPairing"] = "IslandOkanaganPairing",
        ["EdmontonPairing"] = "EdmontonPairing",
        ["IslandOkanaganEcosystem"] = "IslandOkanaganEcosystem",
        ["InstitutionalPipeline"] = "InstitutionalPipeline",
        ["structural-pipeline"] = "structural-pipeline",
        ["OwnerPipeline"] = "OwnerPipeline",
        ["Seismic"] = "Seismic",
        ["FacilityRenewal"] = "FacilityRenewal",
        ["competitor-projects"] = "competitor-projects",
        ["indigenous-projects"] = "indigenous-projects",
        ["project-teams"] = "project-teams",
        ["IntelTeamAwards"] = "IntelTeamAwards",
        ["Indigenous"] = "Indigenous",
    };

    public static void Route(
        string? rawStage,
        out string? canonicalStage,
        out string? korPipelineTag,
        out string? routeNote)
    {
        canonicalStage = null;
        korPipelineTag = null;
        routeNote = null;

        if (string.IsNullOrWhiteSpace(rawStage))
        {
            return;
        }

        string trimmed = rawStage.Trim();
        if (PipelineTags.TryGetValue(trimmed, out string? matchedTag))
        {
            korPipelineTag = matchedTag;
            routeNote = "pipeline-tag";
            return;
        }

        if (LifecycleStages.TryGetValue(trimmed, out string? matchedStage))
        {
            canonicalStage = matchedStage;
            routeNote = "lifecycle";
            return;
        }

        routeNote = $"unmapped: '{rawStage}'";
    }
}

#nullable enable
namespace Kor.Opportunities.Data.Intel;

/// <summary>
/// Single source of truth for converting Intel type discriminators (string
/// values stored in SignalType / ActionType / RiskType columns) into the
/// human-readable labels shown on briefs (PDF + DOCX), the in-app Dossier,
/// and any future surface. Adding a new SignalType / ActionType / RiskType
/// requires only adding it to the schema CHECK list (if any) and the
/// switch arm below. Three previous copies of these maps were merged here
/// (PDF, DOCX, Dossier XAML converter).
/// </summary>
public static class IntelTypeHumanizer
{
    public static string SignalType(string raw) => raw switch
    {
        "LeadershipChange" => "Leadership change",
        "HiringSurge" => "Hiring surge",
        "OfficeMove" => "Office move",
        "OwnershipMnA" => "Ownership / M&A",
        "CapacityStrain" => "Capacity strain",
        "RecentWin" => "Recent win",
        _ => raw,
    };

    public static string ActionType(string raw) => raw switch
    {
        "ContactStrategy" => "Contact strategy",
        "PursuitAngle" => "Pursuit angle",
        "TimingWindow" => "Timing window",
        "HowToGetOnRoster" => "How to get on roster",
        "KorDisplacementRead" => "Displacement read",
        _ => raw,
    };

    public static string RiskType(string raw) => raw switch
    {
        "CapacityStrain" => "Capacity strain",
        "KeyPersonDependency" => "Key-person dependency",
        "OwnershipUncertainty" => "Ownership uncertainty",
        "ExploitableWeakness" => "Exploitable weakness",
        "DataIssue" => "Data issue",
        _ => raw,
    };

    public static string Confidence(IntelConfidence c) => c switch
    {
        IntelConfidence.High => "High",
        IntelConfidence.Medium => "Medium",
        IntelConfidence.Low => "Low",
        _ => c.ToString(),
    };

    public static string Freshness(IntelFreshness f) => f switch
    {
        IntelFreshness.Fresh => "Fresh",
        IntelFreshness.Aged => "Aged",
        IntelFreshness.Stale => "Stale",
        _ => f.ToString(),
    };
}

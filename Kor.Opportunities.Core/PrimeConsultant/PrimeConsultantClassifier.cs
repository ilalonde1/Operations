#nullable enable
using System;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Core.PrimeConsultant;

public sealed record PrimeRfpClassification(
    bool IsPrimeConsultantRfp,
    decimal Confidence,
    string? DisciplineType,
    string? ProjectSector,
    string? LikelyPrimeType,
    bool KorSectorMatch,
    bool KorLocationMatch);

public static class PrimeConsultantClassifier
{
    private static readonly string[] RfpSignals =
    {
        "rfp",
        "request for proposal",
    };

    private static readonly string[] StrongArchitecturalSignals =
    {
        "prime consultant",
        "architectural services",
        "architectural consulting",
        "architectural design",
        "architect-engineer",
        "architect or engineer",
        "a/e services",
        "a-e services",
        // US-portal phrasings (e.g. LA RAMP) where the design disciplines are
        // listed together. Specific enough to stay high-precision — they do NOT
        // match construction trades like "architectural sheet metal".
        "architectural, engineering",
        "architectural and engineering",
        "architecture and engineering",
        "engineering and architectural",
        "architectural/engineering",
    };

    private static readonly string[] WeakAeSignals =
    {
        "consulting services",
        "consulting engineering",
        "engineering services",
        "engineering consultant",
        "design services",
        "design service",
        "design consultant",
        "design consultants",
        "building design",
    };

    private static readonly string[] ArchitectExclusionSignals =
    {
        "solutions architect",
        "software architect",
        "enterprise architect",
        "data architect",
        "systems architect",
        "landscape architect",
    };

    private static readonly string[] BuildingSignals =
    {
        "school",
        "hospital",
        "health",
        "building",
        "facility",
        "renovation",
        "feasibility",
        "recreation",
        "rec centre",
        "recreational centre",
        "aquatic",
        "arena",
        "library",
        "civic",
        "courthouse",
        "fire hall",
        "campus",
        "university",
        "housing",
        "clinic",
        "museum",
        "theatre",
        "theater",
        "community centre",
        "community center",
        "cultural centre",
        "cultural center",
        "recreation centre",
        "recreation center",
        "pavilion",
        "daycare",
        "childcare",
        "child care",
        "care home",
        "long-term care",
        "long term care",
        "fire station",
        "police station",
        "city hall",
        "town hall",
        "venue",
        "gymnasium",
    };

    // Specific, unambiguous building facilities. Unlike BuildingSignals these
    // exclude generic words ("building", "facility", "renovation") so that
    // facility + bare "design" (Tier 2b) does NOT fire on "building envelope
    // design", "landscape design", etc.
    private static readonly string[] StrongFacilitySignals =
    {
        "school", "hospital", "clinic", "library", "museum", "theatre", "theater",
        "community centre", "community center", "cultural centre", "cultural center",
        "recreation centre", "recreation center", "recreational centre", "rec centre",
        "aquatic", "arena", "courthouse", "fire hall", "fire station", "police station",
        "campus", "university", "daycare", "childcare", "child care", "care home",
        "long-term care", "long term care", "pavilion", "venue", "gymnasium",
        "city hall", "town hall", "civic centre", "housing",
    };

    private static readonly string[] DesignSignals = { "design" };

    private static readonly string[] NoiseSignals =
    {
        "supply chain",
        "process analysis",
        "forcemain",
        "water main",
        "watermain",
        "sanitary",
        "road",
        "pipeline",
        "tree master plan",
        "traffic",
        "bridge",
    };

    // Phrases where a building-sector keyword ("building") is a FALSE match —
    // it's a verb phrase ("building capacity"), not a facility. These must
    // exclude ALWAYS, even though hasBuilding is true.
    private static readonly string[] FalseBuildingSignals =
    {
        "building capacity",
        "capacity building",
    };

    // Always-exclude regardless of building/facility signals. These are project
    // types that are NOT architect-led prime-consultant work: design-build
    // (contractor is prime), MEP/fire sub-scopes, software/BIM, planning/strategy
    // studies, and creative-design noise. Confirmed by the data-honing pass
    // (false positives: ids 468 BIM, 561/640 design-build, 718 fire system,
    // 10803 housing strategy).
    private static readonly string[] HardNoiseSignals =
    {
        "design-build",
        "design build",
        "(bim)",
        "building information mod",
        "common data environment",
        "fire system",
        "fire suppression",
        "sprinkler",
        "housing strategy",
        "graphic design",
        "website design",
        "web design",
        "photography",
    };

    public static PrimeRfpClassification Classify(Opportunity o)
    {
        ArgumentNullException.ThrowIfNull(o);

        var text = $"{o.Name} {o.ProjectCategory}".ToLowerInvariant();
        var title = (o.Name ?? string.Empty).ToLowerInvariant();
        var hasRfp = ContainsAny(title, RfpSignals);
        var hasStrongArchitecturalSignal = ContainsAny(title, StrongArchitecturalSignals);
        var hasWeakAeSignal = ContainsAny(title, WeakAeSignals);
        var publicBuyer = IsPublicBuyer(o.BuyerType);
        var buildingSignalCount = CountMatches(text, BuildingSignals);
        var hasBuilding = buildingSignalCount > 0;
        var hasArchitectExclusion = ContainsAny(title, ArchitectExclusionSignals);
        var excluded = ContainsAny(text, FalseBuildingSignals)
            || ContainsAny(text, HardNoiseSignals)
            || (!hasBuilding && ContainsAny(title, NoiseSignals))
            || hasArchitectExclusion;

        var hasStrongFacility = ContainsAny(text, StrongFacilitySignals);
        var hasDesign = ContainsAny(text, DesignSignals);

        var sector = ClassifySector(text);
        var likelyPrimeType = ClassifyLikelyPrimeType(title);
        var tier1 = hasStrongArchitecturalSignal && !hasArchitectExclusion;
        var tier2 = hasWeakAeSignal && hasBuilding && !excluded;
        // Tier 2b: a specific facility + the word "design" (e.g. "Tofino Library
        // ... Design", "Clinic Relocation Design"). Restricted to StrongFacility
        // (not generic "building") to keep precision.
        var tier2b = hasStrongFacility && hasDesign && !excluded;
        var isPrime = tier1 || tier2 || tier2b;
        var confidence = CalculateConfidence(
            tier1,
            tier2 || tier2b,
            hasRfp,
            publicBuyer,
            buildingSignalCount,
            hasStrongArchitecturalSignal,
            hasWeakAeSignal);

        return new PrimeRfpClassification(
            isPrime,
            confidence,
            ClassifyDisciplineType(o, likelyPrimeType),
            sector,
            likelyPrimeType,
            IsKorSectorMatch(sector),
            IsKorLocationMatch(o.ProjectProvince));
    }

    private static bool ContainsAny(string value, string[] needles)
    {
        foreach (var needle in needles)
        {
            if (value.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static int CountMatches(string value, string[] needles)
    {
        var count = 0;
        foreach (var needle in needles)
        {
            if (value.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }

    private static decimal CalculateConfidence(
        bool tier1,
        bool tier2,
        bool hasRfp,
        bool publicBuyer,
        int buildingSignalCount,
        bool hasStrongArchitecturalSignal,
        bool hasWeakAeSignal)
    {
        decimal confidence;
        if (tier1)
        {
            confidence = 0.80m;
        }
        else if (tier2)
        {
            confidence = 0.60m;
        }
        else
        {
            confidence = 0.10m;
        }

        if (hasRfp) confidence += 0.05m;
        if (publicBuyer) confidence += 0.05m;
        if (buildingSignalCount > 1) confidence += 0.05m;
        if (tier2 && hasStrongArchitecturalSignal) confidence += 0.05m;
        if (tier1 && hasWeakAeSignal) confidence += 0.03m;

        return decimal.Round(Math.Min(confidence, 0.99m), 3);
    }

    private static bool IsPublicBuyer(BuyerType buyerType)
        => buyerType is BuyerType.Municipal
            or BuyerType.Provincial
            or BuyerType.Federal
            or BuyerType.InstitutionalEducation
            or BuyerType.InstitutionalHealthcare;

    private static string ClassifySector(string text)
    {
        if (text.Contains("school", StringComparison.OrdinalIgnoreCase)) return "school";
        if (text.Contains("hospital", StringComparison.OrdinalIgnoreCase) || text.Contains("health", StringComparison.OrdinalIgnoreCase)) return "health";
        if (text.Contains("housing", StringComparison.OrdinalIgnoreCase)) return "housing";
        if (text.Contains("courthouse", StringComparison.OrdinalIgnoreCase) || text.Contains("correction", StringComparison.OrdinalIgnoreCase) || text.Contains("jail", StringComparison.OrdinalIgnoreCase)) return "corrections";
        if (text.Contains("library", StringComparison.OrdinalIgnoreCase)) return "library";
        if (text.Contains("recreation", StringComparison.OrdinalIgnoreCase) || text.Contains("rec centre", StringComparison.OrdinalIgnoreCase) || text.Contains("aquatic", StringComparison.OrdinalIgnoreCase) || text.Contains("arena", StringComparison.OrdinalIgnoreCase)) return "recreational";
        if (text.Contains("campus", StringComparison.OrdinalIgnoreCase) || text.Contains("university", StringComparison.OrdinalIgnoreCase)) return "university";
        if (text.Contains("civic", StringComparison.OrdinalIgnoreCase) || text.Contains("government", StringComparison.OrdinalIgnoreCase) || text.Contains("office", StringComparison.OrdinalIgnoreCase) || text.Contains("fire hall", StringComparison.OrdinalIgnoreCase)) return "government_office";
        return "other";
    }

    private static string ClassifyLikelyPrimeType(string title)
    {
        if (title.Contains("architect", StringComparison.OrdinalIgnoreCase)) return "architectural";
        if (title.Contains("engineer", StringComparison.OrdinalIgnoreCase)) return "engineering";
        return "unknown";
    }

    private static string? ClassifyDisciplineType(Opportunity o, string likelyPrimeType)
        => o.Discipline == OpportunityDiscipline.Unknown
            ? likelyPrimeType
            : o.Discipline.ToString();

    private static bool IsKorSectorMatch(string sector)
        => sector is "school" or "health" or "government_office" or "housing" or "university" or "recreational";

    private static bool IsKorLocationMatch(string? province)
        => province?.Trim().ToUpperInvariant() is "BC" or "AB" or "SK" or "CA" or "WA" or "OR";
}

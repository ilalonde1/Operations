#nullable enable
using System;
using System.Text.RegularExpressions;

namespace Kor.Opportunities.Core.Ingestion;

public sealed record RelevanceDecision(bool Keep, string? RejectReason);

public static class StructuralRelevanceGate
{
    // v1 relevance vocabulary: tuned to reject clearly non-building intake while
    // keeping ambiguous candidates for downstream scoring/review.
    private static readonly string[] BuildingSignals =
    {
        "building",
        "buildings",
        "construction",
        "construct",
        "renovation",
        "renovate",
        "addition",
        "expansion",
        "retrofit",
        "seismic",
        "structural",
        "structure",
        "facility",
        "school",
        "hospital",
        "health centre",
        "clinic",
        "medical centre",
        "library",
        "museum",
        "theatre",
        "theater",
        "community centre",
        "community center",
        "cultural centre",
        "recreation",
        "rec centre",
        "aquatic",
        "arena",
        "gymnasium",
        "courthouse",
        "fire hall",
        "fire station",
        "police station",
        "campus",
        "university",
        "college",
        "housing",
        "residential",
        "condominium",
        "apartment",
        "mixed-use",
        "institutional",
        "civic",
        "city hall",
        "town hall",
        "daycare",
        "childcare",
        "care home",
        "care centre",
        "care center",
        "care facility",
        "long-term care",
        "seniors housing",
        "assisted living",
        "pavilion",
        "tower",
        "high-rise",
        "highrise",
        "parkade",
        "parking structure",
        "tenant improvement",
        "building envelope",
        "bridge",
        "footbridge",
        "pedestrian bridge",
        "overpass",
        "stadium",
        "grandstand",
        "terminal",
        "hangar",
        "warehouse",
        "service centre",
        "service center",
        "operations centre",
        "operations center",
        "works yard",
        "operations yard",
        "public works yard",
        "transit centre",
        "transit center",
        "transit exchange",
        "bus depot",
        "depot",
        "maintenance facility",
    };

    private static readonly string[] HardIrrelevantSignals =
    {
        "road",
        "roadway",
        "paving",
        "pavement",
        "asphalt",
        "sidewalk",
        "curb and gutter",
        "guardrail",
        "line painting",
        "pavement marking",
        "crack seal",
        "streetlight",
        "street light",
        "traffic signal",
        "snow removal",
        "snow clearing",
        "street sweeping",
        "water main",
        "watermain",
        "sewer",
        "sanitary",
        "storm sewer",
        "stormwater",
        "culvert",
        "forcemain",
        "pipeline",
        "hydrant",
        "lift station",
        "pump station",
        "irrigation",
        "software",
        "saas",
        "software license",
        "computer hardware",
        "laptop",
        "desktop",
        "network equipment",
        "it services",
        "it support",
        "help desk",
        "helpdesk",
        "telecom",
        "telecommunication",
        "fibre optic",
        "fiber optic",
        "wi-fi",
        "cyber security",
        "cybersecurity",
        "website",
        "web development",
        "application development",
        "erp",
        "janitorial",
        "custodial",
        "cleaning services",
        "housekeeping",
        "landscaping",
        "lawn",
        "grounds maintenance",
        "groundskeeping",
        "arborist",
        "tree pruning",
        "tree removal",
        "pest control",
        "waste collection",
        "garbage",
        "refuse",
        "recycling collection",
        "security guard",
        "security services",
        "catering",
        "food service",
        "uniform",
        "linen",
        "laundry service",
        "supply of",
        "supply and delivery",
        "office supplies",
        "equipment rental",
        "vehicle",
        "vehicles",
        "fleet",
        "fuel",
        "furniture",
        "appliance",
        "printing services",
        "stationery",
        "personal protective",
        "ppe",
        "tires",
        "advertising",
        "translation services",
        "photography",
        "graphic design",
        "legal services",
        "audit services",
        "payroll",
        "insurance services",
        "staffing",
        "temporary labour",
        "recruitment",
    };

    private static readonly string[] AlwaysIrrelevantSignals =
    {
        "mine",
        "mines",
        "mining",
        "open pit",
        "ore",
        "tailings",
        "smelter",
        "smelting",
        "coal",
        "lng",
        "natural gas",
        "gas plant",
        "gas processing",
        "gas transmission",
        "ngl plant",
        "refinery",
        "oil sands",
        "petrochemical",
        "hydroelectric",
        "hydro dam",
        "wind farm",
        "solar farm",
        "power plant",
        "power station",
        "transmission line",
        "substation",
        "wastewater",
        "sewage",
        "water treatment plant",
        "container terminal",
        "marine terminal",
        "port expansion",
        "nickel",
        "copper",
        "gold mine",
        "gold project",
        "porphyry",
        "molybdenum",
        "potash",
        "pulp mill",
        "sawmill",
        "biocoal",
        "biofuel",
        "biocarbon",
        "biomass",
        "aggregate",
        "quarry",
        "gravel pit",
        "lpg",
        "liquefied petroleum",
        "petroleum",
        "energy export",
        "export terminal",
        "export facility",
        "grinding facility",
        "lime project",
    };

    private static readonly string[] ProfessionalSignals =
    {
        "prime consultant",
        "architect",
        "architectural",
        "architectural services",
        "architectural consulting",
        "architectural design",
        "a/e services",
        "a-e services",
        "consulting engineering",
        "engineering services",
        "engineering consultant",
        "design services",
        "design consultant",
        "building design",
        "building envelope",
        "feasibility study",
    };

    private static readonly Regex[] HardIrrelevantRegexes = HardIrrelevantSignals
        .Select(signal => new Regex($@"\b{Regex.Escape(signal)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled))
        .ToArray();

    private static readonly Regex[] AlwaysIrrelevantRegexes = AlwaysIrrelevantSignals
        .Select(signal => new Regex($@"\b{Regex.Escape(signal)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled))
        .ToArray();

    public static RelevanceDecision Evaluate(string? title, string? description, string? buyer)
    {
        _ = buyer;

        var text = $"{title ?? string.Empty} {description ?? string.Empty}".ToLowerInvariant();
        var matchedAlwaysIrrelevant = FirstAlwaysIrrelevantMatch(text);
        if (matchedAlwaysIrrelevant is not null)
        {
            return new RelevanceDecision(false, $"out-of-lane: {matchedAlwaysIrrelevant}");
        }

        var hasBuilding = ContainsAny(text, BuildingSignals);
        var hasKeep = hasBuilding || ContainsAny(text, ProfessionalSignals);
        var matchedIrrelevant = FirstHardIrrelevantMatch(text);

        // Hard-irrelevant first (more specific reason), but only fatal when no
        // building signal overrides it. Then the allowlist catch-all.
        if (matchedIrrelevant is not null && !hasBuilding)
        {
            return new RelevanceDecision(false, matchedIrrelevant);
        }

        if (!hasKeep)
        {
            return new RelevanceDecision(false, "no building/structural/design signal");
        }

        return new RelevanceDecision(true, null);
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

    private static string? FirstHardIrrelevantMatch(string value)
    {
        for (var i = 0; i < HardIrrelevantSignals.Length; i++)
        {
            if (HardIrrelevantRegexes[i].IsMatch(value))
            {
                return HardIrrelevantSignals[i];
            }
        }

        return null;
    }

    private static string? FirstAlwaysIrrelevantMatch(string value)
    {
        for (var i = 0; i < AlwaysIrrelevantSignals.Length; i++)
        {
            if (AlwaysIrrelevantRegexes[i].IsMatch(value))
            {
                return AlwaysIrrelevantSignals[i];
            }
        }

        return null;
    }
}

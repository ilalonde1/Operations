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
        // Reject-table review 2026-07-01 (opportunities.RelevanceGateRejects):
        "municipal hall",
        "roof replacement",
        "re-roof",
        "reroof",
        "elevator modernization",
        "elevator replacement",
        "elevator upgrade",
        "pre-engineered",
        // Planning-application vocabulary, added 2026-09-03. Everything above is
        // TENDER language ("construction of", "renovation"); municipal
        // development-permit and rezoning applications speak planning instead,
        // and the gate was throwing them away — 42 of Victoria's 146 and 810 of
        // Maple Ridge's 849, including a six-storey hotel, a six-storey 42-unit
        // rental and a heritage building relocation.
        //
        // Every term here was proved on BOTH arms of tools/RelevanceGateDiff
        // before it shipped: zero regressions across the 3,803 kept rows, and
        // examples read by eye on the live planning corpus. Four candidates were
        // KILLED by that harness and are deliberately absent — "retail" (16
        // commodity hits: lottery terminals, shelving, packaging), "restaurant"
        // (golf-clubhouse concession operations), "triplex" (a triplex mower is
        // a golf-course machine) and "office building"/"industrial building"
        // (redundant: "building" already matches). Do not re-add them without
        // re-running that harness.
        "storey",
        "multi-family",
        "multifamily",
        // The hyphenated "mixed-use" is above; it does not match "mixed uses",
        // which is how Maple Ridge writes it on 12 of its rezonings.
        "mixed use",
        "townhouse",
        "townhome",
        "rowhouse",
        "row house",
        "duplex",
        "fourplex",
        "sixplex",
        "houseplex",
        "multiplex",
        "dwelling",
        "garden suite",
        "secondary suite",
        "laneway house",
        "live-work",
        "purpose-built rental",
        "hotel",
        "motel",
        "place of worship",
        // French vocabulary — CanadaBuys federal postings can be French-only;
        // accented and unaccented variants both included because upstream
        // encodings vary.
        "bâtiment",
        "batiment",
        "immeuble",
        "école",
        "ecole",
        "hôpital",
        "hopital",
        "logement",
        "agrandissement",
        "charpente",
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
        // NOT bare "coal": Vancouver's Coal Harbour neighbourhood collides
        // (\bcoal\b killed "Coal Harbour Phase 2 - Construction Manager
        // Pre-Qualification" — a real pursuit). Industrial coal work always
        // arrives with one of these qualifiers.
        "coal mine",
        "coal mining",
        "coal terminal",
        "coal-fired",
        "coal fired",
        "coal handling",
        "coal export",
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
        "engineering consulting",
        "engineering services",
        "engineering consultant",
        "ingénierie",
        "ingenierie",
        "génie-conseil",
        "genie-conseil",
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

    // Audit-v2 #14: the keep-lists previously matched raw substrings while the
    // reject-lists used word boundaries — so 'infrastructure' satisfied the
    // building signal 'structure' and 'additional' satisfied 'addition', letting
    // out-of-lane work pass the gate and (on the award path) mint canonical orgs
    // for commodity vendors. Keep-signals are now word-bounded too, with a short
    // morphological suffix (s/es/d/ed/ing) so verb stems keep their coverage
    // ('constructing', 'renovated', 'towers') without substring false-passes.
    private static readonly Regex[] BuildingSignalRegexes = BuildingSignals
        .Select(signal => new Regex($@"\b{Regex.Escape(signal)}(?:s|es|d|ed|ing)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled))
        .ToArray();

    private static readonly Regex[] ProfessionalSignalRegexes = ProfessionalSignals
        .Select(signal => new Regex($@"\b{Regex.Escape(signal)}(?:s|es|d|ed|ing)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled))
        .ToArray();

    public static RelevanceDecision Evaluate(string? title, string? description, string? buyer)
        => Evaluate(title, description, buyer, delta: null);

    /// <summary>
    /// As <see cref="Evaluate(string?, string?, string?)"/>, but scoring a
    /// PROPOSED vocabulary addition alongside the shipped one. Production always
    /// passes null; a non-null <paramref name="delta"/> is the differential arm
    /// of <c>tools/RelevanceGateDiff</c>. A delta can only ever turn a reject
    /// into a keep — it adds keep-signals and never exclusions — which is what
    /// makes the regression side of that diff assertable.
    /// </summary>
    public static RelevanceDecision Evaluate(
        string? title,
        string? description,
        string? buyer,
        RelevanceVocabularyDelta? delta)
    {
        // Deliberately unused (audit-v2 #14 reviewed this): folding the buyer name
        // into the keep-scan would false-pass out-of-lane work from building-sector
        // buyers ("University of X" + "parking lot repaving"). Relevance is judged
        // on what is being procured, not who is buying it.
        _ = buyer;

        var text = $"{title ?? string.Empty} {description ?? string.Empty}".ToLowerInvariant();
        var matchedAlwaysIrrelevant = FirstAlwaysIrrelevantMatch(text);
        if (matchedAlwaysIrrelevant is not null)
        {
            return new RelevanceDecision(false, $"out-of-lane: {matchedAlwaysIrrelevant}");
        }

        var hasBuilding = MatchesAny(text, BuildingSignalRegexes)
                          || (delta?.MatchesBuilding(text) ?? false);
        var hasKeep = hasBuilding
                      || MatchesAny(text, ProfessionalSignalRegexes)
                      || (delta?.MatchesProfessional(text) ?? false);
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

    private static bool MatchesAny(string value, Regex[] patterns)
    {
        foreach (var pattern in patterns)
        {
            if (pattern.IsMatch(value))
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

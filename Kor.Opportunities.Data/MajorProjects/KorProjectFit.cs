#nullable enable
using System.Text.RegularExpressions;

namespace Kor.Opportunities.Data.MajorProjects;

/// <summary>
/// Is a future project (MajorProjectsInventory) a building a structural engineering firm would design? The forward-pipeline
/// twin of <see cref="Ingestion.KorBidFit"/>, read from the project's sector and name together, because neither decides
/// alone: the "Infrastructure" sector holds 85 wind farms, hydro projects, roads and port terminals, and also transit
/// centres, a fire hall and an administration building.
///
/// A building sector (school, residential, health, civic, and unknown or master plan) is trusted, except for an energy
/// project named as one. A project in an infrastructure, utility, transport or industrial sector is shown only when its
/// name says it is a building. Measured 2026-10-21 on all 2,616 active projects: 2,533 shown and 83 hidden, none of the
/// hidden from a building sector; the 19 shown from an infrastructure sector were read by hand and are all buildings
/// (transit centres, a fire hall, an administration building). Not caught: a building named only by its address in an
/// infrastructure sector is hidden, and a power or port project filed under a building sector without the word "wind",
/// "hydro", "solar" or similar is shown ("South Richmond Terminal Project", Commercial).
/// </summary>
public static class KorProjectFit
{
    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    private static readonly Regex Energy = new(
        @"\b(wind\s+(farm|park|project|power|energy|turbine\w*)|hydro(power)?\s+project|solar\s+project|substation|(oil|gas|crude|lng)\s+pipeline|power\s+(plant|project|station)|geothermal|biomass|bioenergy|lng)\b",
        Opts);

    private static readonly Regex InfrastructureSector = new(
        // Matches the start of the sector, so a composite ("Infrastructure / Utilities", "Utility / Infrastructure") counts
        // (audit, 2026-10-21: the anchored form trusted any composite it did not spell out).
        @"^(infrastructure|utilit(y|ies)|transit|transport\w*|agriculture|industrial|economic-development|cross-sector|other\s+sectors|energy|mining|oil|gas)\b",
        Opts);

    private static readonly Regex Building = new(
        @"\b(centre|center|building|hall|facilit(y|ies)|housing|hangar|lab|school|hospital|village|office|workplace|library|museum|arena|pool|aquatic\w*|recreation|courthouse|clinic)\b",
        Opts);

    // Named as a building, but plant: a waste centre, a composting facility at a landfill, a dam's release facility.
    private static readonly Regex Plant = new(@"\b(waste|generators?|landfill|compost\w*|resource\s+recovery|dam)\b", Opts);

    public static bool IsKorProject(string? sector, string? name)
    {
        var n = (name ?? "").Trim();
        if (Energy.IsMatch(n)) return false;
        if (InfrastructureSector.IsMatch((sector ?? "").Trim()))
            return Building.IsMatch(n) && !Plant.IsMatch(n);
        return true;
    }
}

using Kor.Opportunities.Data.MajorProjects;
using Xunit;

namespace Kor.Opportunities.Data.Tests;

// The Future Projects rule on the projects Ian saw on screen (2026-10-21) and the cases that shaped it: wind, hydro, tunnels
// and substations out; transit centres and fire halls filed under "Infrastructure" in; school capital-plan "pipelines" and
// housing on Terminal Avenue in. Covers sector and name only, never the description; a building named only by its address
// in an infrastructure sector is hidden and this does not catch it.
public sealed class KorProjectFitTests
{
    [Theory]
    [InlineData("Infrastructure", "Fraser River Tunnel Project", false)]
    [InlineData("Infrastructure", "NaiKun Wind Power Project", false)]
    [InlineData("Infrastructure", "Rocky Creek Wind Energy Project", false)]
    [InlineData("Infrastructure", "Highland Valley Centre for Sustainable Waste Management", false)]
    [InlineData("Utilities", "West End Underground Substation", false)]
    [InlineData("Industrial", "Aspen Point Program", false)]
    [InlineData("Infrastructure", "Kenney Dam Cold Water Release Facility", false)]
    [InlineData("Infrastructure", "Burnaby Transit Centre", true)]
    [InlineData("Infrastructure", "South Fire Hall Replacement", true)]
    [InlineData("Infrastructure", "Sangudo Municipal Administration Building", true)]
    [InlineData("School", "2026-27 Capital Plan Pipeline (27 new schools, 21 additions, 25 site acquisitions, 2 replacements)", true)]
    [InlineData("Housing", "250 Terminal Avenue Supportive and Affordable Housing", true)]
    [InlineData("Residential", "West Railway Street Condos", true)]
    [InlineData(null, "Surrey Memorial Hospital New Acute Care Tower", true)]
    // Adversarial audit, 2026-10-21: a library in an infrastructure sector, and composite or unlisted infrastructure sectors.
    [InlineData("Infrastructure", "Riverbend Library Expansion", true)]
    [InlineData("Infrastructure / Utilities", "Eastside Trunk Sewer Renewal", false)]
    [InlineData("Energy", "Ridge Wind Turbine Array", false)]
    [InlineData("Mining", "Copper Pit Expansion", false)]
    [InlineData("Transport", "Highway 1 Interchange Reconstruction", false)]
    [InlineData("Civic", "Substation Road Community Hall", true)]
    [InlineData("Transportation", "Airport Terminal Expansion", true)]
    [InlineData("Industrial", "Waste Services Administration Building", true)]
    [InlineData("Utilities", "Water Treatment Facility Expansion", false)]
    [InlineData("Infrastructure", "Water Supply Pipeline to School Road", false)]
    public void Shows_buildings_and_keeps_energy_roads_and_plant_out(string? sector, string name, bool ours)
        => Assert.Equal(ours, KorProjectFit.IsKorProject(sector, name));
}

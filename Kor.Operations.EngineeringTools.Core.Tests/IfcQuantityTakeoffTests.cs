#nullable enable

using System.Linq;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class IfcQuantityTakeoffTests
{
    // A hand-authored, exporter-realistic IFC fragment: a metre/cubic-metre model with one storey holding a
    // suspended slab (12 m³), a wall (5 m³), a column (2 m³); a base-slab mat (40 m³) that must read as a
    // Foundation; an un-quantified beam (no NetVolume → residual); and one modelled rebar (0.01 m³ steel).
    private const string Ifc = @"ISO-10303-21;
HEADER;
ENDSEC;
DATA;
#1=IFCSIUNIT(*,.LENGTHUNIT.,$,.METRE.);
#2=IFCSIUNIT(*,.VOLUMEUNIT.,$,.CUBIC_METRE.);
#10=IFCBUILDINGSTOREY('s2guid',$,'Level 2',$,$,$,$,$,.ELEMENT.,3000.);
#20=IFCSLAB('slabguid',$,'Floor:200mm',$,'Floor',$,$,'A-201',.FLOOR.);
#21=IFCWALL('wallguid',$,'Core Wall',$,'Wall',$,$,'W-1',.STANDARD.);
#22=IFCCOLUMN('colguid',$,'C1 600x600',$,'Column',$,$,'C-1',.COLUMN.);
#23=IFCSLAB('matguid',$,'Mat 1200mm',$,'Mat',$,$,'F-1',.BASESLAB.);
#24=IFCBEAM('beamguid',$,'B1 transfer',$,'Beam',$,$,'B-1',.BEAM.);
#30=IFCREINFORCINGBAR('rebarguid',$,'15M',$,$,$,$,'R-1',$,0.196,15.,$,.MAIN.,$);
#40=IFCQUANTITYVOLUME('NetVolume',$,$,12.0);
#41=IFCELEMENTQUANTITY('q-slab',$,'BaseQuantities',$,$,(#40));
#42=IFCRELDEFINESBYPROPERTIES('rd-slab',$,$,$,(#20),#41);
#43=IFCQUANTITYVOLUME('GrossVolume',$,$,6.0);
#44=IFCQUANTITYVOLUME('NetVolume',$,$,5.0);
#45=IFCELEMENTQUANTITY('q-wall',$,'BaseQuantities',$,$,(#43,#44));
#46=IFCRELDEFINESBYPROPERTIES('rd-wall',$,$,$,(#21),#45);
#47=IFCQUANTITYVOLUME('NetVolume',$,$,2.0);
#48=IFCELEMENTQUANTITY('q-col',$,'BaseQuantities',$,$,(#47));
#49=IFCRELDEFINESBYPROPERTIES('rd-col',$,$,$,(#22),#48);
#50=IFCQUANTITYVOLUME('NetVolume',$,$,40.0);
#51=IFCELEMENTQUANTITY('q-mat',$,'BaseQuantities',$,$,(#50));
#52=IFCRELDEFINESBYPROPERTIES('rd-mat',$,$,$,(#23),#51);
#53=IFCQUANTITYVOLUME('NetVolume',$,$,0.01);
#54=IFCELEMENTQUANTITY('q-bar',$,'BaseQuantities',$,$,(#53));
#55=IFCRELDEFINESBYPROPERTIES('rd-bar',$,$,$,(#30),#54);
#60=IFCRELCONTAINEDINSPATIALSTRUCTURE('cont',$,$,$,(#20,#21,#22,#23,#24),#10);
ENDSEC;
END-ISO-10303-21;";

    private static IfcTakeoffResult Read() => IfcQuantityTakeoff.Read(Ifc);

    [Fact]
    public void Reads_exact_model_volumes_grouped_by_level_and_element()
    {
        var r = Read();
        var byElem = r.Inputs.ToDictionary(i => i.Element, i => i);

        Assert.Equal(12.0, byElem[TakeoffElementType.Slab].ConcreteVolume, 3);
        Assert.Equal(5.0, byElem[TakeoffElementType.Wall].ConcreteVolume, 3);   // NET, not the 6.0 gross
        Assert.Equal(2.0, byElem[TakeoffElementType.Column].ConcreteVolume, 3);
        Assert.Equal(40.0, byElem[TakeoffElementType.Foundation].ConcreteVolume, 3);
        Assert.All(r.Inputs, i => Assert.Equal("Level 2", i.Level));
    }

    [Fact]
    public void BaseSlab_is_a_Foundation_not_a_suspended_slab()
    {
        var r = Read();
        Assert.Contains(r.Inputs, i => i.Element == TakeoffElementType.Foundation && i.ConcreteVolume == 40.0);
        Assert.DoesNotContain(r.Inputs, i => i.Element == TakeoffElementType.Slab && i.ConcreteVolume == 40.0);
    }

    [Fact]
    public void Unquantified_element_is_a_residual_never_guessed()
    {
        var r = Read();
        Assert.DoesNotContain(r.Inputs, i => i.Element == TakeoffElementType.Beam);
        var res = Assert.Single(r.Residual);
        Assert.Equal("IFCBEAM", res.Type);
        Assert.Equal("Level 2", res.Level);
    }

    [Fact]
    public void Modelled_rebar_is_summed_as_exact_steel()
    {
        var r = Read();
        // 0.01 m³ × 7850 kg/m³ = 78.5 kg, one bar.
        Assert.Equal(1, r.ModelledRebarBars);
        Assert.Equal(78.5, r.ModelledRebarKg, 2);
    }

    [Fact]
    public void Prices_through_the_existing_service_to_a_total()
    {
        var r = Read();
        var computed = StructuralTakeoffService.Compute(r.Inputs, PlanProfile.BcModerate.ToImperialDensityTable());
        // Concrete is exact and unit-preserving: 12 + 5 + 2 + 40 = 59 m³.
        Assert.Equal(59.0, computed.TotalConcreteVolume, 3);
        Assert.True(computed.TotalRebarWeight > 0);   // density method prices reinforcing downstream
    }
}

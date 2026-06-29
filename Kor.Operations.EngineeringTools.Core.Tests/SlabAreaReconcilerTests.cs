#nullable enable

using System.Linq;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class SlabAreaReconcilerTests
{
    // A grid whose envelope is a known sqft at 1:100, so the net-factor math is checkable.
    // 1400×1000 pt → 1400*1000*0.0133959 = 18,754 sqft gross; × 0.92 = 17,254 net.
    private static GridFrame Grid(double xPt = 1400, double yPt = 1000, bool multi = false)
        => new(new[] { "1", "2" }, new[] { "A", "B" }, xPt, yPt, multi);

    private static double GridNet(GridFrame g) => g.EnvelopeSqFt(100) * SlabAreaReconciler.DefaultNetFactor;

    [Fact]
    public void Agreeing_grid_and_poche_are_confirmed_and_clean()
    {
        var g = Grid();
        double net = GridNet(g);
        var r = SlabAreaReconciler.Reconcile(g, 100, pocheSqFt: net * 0.95);   // poché close to grid-net
        Assert.Equal(AreaBasis.GridConfirmed, r.Basis);
        Assert.Equal(net, r.AreaSqFt, 0);
        Assert.DoesNotContain(r.Flags, f => f.Severity == PlanFlagSeverity.Review);
    }

    [Fact]
    public void A_leaky_poche_far_below_grid_keeps_the_grid_and_flags_review()
    {
        var g = Grid();
        // The south-tower case: poché collapsed to a tiny fragment.
        var r = SlabAreaReconciler.Reconcile(g, 100, pocheSqFt: 449);
        Assert.Equal(AreaBasis.GridPocheDisagree, r.Basis);
        Assert.Equal(GridNet(g), r.AreaSqFt, 0);   // grid stands
        Assert.Contains(r.Flags, f => f.Code == "AREA_POCHE_LOW" && f.Severity == PlanFlagSeverity.Review);
    }

    [Fact]
    public void A_poche_that_grabbed_a_neighbour_above_grid_is_flagged()
    {
        var g = Grid();
        var r = SlabAreaReconciler.Reconcile(g, 100, pocheSqFt: GridNet(g) * 1.8);
        Assert.Equal(AreaBasis.GridPocheDisagree, r.Basis);
        Assert.Contains(r.Flags, f => f.Code == "AREA_POCHE_HIGH" && f.Severity == PlanFlagSeverity.Review);
    }

    [Fact]
    public void Grid_only_when_no_poche_is_an_info_note()
    {
        var r = SlabAreaReconciler.Reconcile(Grid(), 100, pocheSqFt: null);
        Assert.Equal(AreaBasis.GridOnly, r.Basis);
        Assert.Contains(r.Flags, f => f.Code == "AREA_GRID_ONLY" && f.Severity == PlanFlagSeverity.Info);
    }

    [Fact]
    public void Poche_only_when_grid_is_unusable_is_used_but_flagged()
    {
        var r = SlabAreaReconciler.Reconcile(grid: null, 100, pocheSqFt: 12000);
        Assert.Equal(AreaBasis.PocheOnly, r.Basis);
        Assert.Equal(12000, r.AreaSqFt, 0);
        Assert.Contains(r.Flags, f => f.Code == "AREA_NO_GRID" && f.Severity == PlanFlagSeverity.Review);
    }

    [Fact]
    public void Neither_signal_is_unresolved_orange()
    {
        var r = SlabAreaReconciler.Reconcile(grid: null, 100, pocheSqFt: null);
        Assert.Equal(AreaBasis.Unresolved, r.Basis);
        Assert.Equal(0, r.AreaSqFt);
        Assert.Contains(r.Flags, f => f.Code == "AREA_UNRESOLVED");
    }

    [Fact]
    public void A_multiplan_sheet_adds_an_info_note_even_when_confirmed()
    {
        var g = Grid(multi: true);
        var r = SlabAreaReconciler.Reconcile(g, 100, pocheSqFt: GridNet(g));
        Assert.Equal(AreaBasis.GridConfirmed, r.Basis);
        Assert.Contains(r.Flags, f => f.Code == "AREA_MULTIPLAN" && f.Severity == PlanFlagSeverity.Info);
    }

    [Fact]
    public void An_unusable_grid_with_too_few_bubbles_falls_back_to_poche()
    {
        var thin = new GridFrame(new[] { "1" }, System.Array.Empty<string>(), 0, 0, false);   // not IsUsable
        var r = SlabAreaReconciler.Reconcile(thin, 100, pocheSqFt: 9000);
        Assert.Equal(AreaBasis.PocheOnly, r.Basis);
    }
}

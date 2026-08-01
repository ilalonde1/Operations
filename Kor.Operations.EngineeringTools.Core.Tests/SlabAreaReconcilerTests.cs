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

    // ── Cross-tower sibling adjudication (ResolveAgainstPeers) ──────────────────────────────────────────
    // A divergent consensus: grid says 16,979 (the 18-SOUTH full-podium-width mis-read), poché says 6,656.
    private static AreaConsensus Diverged(double grid = 16979, double poche = 6656)
        => new(grid, AreaBasis.GridPocheDisagree, grid, poche,
               new[] { new PlanFlag(PlanFlagSeverity.Review, "AREA_POCHE_LOW", "poché low") });

    [Fact]
    public void Sibling_adjudication_overrides_a_grid_outlier_with_the_peer_consistent_poche()
    {
        // 18-NORTH (the confirmed sibling) measured 5,319 sqft. The 18-SOUTH grid is 3.2× that while its
        // poché (6,656) matches it → the grid grabbed podium-width bubbles; use the poché.
        var r = SlabAreaReconciler.ResolveAgainstPeers(Diverged(), peerMedianSqFt: 5319);
        Assert.Equal(AreaBasis.PocheOnly, r.Basis);
        Assert.Equal(6656, r.AreaSqFt, 0);
        Assert.Contains(r.Flags, f => f.Code == "AREA_GRID_OUTLIER" && f.Severity == PlanFlagSeverity.Review);
    }

    [Fact]
    public void Sibling_adjudication_leaves_a_genuinely_leaky_plate_on_the_grid()
    {
        // Grid is in line with peers (not an outlier) and the poché collapsed — a real leaky plate, NOT a
        // bad envelope. The grid must stand; the adjudicator must not "rescue" a leaked poché.
        var leaky = new AreaConsensus(5500, AreaBasis.GridPocheDisagree, 5500, 449,
            new[] { new PlanFlag(PlanFlagSeverity.Review, "AREA_POCHE_LOW", "poché low") });
        var r = SlabAreaReconciler.ResolveAgainstPeers(leaky, peerMedianSqFt: 5319);
        Assert.Equal(AreaBasis.GridPocheDisagree, r.Basis);   // unchanged
        Assert.Equal(5500, r.AreaSqFt, 0);
    }

    [Fact]
    public void Sibling_adjudication_holds_when_the_poche_does_not_match_the_peers_either()
    {
        // Grid is an outlier, but the poché (2,000) is also far from the 5,319 peer median → no signal can
        // be vouched for; leave it as-is for the AI/human pass (stays orange).
        var r = SlabAreaReconciler.ResolveAgainstPeers(Diverged(grid: 16979, poche: 2000), peerMedianSqFt: 5319);
        Assert.Equal(AreaBasis.GridPocheDisagree, r.Basis);
        Assert.Equal(16979, r.AreaSqFt, 0);
    }

    [Fact]
    public void Sibling_adjudication_is_a_noop_on_confirmed_or_peerless_plates()
    {
        var confirmed = new AreaConsensus(5319, AreaBasis.GridConfirmed, 5319, 5400, System.Array.Empty<PlanFlag>());
        Assert.Same(confirmed, SlabAreaReconciler.ResolveAgainstPeers(confirmed, 5319));   // wrong basis → untouched
        Assert.Same(confirmed, SlabAreaReconciler.ResolveAgainstPeers(confirmed, 0));      // no peers → untouched
        var diverged = Diverged();
        Assert.Same(diverged, SlabAreaReconciler.ResolveAgainstPeers(diverged, 0));        // no peer median → untouched
    }
}

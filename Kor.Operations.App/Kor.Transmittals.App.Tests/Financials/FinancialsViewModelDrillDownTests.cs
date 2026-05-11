#nullable enable
using System.Reflection;
using AppFin = Kor.Operations.Financials;
using AppSvc = Kor.Operations.Services;
using Xunit;

namespace Kor.Operations.Tests.Financials;

/// <summary>
/// Locks the Batch 64 contract: when the user selects a row in the
/// Active Projects DataGrid (TwoWay-bound to SelectedProject) or the
/// Clients DataGrid (SelectedClient), the AI bar's BuildLocalContext
/// emits a focused "user is asking about this row" block that the
/// MCP system prompt knows to read first. Without these tests the
/// drill-down regresses silently into either-empty-string-or-full-snapshot.
/// </summary>
public sealed class FinancialsViewModelDrillDownTests
{
    [Fact]
    public void BuildLocalContext_ReturnsEmpty_WhenNothingSelected()
    {
        var vm = NewVm();

        var ctx = ((AppSvc.IAiContextProvider)vm).BuildLocalContext();

        // Empty is correct — the registered builder still emits the
        // full BuildContext snapshot, so AI isn't blind, just unfocused.
        Assert.Equal("", ctx);
    }

    [Fact]
    public void BuildLocalContext_EmitsProjectBlock_WhenProjectSelected()
    {
        var vm = NewVm();
        vm.SelectedProject = new AppFin.FinancialsProjectRow
        {
            Wbs1 = "17202",
            Name = "Test Tower",
            Pm = "JM",
            DraftingManager = "Jim",
            Phase = "DD",
            Fee = 500_000,
            HourlyRevenue = 50_000,
            FeeBilled = 200_000,
            UnpostedFeeBilled = 0,
            EngHrs = 1200, EngBudget = 2000,
            DraftHrs = 800, DraftBudget = 1500,
            EngPercent = 0.6, DraftPercent = 0.53,
            EngLaborCost = 60_000,
            DraftLaborCost = 40_000,
            SubconsultantCost = 25_000,
        };

        var ctx = ((AppSvc.IAiContextProvider)vm).BuildLocalContext();

        Assert.Contains("Selected project (user is asking about this row)", ctx, System.StringComparison.Ordinal);
        Assert.Contains("17202", ctx, System.StringComparison.Ordinal);
        Assert.Contains("Test Tower", ctx, System.StringComparison.Ordinal);
        Assert.Contains("JM", ctx, System.StringComparison.Ordinal);          // PM
        Assert.Contains("Jim", ctx, System.StringComparison.Ordinal);          // Drafting Manager
        Assert.Contains("DD", ctx, System.StringComparison.Ordinal);           // Phase
        Assert.Contains("$550,000", ctx, System.StringComparison.Ordinal);     // TotalFee = Fee + HourlyRevenue
        Assert.Contains("$200,000", ctx, System.StringComparison.Ordinal);     // FeeBilled
        Assert.Contains("Multiplier:", ctx, System.StringComparison.Ordinal);
        Assert.Contains("Margin:", ctx, System.StringComparison.Ordinal);
        Assert.Contains("Profit:", ctx, System.StringComparison.Ordinal);
    }

    [Fact]
    public void BuildLocalContext_EmitsClientBlock_WhenClientSelected()
    {
        var vm = NewVm();
        vm.SelectedClient = new AppFin.ClientRollupRow
        {
            ClientId = "CL00403",
            ClientName = "Acme Holdings",
            ProjectCount = 12,
            ActiveProjectCount = 3,
            LifetimeFee = 4_200_000,
            LifetimeBilled = 3_800_000,
            LifetimeUnpostedBilled = 100_000,
            LifetimeDirectLaborCost = 1_500_000,
            LifetimeSubconsultantCost = 320_000,
            Outstanding = 250_000,
            Outstanding90Plus = 80_000,
            FirstProjectDate = System.DateTime.Today.AddYears(-7).AddMonths(-4),
        };

        var ctx = ((AppSvc.IAiContextProvider)vm).BuildLocalContext();

        Assert.Contains("Selected client (user is asking about this client rollup)", ctx, System.StringComparison.Ordinal);
        Assert.Contains("Acme Holdings", ctx, System.StringComparison.Ordinal);
        Assert.Contains("12 projects total, 3 active", ctx, System.StringComparison.Ordinal);
        Assert.Contains("$4,200,000", ctx, System.StringComparison.Ordinal);
        Assert.Contains("Lifetime Multiplier:", ctx, System.StringComparison.Ordinal);
        Assert.Contains("Tenure:", ctx, System.StringComparison.Ordinal);
        Assert.Contains("years", ctx, System.StringComparison.Ordinal);
        // Per the partner-naming + no-codes rules, the rollup output keeps the
        // Name visible but the raw ClientID is not surfaced in BuildLocalContext
        // (the engineer already sees the name in the grid; AI just needs the
        // human label for follow-up questions).
        Assert.DoesNotContain("CL00403", ctx, System.StringComparison.Ordinal);
    }

    [Fact]
    public void BuildLocalContext_EmitsBothBlocks_WhenBothSelected()
    {
        var vm = NewVm();
        vm.SelectedProject = new AppFin.FinancialsProjectRow { Wbs1 = "17202", Name = "P1" };
        vm.SelectedClient  = new AppFin.ClientRollupRow { ClientName = "Acme" };

        var ctx = ((AppSvc.IAiContextProvider)vm).BuildLocalContext();

        // Project section first, then client (matches the order in BuildLocalContext).
        var iProject = ctx.IndexOf("Selected project", System.StringComparison.Ordinal);
        var iClient  = ctx.IndexOf("Selected client",  System.StringComparison.Ordinal);
        Assert.True(iProject >= 0 && iClient > iProject);
        Assert.Contains("17202", ctx, System.StringComparison.Ordinal);
        Assert.Contains("Acme",  ctx, System.StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedProjectHasValue_ReflectsSelection()
    {
        var vm = NewVm();
        Assert.False(vm.SelectedProjectHasValue);

        vm.SelectedProject = new AppFin.FinancialsProjectRow { Wbs1 = "X" };
        Assert.True(vm.SelectedProjectHasValue);

        vm.SelectedProject = null;
        Assert.False(vm.SelectedProjectHasValue);
    }

    /// <summary>
    /// FinancialsViewModel's full ctor pulls services from DI. The
    /// drill-down emission doesn't touch any of those — it reads only
    /// the two SelectedXxx properties — so an uninitialized instance
    /// (RuntimeHelpers.GetUninitializedObject, same pattern as
    /// PostingLagTierTests) is enough scaffolding for this test class.
    /// </summary>
    private static AppFin.FinancialsViewModel NewVm()
        => (AppFin.FinancialsViewModel)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(AppFin.FinancialsViewModel));
}

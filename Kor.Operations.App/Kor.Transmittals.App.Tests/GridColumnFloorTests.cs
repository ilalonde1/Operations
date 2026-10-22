using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Kor.Operations;
using Xunit;

namespace Kor.Operations.App.Tests;

/// <summary>
/// Fixed-width DataGrid columns were cut to a sliver when a grid ran short of room ("$5,70(", a missing Prov. column,
/// "2026-10-2"; 2026-10-22). Covers: a pixel-width column gets its width as its floor; a star column and a column with
/// its own MinWidth are untouched; and the registered handler applies it to a real grid on first layout, so its fixed
/// column keeps its width in a grid narrower than its columns. Does NOT cover: a column added to a grid after its first
/// layout, or text wider than its column's stated width (that is still cut; the XAML width is the decision).
/// </summary>
public sealed class GridColumnFloorTests
{
    [Fact]
    public void AFixedColumnKeepsItsWidthAndOthersAreLeftAlone()
    {
        RunSta(() =>
        {
            var grid = new DataGrid();
            var fixedCol = new DataGridTextColumn { Width = 110 };
            var star = new DataGridTextColumn { Width = new DataGridLength(1, DataGridLengthUnitType.Star) };
            var own = new DataGridTextColumn { Width = 200, MinWidth = 150 };
            grid.Columns.Add(fixedCol);
            grid.Columns.Add(star);
            grid.Columns.Add(own);

            GridColumnFloor.Apply(grid);

            Assert.Equal(110, fixedCol.MinWidth);
            Assert.Equal(DataGridColumn.MinWidthProperty.DefaultMetadata.DefaultValue, star.MinWidth);
            Assert.Equal(150, own.MinWidth);
        });
    }

    [Fact]
    public void ARegisteredGridShorterThanItsColumnsDoesNotSqueezeAFixedColumn()
    {
        RunSta(() =>
        {
            GridColumnFloor.Register();
            var cost = new DataGridTextColumn { Header = "Cost", Width = 110 };
            var grid = new DataGrid { Width = 300, Height = 200 };
            grid.Columns.Add(new DataGridTextColumn { Header = "Project", Width = new DataGridLength(3, DataGridLengthUnitType.Star), MinWidth = 220 });
            grid.Columns.Add(cost);
            grid.Columns.Add(new DataGridTextColumn { Header = "Stage", Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 100 });
            var w = new Window { Content = grid, Width = 400, Height = 300, ShowInTaskbar = false, ShowActivated = false };
            try
            {
                w.Show();
                w.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                Assert.True(cost.ActualWidth >= 110, $"EXPECTED Cost >= 110 px; ACTUAL {cost.ActualWidth}");
            }
            finally
            {
                w.Close();
            }
        });
    }

    private static void RunSta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }
}

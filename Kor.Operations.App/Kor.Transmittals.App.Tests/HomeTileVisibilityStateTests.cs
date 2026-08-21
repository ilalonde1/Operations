#nullable enable
using System;
using System.IO;
using System.Windows;
using Kor.Operations;
using Xunit;

namespace Kor.Operations.App.Tests;

public sealed class HomeTileVisibilityStateTests
{
    [Fact]
    public void Security_lookup_failure_collapses_financials_and_compensation()
    {
        var state = HomeTileVisibilityState.ForSecurityLookupFailure();

        Assert.Equal(Visibility.Collapsed, state.Financials);
        Assert.Equal(Visibility.Collapsed, state.Compensation);
    }

    [Fact]
    public void Security_lookup_failure_keeps_non_sensitive_fallback_tiles_visible()
    {
        var state = HomeTileVisibilityState.ForSecurityLookupFailure();

        Assert.Equal(Visibility.Visible, state.PmTools);
        Assert.Equal(Visibility.Visible, state.StandardDetails);
        Assert.Equal(Visibility.Visible, state.GeneralTools);
        Assert.Equal(Visibility.Visible, state.FeeProposalBuilder);
        Assert.Equal(Visibility.Visible, state.EngineeringTools);
    }

    [Fact]
    public void HomeWindow_failure_path_uses_restricted_fallback_decision()
    {
        var source = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "Kor.Operations.App",
            "HomeWindow.xaml.cs"));

        Assert.Contains("HomeTileVisibilityState.ForSecurityLookupFailure()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FinancialsTileHost.Visibility = Visibility.Visible;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CompensationTileHost.Visibility = Visibility.Visible;", source, StringComparison.Ordinal);
    }

    private static string GetRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Kor.Operations.App")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
    }
}

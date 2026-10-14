#nullable enable
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

/// <summary>
/// CRM clarity pass, 2026-10-13. Every input box on the Pursuits page says what it is
/// for without a hover; the page's list is called what it is; the manager board says
/// what cold means; and the deep-link aliases for the old page names survive.
///
/// XAML text only. Does not render, so an adorner that draws off-box, or a placeholder
/// that never clears on focus, would escape.
/// </summary>
public sealed class BdCrmClarityRegressionTests
{
    [Fact]
    public void Pursuits_EveryInlineInputHasAPlaceholder()
    {
        var xaml = Source("Kor.Operations.App/Crm/CrmView.xaml");
        var boxes = Regex.Matches(xaml, @"<TextBox\b[^>]*?(/>|>)", RegexOptions.Singleline);
        Assert.True(boxes.Count >= 7, $"EXPECTED the seven inline text boxes; ACTUAL {boxes.Count}");
        foreach (Match box in boxes)
        {
            Assert.True(box.Value.Contains("controls:Placeholder.Text=", StringComparison.Ordinal),
                $"EXPECTED a placeholder on every TextBox; ACTUAL none on:\n{box.Value}");
        }

        Assert.Contains("Text=\"Pursuits\"", xaml);
        Assert.DoesNotContain("Text=\"Engagements\"", xaml);
    }

    [Fact]
    public void PursuitHealth_SaysWhatColdMeans()
    {
        var xaml = Source("Kor.Operations.App/BusinessDevelopment/Workspace/OverwatchView.xaml");
        Assert.Contains("coldest first", xaml);
        Assert.Contains("21 days", xaml);
        Assert.DoesNotContain("Pursuit Monitor", xaml);
        var vm = Source("Kor.Operations.App/BusinessDevelopment/Workspace/OverwatchViewModel.cs");
        Assert.Contains("ColdDays = 21", vm);   // the label and the rule must agree
        Assert.Contains("WatchDays = 7", vm);
    }

    [Fact]
    public void PursuitPages_KeepTheirDeepLinkAliases()
    {
        // NavigateByKey matches nav labels; old spellings are permanent aliases
        // (BdWorkspaceWindow.xaml.cs). The board's on-screen title changed today;
        // its nav label and every alias did not.
        var nav = Source("Kor.Operations.App/BusinessDevelopment/Workspace/BdWorkspaceWindow.xaml");
        var code = Source("Kor.Operations.App/BusinessDevelopment/Workspace/BdWorkspaceWindow.xaml.cs");
        Assert.Contains("Content=\"My Pursuits\"", nav);
        Assert.Contains("Content=\"All Pursuits\"", nav);
        foreach (var alias in new[] { "\"My Pursuits\"", "\"Pursuits\"", "\"All Pursuits\"", "\"Pursuit Monitor\"", "\"Overwatch\"" })
            Assert.True(code.Contains("case " + alias, StringComparison.Ordinal), $"EXPECTED deep-link alias {alias}; ACTUAL missing");
    }

    [Fact]
    public void Placeholder_IsAnAttachedPropertyOnTextBox()
    {
        var source = Source("Kor.Operations.App/Controls/Placeholder.cs");
        Assert.Contains("RegisterAttached(", source);
        Assert.Contains("AdornerLayer.GetAdornerLayer(box)", source);
        Assert.Contains("!box.IsKeyboardFocusWithin", source);   // hidden while typing, not only when non-empty
    }

    private static string Source(string path)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, path));
    }
}

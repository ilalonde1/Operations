#nullable enable
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Kor.Operations.App.BusinessDevelopment.Workspace;
using Kor.Operations.App.Crm;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Awards;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.Opportunities;
using Moq;
using Xunit;
using Match = System.Text.RegularExpressions.Match;   // Moq also exports a Match type

namespace Kor.Operations.App.Tests.BusinessDevelopment;

/// <summary>
/// CRM clarity pass, 2026-10-13. Every input box on the Pursuits page says what it is
/// for without a hover; the page's list is called what it is; the manager board says
/// what cold means; and the deep-link aliases for the old page names survive.
///
/// Placeholder gates inspect XAML/source text only: they cannot see an adorner drawn
/// off-box; a ComboBox whose bound selection is null at runtime would render blank and escape.
/// Wording checks cover XAML and two view models' AnalyticsHeadline/StatusMessage/Headline
/// only. Item labels such as ActivityTypeOptions and strings built elsewhere are not
/// checked; identifiers and comments containing "engagement" are not UI and not checked.
/// XAML markup-extension values are skipped, so wording supplied by a binding escapes.
/// </summary>
public sealed class BdCrmClarityRegressionTests
{
    [Fact]
    public void Attachments_UseDatabaseStorageAndOneLauncher_WithoutShareSetupWording()
    {
        foreach (var area in new[] { "Crm", "BusinessDevelopment/Director" })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot(), "Kor.Operations.App", area))
                     .Where(f => Path.GetExtension(f) is ".cs" or ".xaml"))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("aren't set up", text, StringComparison.Ordinal);
            Assert.DoesNotContain("PursuitFilesRoot", text, StringComparison.Ordinal);
        }
        var registration = Source("Kor.Operations.App/CompositionModules/OpportunitiesModule.cs");
        Assert.Contains("IPursuitFileStorage, App.Crm.DbPursuitFileStorage", registration, StringComparison.Ordinal);
        var crm = Source("Kor.Operations.App/Crm/CrmView.xaml.cs");
        Assert.Contains("GetRequiredService<FeeProposalLauncher>().Launch(row.OpportunityId, row.Id", crm, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<IPursuitFileOpener>().OpenAsync(row.Model)", crm, StringComparison.Ordinal);
        Assert.DoesNotContain("LinkProposalToPursuitAsync", crm, StringComparison.Ordinal);
        var vm = Source("Kor.Operations.App/Crm/CrmViewModel.cs");
        Assert.Contains("var saved = stored.File", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("_fileStore.AddAsync", vm, StringComparison.Ordinal);
        var builder = Source("Kor.Operations.App/FeeProposal/FeeProposalBuilderWindow.xaml.cs");
        Assert.Equal(2, Regex.Matches(builder, "await _vm.NotifyProposalRenderedAsync").Count);
        Assert.Contains("proposal.Id, dlg.FileName, \"application/pdf\"", builder, StringComparison.Ordinal);
        Assert.Contains("application/vnd.openxmlformats-officedocument.wordprocessingml.document", builder, StringComparison.Ordinal);
        var launcher = Source("Kor.Operations.App/FeeProposal/FeeProposalLauncher.cs");
        Assert.Contains("_builder.ProposalSaved -= OnSaved", launcher, StringComparison.Ordinal);
        Assert.Contains("_builder.ProposalRendered -= OnRenderedAsync", launcher, StringComparison.Ordinal);
        Assert.DoesNotContain("RemoveAsync", launcher, StringComparison.Ordinal);
    }

    [Fact]
    public void Pursuits_EveryInlineInputHasAPlaceholder()
    {
        var xaml = Source("Kor.Operations.App/Crm/CrmView.xaml");
        var boxes = Regex.Matches(xaml, @"<(TextBox|ComboBox)\b[^>]*?(/>|>)", RegexOptions.Singleline);
        var textBoxes = boxes.Cast<Match>().Count(box => box.Groups[1].Value == "TextBox");
        Assert.True(textBoxes >= 7, $"EXPECTED at least seven inline text boxes; ACTUAL {textBoxes}");
        Assert.Contains(boxes.Cast<Match>(), box => box.Groups[1].Value == "ComboBox");
        foreach (Match box in boxes)
        {
            if (box.Groups[1].Value == "ComboBox")
            {
                Assert.True(Regex.IsMatch(box.Value, @"\b(SelectedIndex|SelectedItem|SelectedValue|controls:Placeholder.Text)\s*="),
                    $"EXPECTED selection or a placeholder on every ComboBox; ACTUAL none on:\n{box.Value}");
                continue;
            }
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
    public void Placeholder_IsAnAttachedPropertyOnTextBoxAndComboBox()
    {
        var source = Source("Kor.Operations.App/Controls/Placeholder.cs");
        Assert.Contains("RegisterAttached(", source);
        Assert.Contains("TextBox", source);
        Assert.Contains("ComboBox", source);
        Assert.Contains("SelectedIndex < 0", source);
        Assert.Contains("!combo.IsEditable || string.IsNullOrEmpty(combo.Text)", source);
        Assert.Contains("SelectionChanged += Refresh", source);
        Assert.Contains("AddHandler(TextBoxBase.TextChangedEvent", source);
        Assert.Contains("PlaceholderAdorner(Control", source);
        Assert.Contains("AdornerLayer.GetAdornerLayer(box)", source);
        Assert.Contains("!box.IsKeyboardFocusWithin", source);   // hidden while typing, not only when non-empty
    }

    [Fact]
    public async Task PursuitPages_SayPursuit_AndLiveMeansOneThing()
    {
        foreach (var file in new[] { "Crm/CrmView.xaml", "Crm/CrmOutcomeDialog.xaml", "Opportunities/OpportunitiesView.xaml" })
        {
            var xaml = Regex.Replace(Source("Kor.Operations.App/" + file), @"<!--.*?-->", "", RegexOptions.Singleline);
            var attributes = Regex.Matches(xaml,
                @"\b(?:Content|ToolTip|Text|Header)\s*=\s*""([^""]*)""", RegexOptions.Singleline);
            Assert.NotEmpty(attributes);
            foreach (Match attribute in attributes)
            {
                var value = attribute.Groups[1].Value;
                // A binding path such as SelectedEngagementStageDisplay is an identifier,
                // not visible wording. Escaped literal braces still contain screen text.
                if (value.StartsWith("{", StringComparison.Ordinal) && !value.StartsWith("{}", StringComparison.Ordinal)) continue;
                Assert.False(value.Contains("engagement", StringComparison.OrdinalIgnoreCase),
                    $"EXPECTED pursuit wording in {file}; ACTUAL {attribute.Value}");
            }
        }

        var rows = Enumerable.Range(1, 4).Select(i => new CrmEngagement
        {
            Id = i, Stage = i % 2 == 0 ? CrmEngagementStage.Submitted : CrmEngagementStage.Drafting,
            OwnerStaffId = "owner",
        }).ToList();
        rows.Add(new CrmEngagement { Id = 5, Stage = CrmEngagementStage.Won, ProposedFee = 41000m });
        var engagements = new Mock<ICrmEngagementStore>();
        engagements.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(rows);
        var opportunities = new Mock<IOpportunityStore>();
        opportunities.Setup(s => s.ListAsync(It.IsAny<CancellationToken>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync(Array.Empty<Opportunity>());
        var vm = new CrmViewModel(engagements.Object, Mock.Of<ICrmActivityStore>(), Mock.Of<ICrmContactStore>(),
            opportunities.Object, Mock.Of<IDeltekClientContextService>(), Mock.Of<ICanonicalOrgStore>(),
            Mock.Of<IPursuitContextStore>(), Mock.Of<IOpportunityInterestedFirmStore>(),
            Mock.Of<IPursuitFileStore>(), Mock.Of<IPursuitFileStorage>());
        await vm.LoadAsync(CancellationToken.None);
        Assert.Equal(new[]
        {
            "5 pursuits", "won 1 / lost 0 recorded here", "win rate " + 1d.ToString("P0", CultureInfo.CurrentCulture),
            "avg won fee " + 41000m.ToString("C0", CultureInfo.CurrentCulture),
        }, vm.AnalyticsHeadline.Split("  •  "));
        Assert.Equal("Loaded 5 pursuits.", vm.StatusMessage);

        rows.Add(new CrmEngagement { Id = 6, Stage = CrmEngagementStage.Won, ProposedFee = 90000m, ExternalSource = "Deltek.CustomProposal" });
        await vm.LoadAsync(CancellationToken.None);
        Assert.Contains("1 earlier Deltek wins", vm.AnalyticsHeadline);
        Assert.Contains("kept out of the rate", vm.AnalyticsHeadline);
        Assert.Contains("won 1 / lost 0 recorded here", vm.AnalyticsHeadline);
        Assert.Contains("avg won fee " + 41000m.ToString("C0", CultureInfo.CurrentCulture), vm.AnalyticsHeadline);
        Assert.DoesNotContain("engagement", vm.AnalyticsHeadline, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("live", vm.AnalyticsHeadline, StringComparison.OrdinalIgnoreCase);

        rows.RemoveAll(e => e.Stage == CrmEngagementStage.Won);
        await vm.LoadAsync(CancellationToken.None);
        Assert.Contains("— (none decided yet)", vm.AnalyticsHeadline);
        rows.RemoveRange(1, 3);
        await vm.LoadAsync(CancellationToken.None);
        Assert.StartsWith("1 pursuit  •", vm.AnalyticsHeadline);
        Assert.Equal("Loaded 1 pursuit.", vm.StatusMessage);
        rows.Clear();
        await vm.LoadAsync(CancellationToken.None);
        Assert.Equal("No pursuits yet — promote an opportunity from the Opportunities window to start one.", vm.StatusMessage);

        var now = DateTimeOffset.UtcNow;
        var boardRows = Enumerable.Range(1, 4).Select(i => new PursuitOverwatchRow(i, null, "owner", 1,
            "Pursuit", "Buyer", null, null, now, 0, null, now)).ToList();
        var store = new Mock<IPursuitOverwatchStore>();
        store.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(boardRows);
        var board = new OverwatchViewModel(store.Object);
        await board.LoadAsync(CancellationToken.None);
        Assert.StartsWith("4 owned live pursuits", board.Headline);
        Assert.Equal("Loaded 4 owned live pursuits.", board.StatusMessage);
        boardRows.RemoveRange(1, 3);
        await board.LoadAsync(CancellationToken.None);
        Assert.StartsWith("1 owned live pursuit  •", board.Headline);
        boardRows.Clear();
        await board.LoadAsync(CancellationToken.None);
        Assert.Equal("No owned live pursuits.", board.StatusMessage);
        Assert.Contains("Every owned live pursuit, coldest first", Source("Kor.Operations.App/BusinessDevelopment/Workspace/OverwatchView.xaml"));
        Assert.Contains("every owned live pursuit", Source("Kor.Operations.App/BusinessDevelopment/Workspace/BdWorkspaceWindow.xaml"));
    }

    private static string Source(string path) => File.ReadAllText(Path.Combine(RepoRoot(), path));

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}

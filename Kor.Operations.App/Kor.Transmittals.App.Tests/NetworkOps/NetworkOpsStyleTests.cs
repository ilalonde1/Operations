#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Kor.Operations.App.Tests.NetworkOps;

/// <summary>
/// One look across every NetworkOps window (NetworkOps/NetworkOpsStyles.xaml). Before the 2026-10-01 pass, 0 of the 5
/// windows used the app's shared styles: every button looked the same ("Remove agent" exactly like "Connect") and every
/// empty table was a grey slab -- each window had been styled by hand, one attribute at a time.
///
/// WHAT IT COVERS: every NetworkOps window merges the style dictionary; no button styles itself inline (background,
/// foreground, border, weight, padding come from a style); every table uses Ops.Grid; a window has at most two filled
/// (primary) buttons; the dictionary defines every Ops.* key a window asks for.
/// WHAT IT DOES NOT: how anything looks -- the render test draws each window to a PNG, and only looking at it shows
/// whether it reads well. A SAME-CLASS FAULT IT WOULD NOT CATCH: a TextBlock or Border styled by hand with its own
/// colours and sizes; the check is on buttons and tables, where the drift was.
/// </summary>
public sealed class NetworkOpsStyleTests
{
    private static string Dir => Path.Combine(XamlStaticResourceOrderTests.GetRepoRoot(), "Kor.Operations.App", "NetworkOps");

    private static string[] Windows() => Directory.GetFiles(Dir, "*Window.xaml");

    // The NetworkOps windows the style scan covers, by file name. Adding or removing a window is a deliberate edit: update this
    // list and know the scan now (un)covers it. The count lives HERE, in one place, derived from the list -- not split between a
    // hard-coded number in the assertion and a word in the method name, which is how it drifted (7 -> 8, left asserting 8 of 6).
    private static readonly string[] ExpectedWindows =
    {
        "KorRemoteViewerWindow.xaml",
        "NetworkOpsCommandCenterWindow.xaml",
        "NetworkOpsDeviceWindow.xaml",
        "NetworkOpsFixWindow.xaml",
        "NetworkOpsMachinePickerWindow.xaml",
        "PromptLibraryWindow.xaml",
    };

    [Fact]
    public void Every_NetworkOps_window_is_expected_and_merges_the_styles()
    {
        var windows = Windows().Select(w => Path.GetFileName(w)!).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        // A window added or removed shows here as exactly which file appeared or disappeared -- not an opaque "expected N, got M".
        Assert.Equal(ExpectedWindows.OrderBy(n => n, StringComparer.Ordinal).ToArray(), windows);
        foreach (var w in Windows())
            Assert.True(File.ReadAllText(w).Contains("component/NetworkOps/NetworkOpsStyles.xaml"), $"{Path.GetFileName(w)} does not merge NetworkOpsStyles.xaml");
    }

    [Fact]
    public void No_button_styles_itself()
    {
        foreach (var w in Windows())
        {
            var xaml = File.ReadAllText(w);
            foreach (Match b in Regex.Matches(xaml, @"<Button[\s/>][^>]*>", RegexOptions.Singleline))
                Assert.False(Regex.IsMatch(b.Value, @"\s(Background|Foreground|BorderBrush|FontWeight|Padding)="),
                    $"{Path.GetFileName(w)}: a button sets its own look -- use a style: {b.Value}");
        }
    }

    [Fact]
    public void Every_table_is_an_Ops_grid()
    {
        foreach (var w in Windows())
        {
            var xaml = File.ReadAllText(w);
            foreach (Match g in Regex.Matches(xaml, @"<DataGrid\s[^>]*>", RegexOptions.Singleline))
                Assert.True(g.Value.Contains("Style=\"{StaticResource Ops.Grid}\""), $"{Path.GetFileName(w)}: a table without Ops.Grid: {g.Value}");
        }
    }

    [Fact]
    public void A_window_has_at_most_two_filled_buttons()
    {
        foreach (var w in Windows())
        {
            var primaries = Regex.Matches(File.ReadAllText(w), @"Style=""\{StaticResource Ops\.Primary\}""").Count;
            Assert.True(primaries <= 2, $"{Path.GetFileName(w)} has {primaries} primary buttons: one per strip, so the eye finds the main action");
        }
    }

    [Fact]
    public void Every_Ops_style_a_window_asks_for_exists()
    {
        var dictionary = File.ReadAllText(Path.Combine(Dir, "NetworkOpsStyles.xaml"));
        var defined = Regex.Matches(dictionary, @"x:Key=""(Ops\.[A-Za-z]+)""").Select(m => m.Groups[1].Value).ToHashSet();
        foreach (var w in Windows())
            foreach (Match used in Regex.Matches(File.ReadAllText(w), @"StaticResource (Ops\.[A-Za-z]+)\}"))
                Assert.True(defined.Contains(used.Groups[1].Value), $"{Path.GetFileName(w)} uses {used.Groups[1].Value}, which NetworkOpsStyles.xaml does not define");
    }
}

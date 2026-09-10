#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.StandardDetails;

/// <summary>
/// A button whose style hard-codes padding inside its own ControlTemplate cannot be given a fixed
/// Height smaller than that padding plus a line of text: the content is clipped, and setting
/// Padding on the button does nothing because the template never binds to it.
///
/// This is the second instance of one class — a control pinned to a fixed size that the theme's own
/// padding then eats. The first was TextBox (theme Padding="8,5" at Height="32", "I type but I
/// can't see what I typed"); the second was OpenReviewSetButton at Height="32" against a template
/// padding of 9 top and bottom, which shipped clipped on 2026-09-09. So this is the check rather
/// than another one-off nudge.
///
/// COVERS: every .xaml under Kor.Operations.App. Finds each x:Key'd Button style whose template
/// Border hard-codes a vertical padding, computes the height that padding plus borders plus one
/// ~18px text line needs, and fails any Button using that style that pins a smaller Height.
/// DOES NOT COVER: implicit (un-keyed) styles; padding expressed as a four-value Thickness;
/// templates that bind Padding properly (those are fine by construction); Width, which clips
/// horizontally by the same mechanism; and anything about how it actually looks — only rendering
/// and looking at it can tell you that.
/// A same-class fault it would NOT catch: a button with no Height at all placed inside a parent
/// with a fixed Height too small for it, which clips identically from the outside.
/// </summary>
public sealed class ButtonHeightFitsItsTemplateTests
{
    /// <summary>One line of ~12.5px UI text, with its ascender and descender.</summary>
    private const double TextLineHeight = 18;

    private sealed record TemplatedStyle(string Key, double PadY, double BorderThickness, string File)
    {
        public double MinimumHeight => (2 * PadY) + (2 * BorderThickness) + TextLineHeight;
    }

    /// <summary>
    /// Anchored on the project FILE. Looking for a "StandardDetails" folder finds this test project's
    /// own StandardDetails folder first, so the scan ran over no XAML at all and the gate passed
    /// vacuously — caught only because the empty-scan assert below fired.
    /// </summary>
    private static string AppRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Kor.Operations.App.csproj")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "Could not locate Kor.Operations.App.csproj from the test output directory.");
        return dir!.FullName;
    }

    private static IReadOnlyList<string> AppXaml() =>
        Directory.GetFiles(AppRoot(), "*.xaml", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static Dictionary<string, TemplatedStyle> StylesThatHardCodeTheirPadding()
    {
        var found = new Dictionary<string, TemplatedStyle>(StringComparer.Ordinal);
        foreach (var path in AppXaml())
        {
            var text = File.ReadAllText(path);
            foreach (Match style in Regex.Matches(text, @"(?s)<Style\s+x:Key=""([^""]+)""\s+TargetType=""Button"">(.*?)</Style>"))
            {
                var body = style.Groups[2].Value;
                var padding = Regex.Match(body, @"<Border[^>]*\sPadding=""\s*[\d.]+\s*,\s*([\d.]+)");
                if (!padding.Success) continue;   // binds Padding properly, or sets none: fine

                var border = Regex.Match(body, @"<Border[^>]*\sBorderThickness=""\s*([\d.]+)");
                found[style.Groups[1].Value] = new TemplatedStyle(
                    style.Groups[1].Value,
                    double.Parse(padding.Groups[1].Value),
                    border.Success ? double.Parse(border.Groups[1].Value) : 0,
                    Path.GetFileName(path));
            }
        }

        return found;
    }

    [Fact]
    public void No_button_is_pinned_shorter_than_its_own_template_padding_allows()
    {
        var styles = StylesThatHardCodeTheirPadding();
        Assert.NotEmpty(styles); // the scan itself must not silently find nothing

        var clipped = new List<string>();
        var checkedButtons = 0;

        foreach (var path in AppXaml())
        {
            var text = File.ReadAllText(path);
            foreach (Match button in Regex.Matches(text, @"(?s)<Button\b[^>]*?(?:/>|>)"))
            {
                var tag = button.Value;
                var styleRef = Regex.Match(tag, @"Style=""\{StaticResource\s+([^}]+)\}""");
                if (!styleRef.Success) continue;
                if (!styles.TryGetValue(styleRef.Groups[1].Value.Trim(), out var style)) continue;

                checkedButtons++;
                var height = Regex.Match(tag, @"\sHeight=""([\d.]+)""");
                if (!height.Success) continue;   // unpinned: the template sizes it, which is the pattern that works

                var value = double.Parse(height.Groups[1].Value);
                if (value < style.MinimumHeight)
                {
                    var name = Regex.Match(tag, @"x:Name=""([^""]+)""").Groups[1].Value;
                    clipped.Add(
                        $"{Path.GetFileName(path)}: {(name.Length > 0 ? name : "(unnamed)")} uses {style.Key} " +
                        $"(template padding {style.PadY} top and bottom, border {style.BorderThickness}) so it needs " +
                        $"Height >= {style.MinimumHeight}, but pins Height=\"{value}\". Drop the Height and let the template size it.");
                }
            }
        }

        Assert.True(checkedButtons > 0, "Found no buttons using a hard-coded-padding style; the scan is not looking where it thinks it is.");
        Assert.True(clipped.Count == 0, "Clipped button(s):" + Environment.NewLine + string.Join(Environment.NewLine, clipped));
    }
}

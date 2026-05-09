#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Kor.Operations.Controls
{
    /// <summary>
    /// Minimal Markdown renderer for the AI Query Panel. Handles the subset
    /// Claude actually emits in plain Q&A — paragraphs, bullets, headings,
    /// bold / italic / inline-code spans, fenced code blocks. Renders into
    /// a StackPanel of TextBlocks + Borders so it composes inside the
    /// existing ScrollViewer without pulling in a Markdown library or
    /// switching to FlowDocument.
    /// </summary>
    internal static class MarkdownPresenter
    {
        // Tuned to match the panel's existing typography. Kept as fields so
        // the rendering code reads like a recipe rather than a wall of magic
        // numbers.
        private const double BaseFontSize = 11.5;
        private const double HeadingFontSize = 13.5;
        private const double CodeFontSize = 11.0;
        private const string MonoFontFamily = "Consolas, Cascadia Mono, Courier New";

        public static void Render(string markdown, StackPanel target, Brush textBrush, Brush codeBackground, Brush codeBorder)
        {
            target.Children.Clear();
            if (string.IsNullOrEmpty(markdown))
            {
                return;
            }

            var lines = markdown.Replace("\r\n", "\n").Split('\n');
            var i = 0;

            while (i < lines.Length)
            {
                var line = lines[i];

                // Fenced code block — accumulate until closing fence.
                if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    var buf = new StringBuilder();
                    i++;
                    while (i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
                    {
                        if (buf.Length > 0)
                        {
                            buf.Append('\n');
                        }
                        buf.Append(lines[i]);
                        i++;
                    }
                    if (i < lines.Length)
                    {
                        i++; // skip the closing fence
                    }
                    target.Children.Add(BuildCodeBlock(buf.ToString(), textBrush, codeBackground, codeBorder));
                    continue;
                }

                // Blank line — flush as paragraph spacer.
                if (string.IsNullOrWhiteSpace(line))
                {
                    i++;
                    continue;
                }

                // Heading — `#`, `##`, `###` all rendered as bold-larger.
                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    var stripped = line.TrimStart('#').TrimStart();
                    target.Children.Add(BuildHeading(stripped, textBrush));
                    i++;
                    continue;
                }

                // Bullet list — collect contiguous lines starting with `- `
                // or `* ` so the whole list renders as one block.
                if (IsBulletLine(line))
                {
                    var bullets = new List<string>();
                    while (i < lines.Length && IsBulletLine(lines[i]))
                    {
                        bullets.Add(lines[i].TrimStart()[2..]);
                        i++;
                    }
                    target.Children.Add(BuildBulletList(bullets, textBrush));
                    continue;
                }

                // Plain paragraph — collect contiguous non-special lines.
                var paragraph = new StringBuilder(line);
                i++;
                while (i < lines.Length
                    && !string.IsNullOrWhiteSpace(lines[i])
                    && !lines[i].StartsWith("#", StringComparison.Ordinal)
                    && !IsBulletLine(lines[i])
                    && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    paragraph.Append(' ');
                    paragraph.Append(lines[i].TrimStart());
                    i++;
                }
                target.Children.Add(BuildParagraph(paragraph.ToString(), textBrush));
            }
        }

        private static bool IsBulletLine(string line)
        {
            var trimmed = line.TrimStart();
            return (trimmed.StartsWith("- ", StringComparison.Ordinal)
                 || trimmed.StartsWith("* ", StringComparison.Ordinal))
                 && trimmed.Length > 2;
        }

        private static UIElement BuildParagraph(string text, Brush textBrush)
        {
            var tb = new TextBlock
            {
                FontSize = BaseFontSize,
                Foreground = textBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6),
            };
            AppendInlines(tb.Inlines, text);
            return tb;
        }

        private static UIElement BuildHeading(string text, Brush textBrush)
        {
            var tb = new TextBlock
            {
                FontSize = HeadingFontSize,
                FontWeight = FontWeights.SemiBold,
                Foreground = textBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 4),
            };
            AppendInlines(tb.Inlines, text);
            return tb;
        }

        private static UIElement BuildBulletList(IReadOnlyList<string> items, Brush textBrush)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            foreach (var item in items)
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var bullet = new TextBlock
                {
                    Text = "•",
                    FontSize = BaseFontSize,
                    Foreground = textBrush,
                    Margin = new Thickness(0, 0, 4, 0),
                };
                Grid.SetColumn(bullet, 0);

                var content = new TextBlock
                {
                    FontSize = BaseFontSize,
                    Foreground = textBrush,
                    TextWrapping = TextWrapping.Wrap,
                };
                AppendInlines(content.Inlines, item);
                Grid.SetColumn(content, 1);

                row.Children.Add(bullet);
                row.Children.Add(content);
                panel.Children.Add(row);
            }
            return panel;
        }

        private static UIElement BuildCodeBlock(string code, Brush textBrush, Brush background, Brush border)
        {
            var tb = new TextBlock
            {
                Text = code,
                FontFamily = new FontFamily(MonoFontFamily),
                FontSize = CodeFontSize,
                Foreground = textBrush,
                TextWrapping = TextWrapping.Wrap,
            };
            return new Border
            {
                Background = background,
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 2, 0, 8),
                Child = tb,
            };
        }

        /// <summary>
        /// Walks the line and emits Run/Bold/Italic/InlineCode segments based
        /// on the simple Markdown subset Claude uses. Greedy single-pass —
        /// no nested handling beyond what falls out of left-to-right.
        /// </summary>
        private static void AppendInlines(InlineCollection target, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var i = 0;
            var literal = new StringBuilder();

            void FlushLiteral()
            {
                if (literal.Length > 0)
                {
                    target.Add(new Run(literal.ToString()));
                    literal.Clear();
                }
            }

            while (i < text.Length)
            {
                // Inline code: `code`
                if (text[i] == '`')
                {
                    var close = text.IndexOf('`', i + 1);
                    if (close > i)
                    {
                        FlushLiteral();
                        var code = text.Substring(i + 1, close - i - 1);
                        target.Add(new Run(code)
                        {
                            FontFamily = new FontFamily(MonoFontFamily),
                            FontSize = CodeFontSize,
                            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xCB, 0xD5, 0xE1)),
                        });
                        i = close + 1;
                        continue;
                    }
                }

                // Bold: **text** or __text__
                if ((text[i] == '*' && i + 1 < text.Length && text[i + 1] == '*')
                    || (text[i] == '_' && i + 1 < text.Length && text[i + 1] == '_'))
                {
                    var marker = text.Substring(i, 2);
                    var close = text.IndexOf(marker, i + 2, StringComparison.Ordinal);
                    if (close > i + 1)
                    {
                        FlushLiteral();
                        var inner = text.Substring(i + 2, close - i - 2);
                        target.Add(new Bold(new Run(inner)));
                        i = close + 2;
                        continue;
                    }
                }

                // Italic: *text* or _text_ (single marker, not part of a bold)
                if ((text[i] == '*' || text[i] == '_')
                    && (i + 1 < text.Length && text[i + 1] != text[i]))
                {
                    var marker = text[i];
                    var close = text.IndexOf(marker, i + 1);
                    if (close > i)
                    {
                        FlushLiteral();
                        var inner = text.Substring(i + 1, close - i - 1);
                        target.Add(new Italic(new Run(inner)));
                        i = close + 1;
                        continue;
                    }
                }

                literal.Append(text[i]);
                i++;
            }

            FlushLiteral();
        }
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Kor.Operations.Rendering.Proposal;

namespace Kor.Operations.App.FeeProposal;

public sealed record OrgChartRender(string Svg, byte[] Png);

/// <summary>One deterministic layout feeds both vector and raster drawings.</summary>
public static class OrgChartRenderer
{
    private sealed record TextLine(string Text, double Size = 14, bool Bold = false, bool PersonName = false);
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    private const string Ink = "#21313F", Light = "#F0F3F6", Slate = "#3D5166", White = "#FFFFFF";
    private static FormattedText Format(string text, double size, bool bold, string color) => new(text,
        CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
        new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
        size, Brush(color), 1);
    private static Brush Brush(string color) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));

    public static OrgChartRender Render(OrgChartSpec spec)
    {
        spec.Validate();
        var width = Math.Max(1000, spec.Groups.Count * 360 + 40);
        var root = new XElement(Svg + "svg", new XAttribute("width", width));
        var visual = new DrawingVisual();
        double bottom;
        using (var dc = visual.RenderOpen())
        {
            void Line(double x1, double y1, double x2, double y2)
            {
                dc.DrawLine(new Pen(Brush(Slate), 1.5), new Point(x1, y1), new Point(x2, y2));
                root.Add(new XElement(Svg + "line", new XAttribute("x1", x1), new XAttribute("y1", y1), new XAttribute("x2", x2), new XAttribute("y2", y2), new XAttribute("stroke", Slate), new XAttribute("stroke-width", 1.5)));
            }
            double Box(double x, double y, double w, IEnumerable<TextLine> lines, string fill, string foreground)
            {
                var laidOut = new List<(TextLine Line, double Size, string[] Parts)>();
                foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l.Text)))
                {
                    if (line.PersonName)
                    {
                        var measured = Format(line.Text, line.Size, line.Bold, foreground).Width;
                        laidOut.Add((line, Math.Min(line.Size, line.Size * (w - 24) / Math.Max(1, measured)), new[] { line.Text }));
                        continue;
                    }
                    var parts = new List<string>();
                    foreach (var paragraph in line.Text.Split('\n'))
                    {
                        var current = "";
                        foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        {
                            var candidate = current.Length == 0 ? word : current + " " + word;
                            if (current.Length > 0 && Format(candidate, line.Size, line.Bold, foreground).Width > w - 24)
                            { parts.Add(current); current = word; }
                            else current = candidate;
                        }
                        if (current.Length > 0) parts.Add(current);
                    }
                    laidOut.Add((line, line.Size, parts.ToArray()));
                }
                var height = 24 + laidOut.Sum(l => l.Parts.Length * (l.Size + 6));
                dc.DrawRectangle(Brush(fill), new Pen(Brush(Slate), 1), new Rect(x, y, w, height));
                root.Add(new XElement(Svg + "rect", new XAttribute("x", x), new XAttribute("y", y), new XAttribute("width", w), new XAttribute("height", height), new XAttribute("fill", fill), new XAttribute("stroke", Slate)));
                var textY = y + 12;
                foreach (var line in laidOut)
                foreach (var part in line.Parts)
                {
                    var formatted = Format(part, line.Size, line.Line.Bold, foreground);
                    var textX = x + (w - formatted.Width) / 2;
                    dc.DrawText(formatted, new Point(textX, textY));
                    root.Add(new XElement(Svg + "text", new XAttribute("x", textX), new XAttribute("y", textY + formatted.Baseline),
                        new XAttribute("font-family", "Segoe UI, sans-serif"), new XAttribute("font-size", line.Size),
                        new XAttribute("font-weight", line.Line.Bold ? "bold" : "normal"), new XAttribute("fill", foreground),
                        new XAttribute("data-kind", line.Line.PersonName ? "person-name" : "text"), part));
                    textY += line.Size + 6;
                }
                return y + height;
            }
            IEnumerable<TextLine> Person(OrgChartPerson p) => new[]
            {
                new TextLine(p.Name, 16, true, true), new TextLine(p.Cred), new TextLine(p.Title),
                new TextLine((p.Role ?? "").ToUpperInvariant(), 13, true)
            };
            bottom = Box(20, 20, width - 40, new[] { new TextLine(spec.Title, 24, true), new TextLine(spec.Subtitle) }, White, Ink);
            bottom += 24;
            bottom = Box((width - 800) / 2d, bottom, 800,
                new[] { new TextLine(spec.Context.Name, 16, true) }.Concat(spec.Context.Lines.Select(t => new TextLine(t))), Light, Ink);
            foreach (var person in spec.Chain)
            {
                Line(width / 2d, bottom, width / 2d, bottom + 28);
                bottom = Box((width - 500) / 2d, bottom + 28, 500, Person(person), Slate, White);
            }
            if (spec.Groups.Count > 0)
            {
                var bus = bottom + 28;
                var left = (width - (spec.Groups.Count * 360 - 20)) / 2d;
                Line(width / 2d, bottom, width / 2d, bus);
                Line(left + 170, bus, left + (spec.Groups.Count - 1) * 360 + 170, bus);
                for (var i = 0; i < spec.Groups.Count; i++)
                {
                    var group = spec.Groups[i]; var x = left + i * 360;
                    Line(x + 170, bus, x + 170, bus + 24);
                    var groupBottom = Box(x, bus + 24, 340, new[] { new TextLine(group.Name, 16, true) }, Slate, White);
                    foreach (var person in group.People)
                    {
                        Line(x + 170, groupBottom, x + 170, groupBottom + 18);
                        groupBottom = Box(x, groupBottom + 18, 340, Person(person), White, Ink);
                    }
                    bottom = Math.Max(bottom, groupBottom);
                }
            }
            if (!string.IsNullOrWhiteSpace(spec.Note)) bottom = Box(20, bottom + 28, width - 40, new[] { new TextLine(spec.Note) }, Light, Ink);
            bottom += 20;
        }
        if (bottom > 16000) throw new ArgumentException("The org chart is too tall. Shorten its text or reduce the number of people.");
        root.SetAttributeValue("height", Math.Ceiling(bottom));
        root.SetAttributeValue("viewBox", FormattableString.Invariant($"0 0 {width} {Math.Ceiling(bottom)}"));
        root.AddFirst(new XElement(Svg + "rect", new XAttribute("width", "100%"), new XAttribute("height", "100%"), new XAttribute("fill", White)));
        var target = new RenderTargetBitmap(width, (int)Math.Ceiling(bottom), 96, 96, PixelFormats.Pbgra32);
        var paper = new DrawingVisual();
        using (var dc = paper.RenderOpen()) dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, bottom));
        target.Render(paper); target.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
        using var png = new MemoryStream(); encoder.Save(png);
        return new(root.ToString(), png.ToArray());
    }
}

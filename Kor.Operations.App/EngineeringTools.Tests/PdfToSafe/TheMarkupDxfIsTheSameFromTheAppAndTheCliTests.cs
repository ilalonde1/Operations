#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

/// <summary>The App and CLI export the same markup shapes and labels, with either layer choice.</summary>
/// <remarks>
/// Compares multisets of POLYLINE layers and ordered vertices, and TEXT layers, strings and
/// positions, rounded to 0.1 mm. Shapes are compared on Regent, and shapes plus labels on a
/// synthetic page. Does not compare scale detection, page-content mode, or colour
/// settings from the grid. Both routes use page 1 and scale 96 explicitly.
/// </remarks>
public sealed class TheMarkupDxfIsTheSameFromTheAppAndTheCliTests
{
    private readonly ITestOutputHelper _out;

    public TheMarkupDxfIsTheSameFromTheAppAndTheCliTests(ITestOutputHelper output) => _out = output;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothRoutesWriteTheSameMarkupDxf(bool layerByColour)
    {
        string pdf = FixturePath("regent_typ_floor.pdf");
        string root = Path.Combine(Path.GetTempPath(), $"kor-markup-dxf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string appPath = Path.Combine(root, "app.dxf");
            string cliPath = Path.Combine(root, "cli.dxf");
            var geometry = PdfGeometryExtractor.Extract(pdf, 96, pageNumber: 1, annotationsOnly: true);
            PdfGeometryExtractor.ExportDxf(geometry, appPath, layerByColour: layerByColour);
            var result = PdfOnlyBuild.WriteSheets(pdf, cliPath, 1, 1, 96,
                markup: true, korLayers: false, options: PdfIntakeOptions.Default, layerByColour: layerByColour);
            Assert.Equal(1, result.Written);

            var app = ReadDxf(appPath);
            var cli = ReadDxf(cliPath);
            _out.WriteLine($"layerByColour={layerByColour}: App POLYLINE={app.Polylines.Count}, TEXT={app.Texts.Count}; " +
                           $"CLI POLYLINE={cli.Polylines.Count}, TEXT={cli.Texts.Count}");
            Assert.NotEmpty(app.Polylines);
            Assert.Equal(app.Polylines, cli.Polylines);
            Assert.Equal(app.Texts, cli.Texts);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothRoutesWriteTheSameMarkupLabels(bool layerByColour)
    {
        string root = Path.Combine(Path.GetTempPath(), $"kor-markup-labels-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string pdf = Path.Combine(root, "markup.pdf");
            File.WriteAllBytes(pdf, MarkupPdf());
            string appPath = Path.Combine(root, "app.dxf");
            string cliPath = Path.Combine(root, "cli.dxf");
            var geometry = PdfGeometryExtractor.Extract(pdf, 96, pageNumber: 1, annotationsOnly: true);
            PdfGeometryExtractor.ExportDxf(geometry, appPath, layerByColour: layerByColour);
            var result = PdfOnlyBuild.WriteSheets(pdf, cliPath, 1, 1, 96,
                markup: true, korLayers: false, options: PdfIntakeOptions.Default, layerByColour: layerByColour);
            Assert.Equal(1, result.Written);

            var app = ReadDxf(appPath);
            var cli = ReadDxf(cliPath);
            _out.WriteLine($"Synthetic page, layerByColour={layerByColour}: App POLYLINE={app.Polylines.Count}, TEXT={app.Texts.Count}; " +
                           $"CLI POLYLINE={cli.Polylines.Count}, TEXT={cli.Texts.Count}");
            Assert.NotEmpty(app.Texts);
            Assert.Equal(app.Texts, cli.Texts);
            Assert.Equal(app.Polylines, cli.Polylines);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] MarkupPdf()
    {
        string content = "0 G 0.5 w 750 600 300 100 re S";
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1200 900] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R /Annots [6 0 R] >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream",
            "<< /Type /Annot /Subtype /Polygon /Rect [600 100 620 120] /Vertices [600 100 620 100 620 120 600 120] /C [1 0 0] /Contents (Check this column) /T (Ian) >>",
        ];
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (int i = 0; i < objects.Length; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        int xref = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
            pdf.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    private static string FixturePath(string name)
    {
        string dir = Path.GetDirectoryName(typeof(TheMarkupDxfIsTheSameFromTheAppAndTheCliTests).Assembly.Location)!;
        string path = Path.Combine(dir, "PdfToSafe", "Fixtures", name);
        if (!File.Exists(path))
        {
            string repoRelative = Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "PdfToSafe", "Fixtures", name));
            if (File.Exists(repoRelative)) path = repoRelative;
        }
        Assert.True(File.Exists(path), $"Fixture file not found: {path}");
        return path;
    }

    private sealed record Entity(string Type, List<(int Code, string Value)> Fields);
    private sealed record Snapshot(IReadOnlyList<string> Polylines, IReadOnlyList<string> Texts);

    private static Snapshot ReadDxf(string path)
    {
        string[] lines = File.ReadAllLines(path, DxfExporter.FileEncoding);
        Assert.Equal(0, lines.Length % 2);
        var entities = new List<Entity>();
        Entity? current = null;
        for (int i = 0; i < lines.Length; i += 2)
        {
            int code = int.Parse(lines[i], CultureInfo.InvariantCulture);
            if (code == 0)
            {
                current = new Entity(lines[i + 1].Trim(), new List<(int, string)>());
                entities.Add(current);
            }
            else current?.Fields.Add((code, lines[i + 1]));
        }

        var polylines = new List<string>();
        var texts = new List<string>();
        for (int i = 0; i < entities.Count; i++)
        {
            var entity = entities[i];
            if (entity.Type == "POLYLINE")
            {
                var vertices = new List<string>();
                int next = i + 1;
                while (next < entities.Count && entities[next].Type == "VERTEX")
                    vertices.Add(Position(entities[next++]));
                Assert.True(next < entities.Count && entities[next].Type == "SEQEND", "POLYLINE has no SEQEND.");
                Assert.NotEmpty(vertices);
                polylines.Add(JsonSerializer.Serialize(new { Layer = Field(entity, 8), Vertices = vertices }));
                i = next;
            }
            else if (entity.Type == "TEXT")
            {
                texts.Add(JsonSerializer.Serialize(new
                {
                    Layer = Field(entity, 8), Text = Field(entity, 1), Position = Position(entity),
                }));
            }
        }

        return new Snapshot(polylines.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            texts.OrderBy(x => x, StringComparer.Ordinal).ToList());
    }

    private static string Field(Entity entity, int code, string fallback = "")
        => entity.Fields.Where(p => p.Code == code).Select(p => p.Value).SingleOrDefault() ?? fallback;

    private static string Position(Entity entity)
    {
        string Coordinate(int code)
        {
            double value = double.Parse(Field(entity, code, "0"), CultureInfo.InvariantCulture);
            double rounded = Math.Round(value, 1, MidpointRounding.AwayFromZero);
            return (rounded == 0 ? 0.0 : rounded).ToString("0.0", CultureInfo.InvariantCulture);
        }
        return $"{Coordinate(10)},{Coordinate(20)},{Coordinate(30)}";
    }
}

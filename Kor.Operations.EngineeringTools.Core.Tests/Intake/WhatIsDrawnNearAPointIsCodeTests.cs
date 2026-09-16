#nullable enable
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// "What is drawn there, on any layer?" is a verb (`dxf-inspect --near`, 2026-09-16), not a scratch script: the
/// question that settled 31168's KW235 (a wall along a line no layer draws). WHAT THIS COVERS: the segments within
/// the reach are listed with their layer and distance, the ones outside are not, the count states X of Y. WHAT IT
/// DOES NOT: the reader's own tessellation of arcs and polylines (DxfPlanReader's tests), and the model half of the
/// question (`model-at`, E2kModelQueryTests).
/// </summary>
public sealed class WhatIsDrawnNearAPointIsCodeTests
{
    private const string Dxf =
        "0\r\nSECTION\r\n2\r\nENTITIES\r\n" +
        "0\r\nLINE\r\n8\r\nKOR_C_SLABEDG\r\n10\r\n0\r\n20\r\n0\r\n11\r\n100\r\n21\r\n0\r\n" +
        "0\r\nLINE\r\n8\r\nKOR_C_WALL\r\n10\r\n0\r\n20\r\n0\r\n11\r\n0\r\n21\r\n100\r\n" +
        "0\r\nLINE\r\n8\r\nA-ANNO\r\n10\r\n50\r\n20\r\n3\r\n11\r\n60\r\n21\r\n3\r\n" +
        "0\r\nENDSEC\r\n0\r\nEOF\r\n";

    [Fact]
    public void TheSegmentsWithinTheReachAreListedWithTheirLayerAndTheRestAreNot()
    {
        string path = Path.Combine(Path.GetTempPath(), "kor-tests", "near-" + Guid.NewGuid().ToString("N")[..8] + ".dxf");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Dxf);
        var verb = global::TakeoffVerbs.All.Single(v => v.Name == "dxf-inspect");
        var stdout = Console.Out;
        using var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            // (50,0): on the slab edge, 5 in from the annotation line at y = 3, 50 in from the wall along x = 0.
            Assert.Equal(0, verb.Run(["dxf-inspect", path, "--near", "50", "0", "5"]));
        }
        finally { Console.SetOut(stdout); File.Delete(path); }

        string text = captured.ToString();
        Assert.Contains("2 of 3 segments within 5 of (50,0)", text, StringComparison.Ordinal);
        Assert.Contains("KOR_C_SLABEDG", text, StringComparison.Ordinal);
        Assert.Contains("A-ANNO", text, StringComparison.Ordinal);
        Assert.DoesNotContain("KOR_C_WALL", text, StringComparison.Ordinal);
        // nearest first: the slab edge at 0, the annotation at 3
        Assert.True(text.IndexOf("KOR_C_SLABEDG", StringComparison.Ordinal) < text.IndexOf("A-ANNO", StringComparison.Ordinal));
    }
}

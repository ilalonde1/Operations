using System;
using System.IO;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

/// <summary>What the shipped scale detector actually returns for a sheet drawn in imperial.</summary>
public sealed class WhatTheScaleDetectorSaysProbe
{
    private readonly ITestOutputHelper _out;
    public WhatTheScaleDetectorSaysProbe(ITestOutputHelper output) => _out = output;

    [Theory]
    [InlineData("OAP-parcel11-arch-markup.pdf", "SCALE: 1/8\" = 1'-0\"  (1:96)")]
    [InlineData("31202-01 - Reinforcing Sheets - REVISED per JD markup 2026-07-27.pdf", "unknown")]
    public void WhatDetectScaleReturns(string file, string whatTheSheetSays)
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), file);
        if (!File.Exists(path)) { _out.WriteLine($"SKIPPED: not at {path}"); return; }

        int? detected = PdfGeometryExtractor.DetectScale(path, 1);
        _out.WriteLine($"{file}");
        _out.WriteLine($"   title block : {whatTheSheetSays}");
        _out.WriteLine($"   DetectScale : {(detected is null ? "NULL — nothing detected" : detected.ToString())}");
    }
}

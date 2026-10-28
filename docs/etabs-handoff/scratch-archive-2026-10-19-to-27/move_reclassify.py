"""Move PdfGeometryExtractor's reclassification block (the '// -- helpers' line to the class end) into Core, verbatim."""
app = r"C:\VIsual Studio Projects\Operations\Kor.Operations.App\EngineeringTools\PdfToSafe\PdfGeometryExtractor.cs"
core = r"C:\VIsual Studio Projects\Operations\Kor.Operations.EngineeringTools.Core\PdfToSafe\MarkupReclassifier.cs"
lines = open(app, encoding="utf-8").read().split("\n")
start = next(i for i, l in enumerate(lines) if "// ── helpers" in l)
end = max(i for i, l in enumerate(lines) if l == "    }")          # the class's closing brace
block = lines[start + 1:end]
header = [
    "#nullable enable",
    "using System;",
    "using System.Collections.Generic;",
    "using System.Linq;",
    "",
    "namespace Kor.Operations.EngineeringTools.PdfToSafe",
    "{",
    "    /// <summary>",
    "    /// A markup's colour says what it is (2026-10-19, moved verbatim from the App's PdfGeometryExtractor).",
    "    ///",
    "    /// The window turned a wall-sized burgundy region into a wall and a small one into a column; the stick-file",
    "    /// route, which builds the storeys and labels piers and spandrels, never called it - so a marked-up plan came",
    "    /// out of one with walls and no model, and out of the other with a model and the engineer's walls as floor.",
    "    /// One reclassifier, in Core, for both.",
    "    /// </summary>",
    "    public static class MarkupReclassifier",
    "    {",
]
open(core, "w", encoding="utf-8", newline="\n").write("\n".join(header + block + ["    }", "}", ""]))
forward = [
    "        /// <summary>The colour reclassifier, now in Core (<see cref=\"MarkupReclassifier\"/>).</summary>",
    "        public static ExtractedGeometry ReclassifyByColor(",
    "            ExtractedGeometry original,",
    "            Dictionary<(byte R, byte G, byte B), SlabColorSettings>? colorSettings,",
    "            Dictionary<int, string>? slabTypeOverrides = null,",
    "            Dictionary<int, string>? lineTypeOverrides = null,",
    "            Dictionary<int, string>? columnTypeOverrides = null,",
    "            double minOrphanSlabAreaMm2 = 2_000_000.0)",
    "            => MarkupReclassifier.ReclassifyByColor(original, colorSettings, slabTypeOverrides, lineTypeOverrides,",
    "                columnTypeOverrides, minOrphanSlabAreaMm2);",
]
out = lines[:start] + forward + lines[end:]
open(app, "w", encoding="utf-8", newline="").write("\n".join(out))
print("moved lines", start + 2, "to", end, "=", len(block), "lines")

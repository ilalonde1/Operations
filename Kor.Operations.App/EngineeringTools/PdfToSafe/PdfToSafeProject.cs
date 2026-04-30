using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    internal sealed class ColorMapping
    {
        public string ElementType   { get; set; } = "Slab";
        public double ThicknessMm   { get; set; } = 200.0;
        public double SdlKPa        { get; set; } = 0.0;
        public double LiveKPa       { get; set; } = 0.0;
        public bool   Excluded    { get; set; } = false;
        public string GradeCode   { get; set; } = "C30";
    }

    internal sealed class PdfToSafeProject
    {
        public string PdfPath { get; set; } = "";
        public int PageNumber { get; set; } = 1;
        public int ScaleDenominator { get; set; } = 100;
        public Dictionary<string, ColorMapping> ColorMappings { get; set; } = new();
        /// <summary>
        /// Per-element type overrides set via right-click on preview shapes.
        /// Key format: "slab_0", "line_3", "column_1". Value: "Slab", "Beam", "Column", "Ignore".
        /// </summary>
        public Dictionary<string, string> ElementTypeOverrides { get; set; } = new();
        /// <summary>Indices of slabs/lines/columns the user left-click-excluded from export.</summary>
        public List<int> ExcludedSlabs { get; set; } = new();
        public List<int> ExcludedLines { get; set; } = new();
        public List<int> ExcludedColumns { get; set; } = new();
        public ExportSettings ExportSettings { get; set; } = new();

        public static PdfToSafeProject Load(string path)
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<PdfToSafeProject>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new PdfToSafeProject();
        }

        public void Save(string path)
        {
            var json = JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }

        public static string ColorKey((byte R, byte G, byte B) c)
            => $"{c.R:X2}{c.G:X2}{c.B:X2}";

        public static (byte R, byte G, byte B) ParseKey(string key)
        {
            byte r = Convert.ToByte(key[0..2], 16);
            byte g = Convert.ToByte(key[2..4], 16);
            byte b = Convert.ToByte(key[4..6], 16);
            return (r, g, b);
        }
    }
}

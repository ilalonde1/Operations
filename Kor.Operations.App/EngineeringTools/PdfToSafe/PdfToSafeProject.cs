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
        public double SlabMinDiagonalMm { get; set; } = 1000.0;
        public double LineMinLengthMm { get; set; } = 200.0;
        public bool ExcludeGridLines { get; set; } = false;
        public Dictionary<string, ColorMapping> ColorMappings { get; set; } = new();

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

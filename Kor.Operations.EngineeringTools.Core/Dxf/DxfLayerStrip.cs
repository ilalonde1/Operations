using System.Text;

namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>
/// THE STRIP-A-LAYER DIFFERENTIAL (2026-09-15). When a model moves and the composer is suspected, the three-minute
/// instrument is: copy the set's DXFs with one layer's entities moved to a layer no role matches, compose both
/// folders, diff the models (`dxf-strip`, then `dxf-to-etabs` twice, then `model-diff`). On 31138 it settled in
/// three minutes what forty minutes of reading the composer had not: L1's lost columns were the plates' doing
/// (`ModelDoubleHeightMembersOnBothFloors` stops a column at a storey that gains a plate). It was a scratch script;
/// this is it in the repo. Group code 8 is the layer of every entity in a DXF's ENTITIES section; nothing else is
/// touched, so the copy differs from the original in those layer names only.
/// </summary>
public static class DxfLayerStrip
{
    /// <summary>The layer stripped entities are moved to: no wall, column or slab pattern matches it.</summary>
    public const string StrippedLayer = "X-STRIPPED";

    /// <summary>
    /// Every DXF under <paramref name="sourceFolder"/> matching <paramref name="fileGlob"/> copied to <paramref name="outFolder"/>
    /// with each entity on a layer containing one of <paramref name="layerPatterns"/> (case-insensitive) moved to
    /// <see cref="StrippedLayer"/>. Files not matching the glob are copied as they are, so the folder still composes.
    /// Returns per file how many entities moved.
    /// </summary>
    public static IReadOnlyList<(string File, int Stripped)> Strip(string sourceFolder, string outFolder, IReadOnlyList<string> layerPatterns, string fileGlob = "*.dxf")
    {
        ArgumentNullException.ThrowIfNull(layerPatterns);
        if (layerPatterns.Count == 0) throw new ArgumentException("Give at least one layer pattern.", nameof(layerPatterns));
        Directory.CreateDirectory(outFolder);
        var result = new List<(string, int)>();
        var wanted = new HashSet<string>(Directory.EnumerateFiles(sourceFolder, fileGlob).Select(Path.GetFileName)!, StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.EnumerateFiles(sourceFolder, "*.dxf").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileName(path);
            string dest = Path.Combine(outFolder, name);
            if (!wanted.Contains(name)) { File.Copy(path, dest, overwrite: true); result.Add((name, 0)); continue; }
            var (text, stripped) = StripText(File.ReadAllText(path, Latin1), layerPatterns);
            File.WriteAllText(dest, text, Latin1);
            result.Add((name, stripped));
        }
        foreach (string other in Directory.EnumerateFiles(sourceFolder).Where(p => !p.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase)))
            File.Copy(other, Path.Combine(outFolder, Path.GetFileName(other)), overwrite: true);
        return result;
    }

    /// <summary>The DXF text with matching layers renamed, and how many group-8 values were changed. Line endings are kept.</summary>
    public static (string Text, int Stripped) StripText(string dxf, IReadOnlyList<string> layerPatterns)
    {
        ArgumentNullException.ThrowIfNull(dxf);
        ArgumentNullException.ThrowIfNull(layerPatterns);
        var lines = dxf.Split('\n');   // a CR stays on its line, so CRLF files come back CRLF
        int n = 0;
        bool inEntities = false;
        for (int i = 0; i < lines.Length - 1; i++)
        {
            string code = lines[i].Trim();
            string value = lines[i + 1].TrimEnd('\r').Trim();
            if (code == "2" && i > 0 && lines[i - 1].Trim().Equals("SECTION", StringComparison.OrdinalIgnoreCase) && value.Equals("ENTITIES", StringComparison.OrdinalIgnoreCase)) inEntities = true;
            else if (code == "0" && value.Equals("ENDSEC", StringComparison.OrdinalIgnoreCase)) inEntities = false;
            if (!inEntities || code != "8") continue;
            if (layerPatterns.Any(p => value.Contains(p, StringComparison.OrdinalIgnoreCase)))
            {
                lines[i + 1] = StrippedLayer + (lines[i + 1].EndsWith('\r') ? "\r" : "");
                n++;
            }
        }
        return (string.Join("\n", lines), n);
    }

    private static readonly Encoding Latin1 = Encoding.Latin1;
}

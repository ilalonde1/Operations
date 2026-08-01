#nullable enable
using System;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace Kor.Operations.App.FileSync;

// Persists Command Center window placement to a small JSON file in
// %LocalAppData%\KorOperations so the window opens where the operator left
// it. Self-contained -- no Properties.Settings dependency to avoid taking
// on an assembly-level config infrastructure for one feature.
public sealed class FileSyncWindowState
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool IsMaximized { get; set; }

    private static string StatePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KorOperations",
        "filesync-command-center.json");

    public static FileSyncWindowState? TryLoad()
    {
        try
        {
            var path = StatePath;
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<FileSyncWindowState>(json);
        }
        catch
        {
            return null;
        }
    }

    public void TrySave()
    {
        try
        {
            var path = StatePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this));
        }
        catch
        {
            // Window placement is purely a nice-to-have; don't surface
            // failures to the user.
        }
    }

    // Returns true if the rect overlaps any virtual-screen area. Used to
    // reject saved coordinates from a monitor that's since been unplugged
    // so the window doesn't open invisibly off-screen.
    public bool IsOnScreen()
    {
        var vsLeft = SystemParameters.VirtualScreenLeft;
        var vsTop = SystemParameters.VirtualScreenTop;
        var vsRight = vsLeft + SystemParameters.VirtualScreenWidth;
        var vsBottom = vsTop + SystemParameters.VirtualScreenHeight;
        var thisRight = Left + Width;
        var thisBottom = Top + Height;
        // At least 100px of overlap so a sliver-on-screen window is treated
        // as off-screen and recentred.
        return Width > 100 && Height > 100
            && thisRight > vsLeft + 100
            && thisBottom > vsTop + 100
            && Left < vsRight - 100
            && Top < vsBottom - 100;
    }
}

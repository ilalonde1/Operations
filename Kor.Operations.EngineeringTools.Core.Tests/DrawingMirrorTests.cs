using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// The mirror exists so no run reads 139 sheets over the VPN. What matters about it is the two
/// ways it can be wrong: copying something that was already local, and serving a set that is not
/// the set on the share.
/// </summary>
public class DrawingMirrorTests
{
    [Fact]
    [Trait("Speed", "Fast")]
    public void ALocalFolderIsUsedWhereItStandsAndNothingIsCopied()
    {
        string source = Path.Combine(Path.GetTempPath(), $"kor-mirror-src-{Guid.NewGuid():N}");
        string root = Path.Combine(Path.GetTempPath(), $"kor-mirror-root-{Guid.NewGuid():N}");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "a.dxf"), "0\nSECTION\n");

        string was = DrawingMirror.Root;
        try
        {
            DrawingMirror.Root = root;

            // A drawing folder already on this machine must be read where it is. Copying it would
            // add a second copy that can go stale against the one the engineer is editing.
            Assert.Equal(source, DrawingMirror.Folder(source));
            Assert.False(Directory.Exists(root), "a local folder was mirrored; it should have been left alone");
        }
        finally
        {
            DrawingMirror.Root = was;
            Directory.Delete(source, recursive: true);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Speed", "Fast")]
    public void AMissingOrEmptyPathIsHandedBackRatherThanInvented()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"kor-mirror-none-{Guid.NewGuid():N}");

        // The caller's own "is it there" check must fail exactly as it did before the mirror
        // existed, rather than being turned into a mirror error about a path nobody gave.
        Assert.Equal(missing, DrawingMirror.Folder(missing));
        Assert.Equal(string.Empty, DrawingMirror.Folder(string.Empty));
        Assert.Equal(missing, DrawingMirror.SingleFile(missing));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void TheShareIsMirroredWholeOrNotAtAll()
    {
        const string remote =
            @"\\Kor-fs01\Projects\Projects\03 Residential\31168-01 (YMCA Langara Vancouver)" +
            @"\02 Engineering\02 Lateral Design\01 ETABS Models\_DXF-from-Revit-2026-08-26";

        if (!Directory.Exists(remote)) return; // share unreachable; the publish path is not exercised here

        string local = DrawingMirror.Folder(remote);

        Assert.NotEqual(remote, local);
        Assert.Equal(
            Directory.GetFiles(remote, "*.dxf", SearchOption.TopDirectoryOnly).Length,
            Directory.GetFiles(local, "*.dxf", SearchOption.TopDirectoryOnly).Length);

        // Asked twice, it does not copy twice.
        Assert.Equal(local, DrawingMirror.Folder(remote));
    }
}

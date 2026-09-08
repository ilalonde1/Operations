using System;
using System.IO;
using System.Linq;
using Kor.Operations.StandardDetails;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.StandardDetails;

/// <summary>
/// Covers local replacement preserving the old bytes, retaining five backups for this master only,
/// and first publish without a backup. Does not cover SMB, Revit, or an OS crash during replacement.
/// A same-class fault this would not catch: PublishAsync reporting a different backup path to the UI.
/// </summary>
public sealed class MasterPublisherArchiveTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kor-master-archive-" + Guid.NewGuid().ToString("N"));

    public MasterPublisherArchiveTests() => Directory.CreateDirectory(_folder);

    [Fact]
    public void Replacement_archives_previous_bytes_and_keeps_newest_five_for_this_master()
    {
        var master = Path.Combine(_folder, "MASTER.rvt");
        var temp = Path.Combine(_folder, "temp.rvt");
        var archive = Path.Combine(_folder, "_archive");
        Directory.CreateDirectory(archive);
        for (var day = 1; day <= 7; day++)
        {
            File.WriteAllText(Path.Combine(archive, $"MASTER.200001{day:D2}-000000.rvt"), "older");
        }

        var unrelated = Path.Combine(archive, "OTHER.20000101-000000.rvt");
        File.WriteAllText(unrelated, "other master");
        File.WriteAllText(master, "last good master");
        File.WriteAllText(temp, "new master");

        var backup = MasterPublisher.ReplaceMaster(temp, master);

        Assert.NotNull(backup);
        Assert.Equal("last good master", File.ReadAllText(backup));
        Assert.Equal("new master", File.ReadAllText(master));
        Assert.False(File.Exists(temp));
        var expected = Enumerable.Range(4, 4).Select(day => $"MASTER.200001{day:D2}-000000.rvt")
            .Append(Path.GetFileName(backup)).OrderBy(name => name).ToArray();
        Assert.Equal(expected, Directory.GetFiles(archive, "MASTER.*.rvt").Select(Path.GetFileName).OrderBy(name => name).ToArray());
        Assert.Equal("other master", File.ReadAllText(unrelated));
    }

    [Fact]
    public void First_publish_moves_the_file_without_an_archive()
    {
        var master = Path.Combine(_folder, "MASTER.rvt");
        var temp = Path.Combine(_folder, "temp.rvt");
        File.WriteAllText(temp, "first master");

        Assert.Null(MasterPublisher.ReplaceMaster(temp, master));

        Assert.Equal("first master", File.ReadAllText(master));
        Assert.False(Directory.Exists(Path.Combine(_folder, "_archive")));
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);
}

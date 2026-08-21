#nullable enable
using System;
using System.IO;
using Kor.Operations.App.FileSync;
using Xunit;

namespace Kor.Operations.App.Tests;

public sealed class FileSyncModeSummaryTests
{
    [Fact]
    public void Derive_returns_live_when_all_jobs_are_live()
    {
        var jobs = new[]
        {
            new JobRow { JobName = "A", Mode = "Live" },
            new JobRow { JobName = "B", Mode = "live" },
        };

        Assert.Equal("Live", FileSyncModeSummary.Derive(jobs));
    }

    [Fact]
    public void Derive_returns_mixed_when_job_modes_disagree()
    {
        var jobs = new[]
        {
            new JobRow { JobName = "A", Mode = "Live" },
            new JobRow { JobName = "B", Mode = "Shadow" },
        };

        Assert.Equal(FileSyncModeSummary.Mixed, FileSyncModeSummary.Derive(jobs));
    }

    [Fact]
    public void ApplyToHeartbeats_ignores_global_mode_and_uses_job_modes()
    {
        var heartbeats = new[]
        {
            new HeartbeatRow { HostName = "KOR-APP01", GlobalMode = "Shadow" },
        };
        var jobs = new[]
        {
            new JobRow { JobName = "A", Mode = "Live" },
            new JobRow { JobName = "B", Mode = "Live" },
        };

        var projected = FileSyncModeSummary.ApplyToHeartbeats(heartbeats, jobs);

        Assert.Equal("Shadow", projected[0].GlobalMode);
        Assert.Equal("Live", projected[0].JobModeSummary);
    }

    [Fact]
    public void Heartbeat_grid_binds_to_derived_job_mode_summary_not_global_mode()
    {
        var repoRoot = GetRepoRoot();
        var xaml = File.ReadAllText(Path.Combine(repoRoot, "Kor.Operations.App", "FileSync", "FileSyncCommandCenterWindow.xaml"));

        Assert.Contains("Header=\"Job modes\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding JobModeSummary}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"Default mode\"", xaml, StringComparison.Ordinal);
    }

    private static string GetRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Kor.Operations.App"))
                && Directory.Exists(Path.Combine(dir.FullName, "Kor.Operations.FileSync.Service")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
    }
}

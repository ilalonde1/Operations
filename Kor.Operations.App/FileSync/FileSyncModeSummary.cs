#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Kor.Operations.App.FileSync;

public static class FileSyncModeSummary
{
    public const string Mixed = "Mixed";
    public const string Unknown = "Unknown";

    public static IReadOnlyList<HeartbeatRow> ApplyToHeartbeats(
        IEnumerable<HeartbeatRow> heartbeats,
        IEnumerable<JobRow> jobs)
    {
        var jobModeSummary = Derive(jobs);
        return heartbeats
            .Select(h => new HeartbeatRow
            {
                HostName = h.HostName,
                StartedAt = h.StartedAt,
                LastHeartbeatAt = h.LastHeartbeatAt,
                GlobalMode = h.GlobalMode,
                JobModeSummary = jobModeSummary,
                ServiceVersion = h.ServiceVersion,
                WatcherGen = h.WatcherGen,
                JobsRegistered = h.JobsRegistered,
            })
            .ToList();
    }

    public static string Derive(IEnumerable<JobRow> jobs)
    {
        var modes = jobs
            .Select(j => Normalize(j.Mode))
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return modes.Count switch
        {
            0 => Unknown,
            1 => modes[0],
            _ => Mixed,
        };
    }

    private static string Normalize(string? mode)
    {
        if (string.Equals(mode, "Live", StringComparison.OrdinalIgnoreCase))
            return "Live";

        if (string.Equals(mode, "Shadow", StringComparison.OrdinalIgnoreCase))
            return "Shadow";

        return mode?.Trim() ?? string.Empty;
    }
}

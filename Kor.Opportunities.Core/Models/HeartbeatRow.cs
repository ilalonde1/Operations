#nullable enable
using System;

namespace Kor.Opportunities.Core.Models;

/// <summary>
/// One row from <c>opportunities.ServiceHeartbeat</c>. Read by the WPF
/// admin tab to display "Worker last seen X minutes ago, version Y".
/// </summary>
public sealed record HeartbeatRow(
    string ServiceName,
    string MachineName,
    string? Version,
    DateTimeOffset LastBeatUtc,
    DateTimeOffset? LastIngestionEndedUtc);

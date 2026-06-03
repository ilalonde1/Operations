#nullable enable
using System;

namespace Kor.Opportunities.Data.Ingestion;

public sealed record JobScheduleRow(
    string JobName,
    string? CronSchedule,
    bool Enabled,
    string? Category,
    string? LastReportPath,
    DateTimeOffset? LastRunAtUtc,
    bool? LastSuccess,
    string? LastSummary);

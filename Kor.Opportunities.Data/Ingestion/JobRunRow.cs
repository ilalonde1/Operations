#nullable enable
using System;

namespace Kor.Opportunities.Data.Ingestion;

public sealed record JobRunRow(
    long Id,
    string JobName,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    bool? Success,
    string? Summary,
    string? Error);

#nullable enable
using System;

namespace Kor.Opportunities.Data.IndustryEvents;

public sealed record IndustryEventRow(
    long Id,
    string SourceKey,
    string? Name,
    string? Organizer,
    string? EventType,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? Recurrence,
    string? City,
    string? Market,
    string? Format,
    string? SectorsThemes,
    string? Audience,
    string? TargetsPresent,
    string? RegistrationUrl,
    string? CostNote,
    string? KorRelevance,
    string? SourceNote,
    DateTimeOffset? RetiredAtUtc,
    string? RetiredReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long? IndustryEventSourceId = null,
    DateTimeOffset? LastSeenAtUtc = null);

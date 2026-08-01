#nullable enable
using System;

namespace Kor.Opportunities.Data.IndustryEvents;

public sealed record IndustryEventRecord(
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
    string? SourceNote);

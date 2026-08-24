#nullable enable
using System;

namespace Kor.Opportunities.Data.IndustryEvents;

/// <summary>
/// An industry event to upsert. <paramref name="SourceKey"/> is the dedup key:
/// SHA-1 of <c>name|yyyy-MM-dd</c>, matching the convention the 2026 manual
/// loads used, so an ingested row merges onto its hand-curated twin instead of
/// duplicating it.
/// </summary>
/// <param name="IndustryEventSourceId">
/// The calendar that produced this row; NULL for hand-curated events.
/// </param>
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
    string? SourceNote,
    long? IndustryEventSourceId = null);

#nullable enable
using System;

namespace Kor.Opportunities.Data.AwardPrograms;

public sealed record AwardProgramUpsert(
    string NaturalKey,
    string AwardingBody,
    string ProgramName,
    int? CycleYear,
    string? Category,
    string? Discipline,
    string? Region,
    string? EligibilitySummary,
    DateOnly? SubmissionDeadline,
    string? EntryFee,
    string? Url,
    string SourceProvider);

public sealed record AwardProgramRow(
    long Id,
    string NaturalKey,
    string AwardingBody,
    string ProgramName,
    int? CycleYear,
    string? Category,
    string? Discipline,
    string? Region,
    string? EligibilitySummary,
    DateOnly? SubmissionDeadline,
    string? EntryFee,
    string? Url,
    DateTimeOffset FirstSeenAtUtc,
    DateTimeOffset LastSeenAtUtc);

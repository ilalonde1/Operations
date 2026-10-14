#nullable enable
using System.Collections.Generic;

namespace Kor.Opportunities.Data.MajorProjects;

public sealed record PursuitBrief(
    PursuitBriefProject Project,
    string? OwnerClendorClientId,
    string? ArchitectClendorClientId,
    PursuitBriefArchitect Architect,
    PursuitBriefOwnerProcurement OwnerProcurement,
    IReadOnlyList<PursuitBriefPerson> ArchitectContacts,
    IReadOnlyList<PursuitBriefPerson> OwnerContacts,
    IReadOnlyList<PursuitBriefCompetitorNote> CompetitorNotes,
    string? KorEdge,
    string? ThePlay,
    decimal? FitScore);

public sealed record PursuitBriefProject(
    long ProjectId,
    string ProjectName,
    string? Owner,
    string? Market,
    string? City,
    string? Sector,
    string? Stage,
    string EstCostDisplay,
    string? SourceUrl,
    string? StructuralEngineerName = null,
    long? StructuralEngineerOrgId = null,
    string? StructuralEngineerKind = null)
{
    // Shared by the screen, PDF and approach context. Organisation-wide partner
    // patterns must never manufacture a project appointment or an available seat.
    public string StructuralAppointmentDisplay => StructuralEngineerOrgId.HasValue
        ? $"Recorded project structural engineer: {StructuralEngineerName ?? "Unnamed organization"}. " +
          (StructuralEngineerKind == "KorStructural"
              ? "This is KOR's recorded appointment."
              : "Availability for KOR is not established.")
        : !string.IsNullOrWhiteSpace(StructuralEngineerName)
            ? $"Source names {StructuralEngineerName}; organization link unresolved. An open seat is not established."
            : "Project structural engineer unknown / not recorded. An open seat is not established.";
}

public sealed record PursuitBriefArchitect(
    string? ArchitectName,
    long? ArchitectOrgId,
    string? SeatStatus,
    string? SeatPriority,
    string? DisplacementRead,
    IReadOnlyList<string> RecurringStructuralPartners);

public sealed record PursuitBriefOwnerProcurement(
    string? ProcurementMethod,
    string? RosterProgram,
    string? EvaluationCriteria,
    string? BudgetCadence);

public sealed record PursuitBriefPerson(
    string? Name,
    string? Title,
    string? Role,
    string? Email);

public sealed record PursuitBriefCompetitorNote(
    string Firm,
    string? CapacityRead);

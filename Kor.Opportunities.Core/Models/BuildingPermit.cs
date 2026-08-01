#nullable enable
using System;

namespace Kor.Opportunities.Core.Models;

public sealed record PermitSourceRow(
    long Id,
    string Name,
    string Adapter,
    string Endpoint,
    string? Region,
    string? Municipality,
    bool IsActive,
    DateTimeOffset? LastPolledAtUtc);

public sealed record BuildingPermitUpsert(
    long PermitSourceId,
    string ExternalId,
    string? PermitNumber,
    string? PermitCategory,
    string? WorkType,
    string? ProjectDescription,
    decimal? EstimatedValue,
    int? NumberOfDwellingUnits,
    string? Address,
    string? City,
    string? PostalCode,
    string? GeoLocalArea,
    decimal? Latitude,
    decimal? Longitude,
    DateTime? AppliedDate,
    DateTime? IssuedDate,
    string? OwnerName,
    string? ApplicantName,
    string? ContractorName,
    string? SpecificUseCategory,
    string? PropertyUse,
    string? RawJson);

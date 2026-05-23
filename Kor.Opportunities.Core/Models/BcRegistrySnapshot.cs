#nullable enable
using System;

namespace Kor.Opportunities.Core.Models;

public sealed record BcRegistrySnapshot(
    string? TopicId,
    string? LegalName,
    string? EntityType,
    string? Status,
    DateTime? IncorporationDate,
    string? Jurisdiction,
    string? BusinessNumber,
    string? RegisteredOffice);

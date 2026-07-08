#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Opportunities.Core.Deltek;

/// <summary>
/// Reads the KOR employee directory (Deltek EMMain) — the authoritative
/// name↔email source used to fill the BD staff directory (plan D2). Read-only.
/// </summary>
public interface IKorStaffDirectoryAccessor
{
    /// <summary>Active employees that have an email address.</summary>
    Task<IReadOnlyList<DeltekStaffRow>> GetActiveStaffAsync(CancellationToken ct);
}

public sealed record DeltekStaffRow(
    string Employee,
    string? FirstName,
    string? LastName,
    string Email);

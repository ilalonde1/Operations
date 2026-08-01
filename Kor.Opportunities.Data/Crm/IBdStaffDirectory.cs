#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Opportunities.Data.Crm;

/// <summary>
/// The BD staff directory (opportunities.BdStaff, plan D2). Resolves a pursuit
/// owner spelling (CrmEngagements.OwnerStaffId — a UPN or a first name) to a
/// mailbox. Replaces the hand-typed MorningReportOwnerEmailMap the per-owner
/// digest routed on. Kept current by BdStaffDirectorySyncJob (Deltek EMMain).
/// </summary>
public interface IBdStaffDirectory
{
    /// <summary>Mailbox for an active owner key, or null if unrouted/unknown.</summary>
    Task<string?> ResolveMailboxAsync(string staffKey, CancellationToken ct);

    /// <summary>
    /// True if ANY directory row exists for the key (regardless of IsActive or
    /// whether it has a mailbox). Lets a caller tell "not in the directory yet"
    /// (safe to self-route a UPN owner) apart from "present but intentionally
    /// suppressed/unrouted" (do not self-route to a dead mailbox).
    /// </summary>
    Task<bool> IsKnownAsync(string staffKey, CancellationToken ct);

    Task<IReadOnlyList<BdStaffRow>> GetAllAsync(CancellationToken ct);

    /// <summary>
    /// Insert a directory row keyed on <paramref name="staffKey"/>, or update
    /// its Mailbox/DisplayName/IsActive/Source if it already exists. Used by the
    /// sync to register every employee email as a routable identity.
    /// </summary>
    Task UpsertAsync(string staffKey, string? mailbox, string? displayName, bool isActive, string source, CancellationToken ct);

    /// <summary>
    /// Fill Mailbox (and DisplayName) on an EXISTING row only when it is
    /// currently unrouted (Mailbox IS NULL) — never overwrites a set mailbox,
    /// and never creates a row. Returns true if a row was filled. Used to bridge
    /// a legacy first-name owner to a mailbox on an unambiguous EMMain match.
    /// </summary>
    Task<bool> FillMailboxIfEmptyAsync(string staffKey, string mailbox, string? displayName, string source, CancellationToken ct);
}

public sealed record BdStaffRow(
    int Id,
    string StaffKey,
    string? Mailbox,
    string? DisplayName,
    bool IsActive,
    string Source);

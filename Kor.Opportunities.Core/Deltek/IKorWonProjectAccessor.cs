#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Opportunities.Core.Deltek;

public interface IKorWonProjectAccessor
{
    /// <summary>
    /// Fetches won projects (PR.ChargeType='R', root rows) for a Clendor ClientID,
    /// ordered by project start/open date DESC. Live read from Deltek via ODBC.
    /// </summary>
    Task<IReadOnlyList<KorWonProjectRow>> GetForClientAsync(
        string clendorClientId,
        int maxRows,
        CancellationToken ct);

    /// <summary>
    /// Aggregate signal across all Clendor clients: (ClendorClientId, MaxStartDate, Count).
    /// Single query, used by the nightly refresh job to populate CanonicalOrg.LastKorProjectAtUtc.
    /// </summary>
    Task<IReadOnlyList<KorWonProjectAggregate>> GetAllClientAggregatesAsync(CancellationToken ct);
}

public sealed record KorWonProjectRow(
    string Wbs1,
    string Name,
    string? ProjectManager,
    string? Principal,
    DateTime? StartDate,
    DateTime? EndDate,
    string? Stage,
    decimal? FeeBilled);

public sealed record KorWonProjectAggregate(
    string ClendorClientId,
    DateTime LastStartDate,
    int ProjectCount);

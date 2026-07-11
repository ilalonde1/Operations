#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Opportunities.Core.Deltek;

public interface IKorPursuitDeltekAccessor
{
    /// <summary>
    /// Returns pursuits where PR.Stage is explicitly 'InPursuit', 'LOST', or 'DNP'.
    /// These are KOR's currently flagged pursuit board entries.
    /// </summary>
    Task<IReadOnlyList<DeltekPursuitRow>> GetExplicitStagePursuitsAsync(CancellationToken ct);

    /// <summary>
    /// Returns PR rows where ChargeType='P' (Promotional / pursuit cost tracking)
    /// that are NOT already covered by the explicit-stage query — the two result
    /// sets are mutually exclusive so one real pursuit can never sync under two
    /// external-source identities (audit-v2 #11).
    /// </summary>
    Task<IReadOnlyList<DeltekPursuitRow>> GetPromotionalPursuitsAsync(CancellationToken ct);

    /// <summary>
    /// Returns the current PR rows for specific WBS1 keys — the won-transition
    /// sweep (audit-v2 #11). This Deltek has no 'Won' stage code: a won pursuit's
    /// Stage becomes '~WDEF~' (it became a project) and the row drops out of both
    /// pull queries, freezing the KorPursuits copy at Pursuing forever without
    /// this targeted lookup.
    /// </summary>
    Task<IReadOnlyList<DeltekPursuitRow>> GetPursuitsByWbs1Async(IReadOnlyCollection<string> wbs1Keys, CancellationToken ct);
}

public sealed record DeltekPursuitRow(
    string Wbs1,
    string Name,
    string? ClientID,
    string? BuyerName,
    string Stage,
    string ChargeType,
    string? LostTo,
    string? LostToName,
    DateTime? OpenDate,
    DateTime? ProposalDueDate,
    DateTime? AwardDate,
    string? ProjMgr,
    string? Principal,
    string? BusinessDeveloper,
    string? ProposalManager);

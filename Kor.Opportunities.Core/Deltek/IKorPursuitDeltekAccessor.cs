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
    /// Returns PR rows where ChargeType='P' (Promotional / pursuit cost tracking).
    /// Older historical pursuit records predate PR.Stage flagging.
    /// </summary>
    Task<IReadOnlyList<DeltekPursuitRow>> GetPromotionalPursuitsAsync(CancellationToken ct);
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

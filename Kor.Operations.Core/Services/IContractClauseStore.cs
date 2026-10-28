#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core.Models.Contracts;

namespace Kor.Operations.Core.Services;

/// <summary>Reads the contract clause library (KorTransmittals dbo.vw_ContractPositions). Read-only: the library is
/// rebuilt by tools/ContractClauseLibrary, never edited from the app.</summary>
public interface IContractClauseStore
{
    Task<List<ContractPosition>> SearchAsync(ContractPositionQuery query, CancellationToken ct = default);

    /// <summary>Distinct client names in the library, for the filter.</summary>
    Task<List<string>> ListClientsAsync(CancellationToken ct = default);
}

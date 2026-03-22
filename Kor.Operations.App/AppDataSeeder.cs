#nullable enable
using Kor.Operations.App.FeeProposal;
using Kor.Operations.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Kor.Operations;

internal static class AppDataSeeder
{
    internal static void Seed(IServiceProvider services)
    {
        ProposalStaffSeed.EnsureSeeded(services.GetRequiredService<IProposalStaffStore>());
        ProposalLibrarySeed.EnsureSeeded(services.GetRequiredService<IProposalBlockLibraryStore>());
    }
}

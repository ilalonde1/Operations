#nullable enable
using System.Threading.Tasks;

namespace Kor.Operations.App.Opportunities;

public sealed class CompetitionInfoViewModel
{
    public CompetitionInfoViewModel(
        CompetitionRfpsViewModel rfps,
        CompetitionAwardsViewModel awards)
    {
        Rfps = rfps;
        Awards = awards;
    }

    public CompetitionRfpsViewModel Rfps { get; }
    public CompetitionAwardsViewModel Awards { get; }

    public async Task InitializeAsync()
    {
        await Rfps.InitializeAsync().ConfigureAwait(true);
        await Awards.InitializeAsync().ConfigureAwait(true);
    }
}

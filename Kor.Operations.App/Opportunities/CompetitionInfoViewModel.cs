#nullable enable
using System.Threading.Tasks;

namespace Kor.Operations.App.Opportunities;

public sealed class CompetitionInfoViewModel : Kor.Operations.Services.IAiContextProvider
{
    // BD-Audit-2026-06-09 M16: expose Competition Info screen state to AI.
    // Registered alongside Rfps/Awards children, which carry the filter detail.
    public string ProviderName => "BD Competition Info";
    public bool HasData => true;
    public string BuildContext()
        => $"BD Competition Info screen open — Historical RFPs tab: {Rfps.Rows.Count:N0} rows ({Rfps.StatusText}); Awards tab: {Awards.Rows.Count:N0} rows ({Awards.StatusText}).";
    public string BuildLocalContext() => BuildContext();

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

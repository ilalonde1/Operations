#nullable enable
using System.Windows;

namespace Kor.Operations;

internal sealed record HomeTileVisibilityState(
    Visibility Financials,
    Visibility Compensation,
    Visibility PmTools,
    Visibility StandardDetails,
    Visibility GeneralTools,
    Visibility FeeProposalBuilder,
    Visibility EngineeringTools,
    Visibility FileSyncCommandCenter,
    Visibility MondayBriefing,
    Visibility CooCard,
    Visibility Opportunities,
    Visibility BusinessDevelopment,
    Visibility BdReports)
{
    internal static HomeTileVisibilityState ForSecurityLookupFailure() => new(
        Financials: Visibility.Collapsed,
        Compensation: Visibility.Collapsed,
        PmTools: Visibility.Visible,
        StandardDetails: Visibility.Visible,
        GeneralTools: Visibility.Visible,
        FeeProposalBuilder: Visibility.Visible,
        EngineeringTools: Visibility.Visible,
        FileSyncCommandCenter: Visibility.Collapsed,
        MondayBriefing: Visibility.Collapsed,
        CooCard: Visibility.Collapsed,
        Opportunities: Visibility.Collapsed,
        BusinessDevelopment: Visibility.Collapsed,
        BdReports: Visibility.Collapsed);
}

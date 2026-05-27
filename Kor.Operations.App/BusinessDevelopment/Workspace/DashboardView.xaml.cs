#nullable enable
using System.Threading;
using System.Windows.Controls;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    private async void DashboardView_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel vm)
        {
            await vm.LoadAsync(CancellationToken.None).ConfigureAwait(true);
        }
    }
}

#nullable enable
using System;
using System.Threading;
using System.Windows.Controls;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    public DashboardView(DashboardViewModel vm)
    {
        InitializeComponent();
        DataContext = vm ?? throw new ArgumentNullException(nameof(vm));
    }

    private async void DashboardView_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel vm)
        {
            await vm.LoadAsync(CancellationToken.None).ConfigureAwait(true);
        }
    }
}

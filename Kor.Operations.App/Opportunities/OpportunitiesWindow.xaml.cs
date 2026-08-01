#nullable enable
using System;
using System.Windows;
using Kor.Operations.Services;

namespace Kor.Operations.App.Opportunities;

public partial class OpportunitiesWindow : Window
{
    public OpportunitiesWindow(OpportunitiesViewModel vm, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(services);

        InitializeComponent();
        ViewHost.Content = new OpportunitiesView(vm, services);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await HeaderLoader.ApplyAsync(HeaderBar).ConfigureAwait(true);
        }
        catch
        {
            // Header identity is cosmetic and should not block the window.
        }
    }
}

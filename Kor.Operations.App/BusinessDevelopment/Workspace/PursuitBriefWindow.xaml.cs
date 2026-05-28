#nullable enable
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using Kor.Operations.Services;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

public partial class PursuitBriefWindow : Window
{
    public PursuitBriefWindow(PursuitBriefViewModel vm)
    {
        InitializeComponent();
        DataContext = vm ?? throw new ArgumentNullException(nameof(vm));
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await HeaderLoader.ApplyAsync(HeaderBar).ConfigureAwait(true);
        }
        catch
        {
            // Header identity is cosmetic and should not block loading the brief.
        }
    }

    private void OnHyperlinkRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        if (e.Uri is null || !e.Uri.IsAbsoluteUri)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.ToString(),
                UseShellExecute = true,
            });
            e.Handled = true;
        }
        catch (Exception ex)
        {
            if (DataContext is PursuitBriefViewModel vm)
            {
                vm.StatusMessage = $"Open source failed: {ex.GetType().Name}: {ex.Message}";
            }
        }
    }
}

#nullable enable
using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Documents;
using Kor.Operations.Services;

namespace Kor.Operations.App.Opportunities;

/// <summary>
/// The RFP read view. Opened by a double-click on an RFP anywhere in the BD
/// workspace. "Edit record…" closes this window with <see cref="EditRequested"/>
/// set; the CALLER then runs its existing editor-and-save path, so the two save
/// paths (dashboard and Opportunities page) stay where their gates already are.
/// </summary>
public partial class OpportunityDetailWindow : Window
{
    private readonly OpportunityDetailViewModel _vm;
    private bool _aiRegistered;

    public OpportunityDetailWindow(OpportunityDetailViewModel vm)
    {
        InitializeComponent();
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        DataContext = vm;
        Title = $"RFP — {vm.Key}";
    }

    /// <summary>True when the user chose "Edit record…"; the caller opens the editor.</summary>
    public bool EditRequested { get; private set; }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_aiRegistered)
        {
            AppServices.Get<AppAiContextBuilder>().Register(_vm);
            _aiRegistered = true;
        }

        await HeaderLoader.ApplyAsync(HeaderBar);
        await _vm.LoadAsync(CancellationToken.None).ConfigureAwait(true);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_aiRegistered)
        {
            AppServices.Get<AppAiContextBuilder>().Unregister(_vm);
            _aiRegistered = false;
        }

        base.OnClosed(e);
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        EditRequested = true;
        DialogResult = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OpenPosting_Click(object sender, RoutedEventArgs e) => OpenUrl(_vm.PrimaryUrl);

    private void Link_Click(object sender, RoutedEventArgs e) => OpenUrl((sender as Hyperlink)?.Tag as string);

    private void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            MessageBox.Show(this, "This posting has no usable web link.", "RFP", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open the posting:\n{ex.Message}", "RFP", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

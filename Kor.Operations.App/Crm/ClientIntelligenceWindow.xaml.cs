#nullable enable
using System;
using System.Threading;
using System.Windows;
using Kor.Operations.Services;

namespace Kor.Operations.App.Crm;

public partial class ClientIntelligenceWindow : Window
{
    private readonly ClientIntelligenceViewModel _vm;
    private CancellationTokenSource? _cts;

    public ClientIntelligenceWindow(ClientIntelligenceViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        InitializeComponent();
        DataContext = _vm;

        AppServices.Get<AppAiContextBuilder>().Register(_vm);
    }

    /// <summary>
    /// Caller sets the client id before Show()/ShowDialog(). Loads on
    /// Window_Loaded so the spinner is visible, not before the window paints.
    /// </summary>
    public string? PendingClientId { get; set; }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PendingClientId)) return;
        _cts = new CancellationTokenSource();
        try
        {
            await _vm.LoadAsync(PendingClientId.Trim(), _cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // window closing
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        base.OnClosed(e);
        AppServices.Get<AppAiContextBuilder>().Unregister(_vm);
    }
}

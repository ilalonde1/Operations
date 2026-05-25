#nullable enable
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Navigation;
using Kor.Operations.Services;

namespace Kor.Operations.App.Opportunities;

public partial class MajorProjectsInventoryWindow : Window
{
    private readonly MajorProjectsInventoryViewModel _vm;
    private CancellationTokenSource? _cts;

    public MajorProjectsInventoryWindow(MajorProjectsInventoryViewModel vm, IServiceProvider services)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        _ = services ?? throw new ArgumentNullException(nameof(services));
        InitializeComponent();
        DataContext = _vm;

        // Per the firm-wide AI rule (memory: feedback_ai_context_provider.md):
        // every feature window registers its VM as a context provider so the
        // Ops chat can answer questions about the visible data. AppAiContextBuilder
        // is internal so we use the service locator (same trick FileSync uses) to
        // avoid an accessibility mismatch on this public ctor.
        var aiContextBuilder = AppServices.Get<AppAiContextBuilder>();
        aiContextBuilder.Register(_vm);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await ReloadAsync().ConfigureAwait(true);
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        base.OnClosed(e);

        // Unregister so a closed window doesn't keep stale data in the AI context.
        AppServices.Get<AppAiContextBuilder>().Unregister(_vm);
    }

    private async Task ReloadAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        try
        {
            await _vm.LoadAsync(_cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // window closing
        }
        catch (Exception ex)
        {
            _vm.StatusMessage = $"Load failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true,
            });
            e.Handled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Major Projects Inventory - Open Link Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

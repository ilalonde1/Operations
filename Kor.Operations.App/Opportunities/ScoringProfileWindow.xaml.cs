#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Kor.Operations.Services;

namespace Kor.Operations.App.Opportunities;

public partial class ScoringProfileWindow : Window
{
    private readonly ScoringProfileViewModel _vm;
    private CancellationTokenSource? _cts;
    private bool _aiRegistered;

    public ScoringProfileWindow(ScoringProfileViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        InitializeComponent();
        DataContext = _vm;
    }

    /// <summary>Fires when the OpportunitiesWindow caller wants to be told
    /// "the user just hit Save / Recalc — refresh your grid". Subscribers
    /// should re-load the opportunity list to pick up new scores.</summary>
    public event EventHandler? ProfilePersisted;

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // BD-Audit-2026-06-09 M16: let the AI assistant see the open scoring profile.
        if (!_aiRegistered)
        {
            AppServices.Get<AppAiContextBuilder>().Register(_vm);
            _aiRegistered = true;
        }

        _vm.Load();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_aiRegistered)
        {
            AppServices.Get<AppAiContextBuilder>().Unregister(_vm);
            _aiRegistered = false;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        base.OnClosed(e);
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _cts = new CancellationTokenSource();
        await _vm.SaveAsync(_cts.Token).ConfigureAwait(true);
        ProfilePersisted?.Invoke(this, EventArgs.Empty);
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _vm.ResetToDefaults();
    }

    private async void RecalcButton_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            this,
            "Recalculate scores for every opportunity using the currently-saved profile?\n\n"
            + "Tip: hit Save first if you've made unsaved edits — Recalc uses the persisted profile, not the in-memory one.",
            "Opportunities — Recalc All",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        try
        {
            var actor = $"{Environment.UserName}@korstructural.com";
            await _vm.RecalcAllAsync(actor, _cts.Token).ConfigureAwait(true);
            ProfilePersisted?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            // window closing
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

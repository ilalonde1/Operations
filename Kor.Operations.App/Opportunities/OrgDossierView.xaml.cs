#nullable enable
using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Navigation;
using Kor.Operations.Services;
using Kor.Opportunities.Data.Intel;

namespace Kor.Operations.App.Opportunities;

public partial class OrgDossierView : UserControl
{
    private readonly OrgDossierViewModel _vm;
    private CancellationTokenSource? _cts;
    private bool _initialized;

    public OrgDossierView(OrgDossierViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        InitializeComponent();
        DataContext = vm;
    }

    public async Task ShowOrgAsync(long canonicalOrgId, CancellationToken ct)
    {
        var cts = ReplaceCts(ct);
        try
        {
            await _vm.LoadAsync(canonicalOrgId, cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _vm.StatusMessage = $"Load failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private void OrgDossierView_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        AppServices.Get<AppAiContextBuilder>().Register(_vm);
    }

    private void OrgDossierView_Unloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        CancelAndDisposeCts();
        if (_initialized)
        {
            AppServices.Get<AppAiContextBuilder>().Unregister(_vm);
        }

        _initialized = false;
    }

    private void OnHyperlinkRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            if (e.Uri is null || !e.Uri.IsAbsoluteUri)
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.ToString(),
                UseShellExecute = true,
            });
            e.Handled = true;
        }
        catch (Exception ex)
        {
            _vm.StatusMessage = $"Open link failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private async void OnPersonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Hyperlink { DataContext: IntelPersonRow row })
        {
            return;
        }

        try
        {
            var personVm = AppServices.Get<PersonDossierViewModel>();
            await personVm.LoadAsync(row.Id, CancellationToken.None).ConfigureAwait(true);
            var win = new PersonDossierWindow(personVm);
            if (Window.GetWindow(this) is { } owner)
            {
                win.Owner = owner;
            }

            win.Show();
            e.Handled = true;
        }
        catch (Exception ex)
        {
            _vm.StatusMessage = $"Open person dossier failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private CancellationTokenSource ReplaceCts(CancellationToken ct)
    {
        var old = _cts;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        old?.Cancel();
        old?.Dispose();
        return _cts;
    }

    private void CancelAndDisposeCts()
    {
        var old = _cts;
        _cts = null;
        old?.Cancel();
        old?.Dispose();
    }
}

public sealed class FalseToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class IntelTypeLabelConverter : IValueConverter
{
    // R81: Mappings live in Kor.Opportunities.Data.Intel.IntelTypeHumanizer
    // (shared with PDF + DOCX brief generators). Adding a new SignalType /
    // ActionType / RiskType requires updating IntelTypeHumanizer only.
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = value as string ?? string.Empty;
        if (string.IsNullOrEmpty(s)) return s;

        // The converter doesn't know which type the raw string is. Probe each
        // map in turn; the maps are disjoint by construction except for
        // "CapacityStrain" which means the same thing as both Signal and Risk
        // ("Capacity strain"). Returns the input string if no map matches.
        var signal = Kor.Opportunities.Data.Intel.IntelTypeHumanizer.SignalType(s);
        if (!ReferenceEquals(signal, s) && signal != s) return signal;
        var action = Kor.Opportunities.Data.Intel.IntelTypeHumanizer.ActionType(s);
        if (!ReferenceEquals(action, s) && action != s) return action;
        var risk = Kor.Opportunities.Data.Intel.IntelTypeHumanizer.RiskType(s);
        if (!ReferenceEquals(risk, s) && risk != s) return risk;
        return s;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

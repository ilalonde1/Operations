#nullable enable
using System;
using System.Windows;
using Kor.Operations.Services;

namespace Kor.Operations.App.Opportunities;

public partial class KorPursuitDialog : Window
{
    private readonly KorPursuitDialogViewModel _vm;
    private bool _aiRegistered;

    public KorPursuitDialog(KorPursuitDialogViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    public KorPursuitDialogViewModel ViewModel => _vm;

    public long? SavedPursuitId { get; private set; }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // BD-Audit-2026-06-09 M16: let the AI assistant see the open pursuit dialog.
        if (!_aiRegistered)
        {
            AppServices.Get<AppAiContextBuilder>().Register(_vm);
            _aiRegistered = true;
        }
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

    private void OnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void OnSave_Click(object sender, RoutedEventArgs e)
    {
        var id = await _vm.SaveAsync();
        if (id.HasValue)
        {
            SavedPursuitId = id;
            DialogResult = true;
            Close();
        }
        else
        {
            MessageBox.Show(this, "Could not save pursuit. Check log.", "Save failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

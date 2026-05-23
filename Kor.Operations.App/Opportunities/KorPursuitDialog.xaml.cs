#nullable enable
using System.Windows;

namespace Kor.Operations.App.Opportunities;

public partial class KorPursuitDialog : Window
{
    private readonly KorPursuitDialogViewModel _vm;

    public KorPursuitDialog(KorPursuitDialogViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    public KorPursuitDialogViewModel ViewModel => _vm;

    public long? SavedPursuitId { get; private set; }

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

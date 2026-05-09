#nullable enable
using System.Windows;

namespace Kor.Operations.App.Views;

internal partial class CollectionsCaseWindow : Window
{
    private readonly CollectionsCaseViewModel _vm;

    public CollectionsCaseWindow(CollectionsCaseViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = _vm;
        _vm.RequestClose += OnRequestClose;
        Loaded += CollectionsCaseWindow_Loaded;
    }

    internal void Initialize(string clientId, string? clientName, long? caseId) => _vm.Initialize(clientId, clientName, caseId);

    private async void CollectionsCaseWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await _vm.LoadAsync().ConfigureAwait(true);
    }

    private void OnRequestClose(object? sender, bool success)
    {
        DialogResult = success;
        Close();
    }
}

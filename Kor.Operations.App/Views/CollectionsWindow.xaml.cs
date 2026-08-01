#nullable enable
using System.Windows;
using System.Windows.Input;
using Kor.Operations.Services;

namespace Kor.Operations.App.Views;

internal partial class CollectionsWindow : Window
{
    private readonly CollectionsViewModel _vm;

    public CollectionsWindow(CollectionsViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = _vm;
        Loaded += CollectionsWindow_Loaded;
    }

    private void CollectionsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _vm.RefreshCommand.Execute(null);
    }

    private void NewCaseBtn_Click(object sender, RoutedEventArgs e)
    {
        var pick = _vm.SelectedClientCandidate;
        if (pick is null)
        {
            MessageBox.Show(this, "Search and pick a client first.", "New Collections Case", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        OpenCaseWindow(pick.ClientId, pick.Name, null);
    }

    private void CasesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CasesGrid.SelectedItem is not CollectionsCaseDto row)
        {
            return;
        }

        // Name not on the list-row payload — case window resolves via Deltek lookup at load time.
        OpenCaseWindow(row.clientID, null, row.id);
    }

    private void OpenCaseWindow(string clientId, string? clientName, long? caseId)
    {
        var win = AppServices.Get<CollectionsCaseWindow>();
        win.Initialize(clientId, clientName, caseId);
        win.Owner = this;
        var result = win.ShowDialog();
        if (result == true)
        {
            _vm.RefreshCommand.Execute(null);
            _vm.ClearClientSearch();
        }
    }
}

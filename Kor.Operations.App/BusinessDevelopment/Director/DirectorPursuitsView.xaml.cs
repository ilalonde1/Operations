#nullable enable
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Kor.Operations.App.BusinessDevelopment.Director;

/// <summary>BD Director v3, Pursuits: the tender board, and a side panel for one pursuit with its owner.</summary>
public partial class DirectorPursuitsView : UserControl
{
    public DirectorPursuitsView() => InitializeComponent();

    private BdDirectorHomeViewModel? Vm => DataContext as BdDirectorHomeViewModel;
    private IDirectorHomeHost? Host => Window.GetWindow(this) as IDirectorHomeHost;

    private void Card_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DirectorCard card } && Vm is { } vm) vm.Selected = card;
    }

    private void Close_Click(object sender, RoutedEventArgs e) { if (Vm is { } vm) vm.Selected = null; }

    private void Everyone_Checked(object sender, RoutedEventArgs e) { if (Vm is { } vm) vm.MineOnly = false; }

    private void Mine_Checked(object sender, RoutedEventArgs e) { if (Vm is { } vm) vm.MineOnly = true; }

    private void AllPursuits_Click(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as BdDirectorWindow)?.ShowLibrary(byPerson: true);

    private async void Review_Click(object sender, RoutedEventArgs e)
    {
        if (Vm?.Selected is { } card && Host is { } host) await host.OpenReviewAsync(card).ConfigureAwait(true);
    }

    private async void Pursuit_Click(object sender, RoutedEventArgs e)
    {
        if (Vm?.Selected is { } card && Host is { } host) await host.OpenPursuitAsync(card).ConfigureAwait(true);
    }

    private async void Reassign_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is { Selected: { } card, ReassignTo: { } to } && Host is { } host) await host.ReassignAsync(card, to).ConfigureAwait(true);
    }
}

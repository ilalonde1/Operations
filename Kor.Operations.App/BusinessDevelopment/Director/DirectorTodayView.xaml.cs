#nullable enable
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Kor.Operations.App.BusinessDevelopment.Director;

/// <summary>What the Director's screens ask the window to do; the window owns the stores, the actor and the dialogs.</summary>
public interface IDirectorHomeHost
{
    Task CardPrimaryAsync(DirectorCard card);
    Task OpenReviewAsync(DirectorCard card);
    Task CompleteAskAsync(DirectorListItem item);
    void BookTime(DirectorTeamCard person);
    void OpenHour();
    void OpenPersonList(DirectorTeamCard person);
    Task ReassignAsync(DirectorCard card, DirectorPerson to);
    Task OpenPursuitAsync(DirectorCard card);
}

/// <summary>BD Director v3, Today: what needs Ian's call, what is coming up, his list, today's moves.</summary>
public partial class DirectorTodayView : UserControl
{
    public DirectorTodayView() => InitializeComponent();

    private IDirectorHomeHost? Host => Window.GetWindow(this) as IDirectorHomeHost;

    private async void Primary_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DirectorCard card } && Host is { } host) await host.CardPrimaryAsync(card).ConfigureAwait(true);
    }

    private async void Review_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DirectorCard card } && Host is { } host) await host.OpenReviewAsync(card).ConfigureAwait(true);
    }

    private async void Done_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DirectorListItem item } && Host is { } host) await host.CompleteAskAsync(item).ConfigureAwait(true);
    }

    private void Hour_Click(object sender, RoutedEventArgs e) => Host?.OpenHour();

    private void Nudge_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is BdDirectorHomeViewModel { Nudge: { } person }) Host?.BookTime(person);
    }
}

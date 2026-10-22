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
    /// <summary>Opens a pursuit to write its next step (BD Workspace Today).</summary>
    void OpenFollowUp(DirectorFollowUp pursuit);
    /// <summary>The owner accepts a tender that joined their forward pursuit.</summary>
    Task AcceptJoinAsync(DirectorJoin join);
    /// <summary>The owner says a joined tender is not this project: it becomes the grabber's own pursuit.</summary>
    Task RejectJoinAsync(DirectorJoin join);
    /// <summary>The manager keeps a new grab with its grabber (BD Director only; the Workspace shows no grabs).</summary>
    Task KeepGrabAsync(DirectorGrab grab) => Task.CompletedTask;
    /// <summary>The manager gives a new grab to someone else: the Pursuits side panel, open on it.</summary>
    void GiveGrab(DirectorGrab grab) { }
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

    private async void AcceptJoin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DirectorJoin join } && Host is { } host) await host.AcceptJoinAsync(join).ConfigureAwait(true);
    }

    private async void RejectJoin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DirectorJoin join } && Host is { } host) await host.RejectJoinAsync(join).ConfigureAwait(true);
    }

    private async void Keep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DirectorGrab grab } && Host is { } host) await host.KeepGrabAsync(grab).ConfigureAwait(true);
    }

    private void Give_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DirectorGrab grab }) Host?.GiveGrab(grab);
    }

    private void FollowUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DirectorFollowUp pursuit }) Host?.OpenFollowUp(pursuit);
    }

    private void Nudge_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is BdDirectorHomeViewModel { Nudge: { } person }) Host?.BookTime(person);
    }
}

#nullable enable
using System.Windows;
using System.Windows.Controls;

namespace Kor.Operations.App.BusinessDevelopment.Director;

/// <summary>BD Director v3, Team: what each person carries and where a hand would help.</summary>
public partial class DirectorTeamView : UserControl
{
    public DirectorTeamView() => InitializeComponent();

    private IDirectorHomeHost? Host => Window.GetWindow(this) as IDirectorHomeHost;

    private void Book_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DirectorTeamCard person }) Host?.BookTime(person);
    }

    private void List_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DirectorTeamCard person }) Host?.OpenPersonList(person);
    }
}

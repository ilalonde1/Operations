#nullable enable
using System.Windows;

namespace Kor.Operations.App.Opportunities;

public partial class CompetitionInfoSourcesWindow : Window
{
    public CompetitionInfoSourcesWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

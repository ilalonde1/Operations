#nullable enable
using System.Windows;

namespace Kor.Operations.App.Opportunities;

public partial class CompetitionInfoWindow : Window
{
    public CompetitionInfoWindow(CompetitionInfoViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += async (_, _) => await vm.InitializeAsync().ConfigureAwait(true);
    }
}

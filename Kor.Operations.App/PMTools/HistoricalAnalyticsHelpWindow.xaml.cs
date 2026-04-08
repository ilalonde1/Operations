#nullable enable
using System.Windows;

namespace Kor.Operations.PMTools
{
    public partial class HistoricalAnalyticsHelpWindow : Window
    {
        public HistoricalAnalyticsHelpWindow()
        {
            InitializeComponent();
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
    }
}

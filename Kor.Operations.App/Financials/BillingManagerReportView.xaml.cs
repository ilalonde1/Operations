#nullable enable
using System.Windows;
using System.Windows.Controls;

namespace Kor.Operations.Financials
{
    public partial class BillingManagerReportView : UserControl
    {
        private BillingManagerReportViewModel? _vm;

        public BillingManagerReportView()
        {
            InitializeComponent();
            DataContextChanged += (_, e) => _vm = e.NewValue as BillingManagerReportViewModel;
        }

        private void ToggleBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is BillingManagerFlatRow row)
                _vm?.ToggleManager(row);
        }
    }
}

#nullable enable
using System.Windows;
using System.Windows.Controls;

namespace Kor.Operations.Financials
{
    public partial class PartnerFinancialsView : UserControl
    {
        private PartnerFinancialsViewModel? _vm;

        public PartnerFinancialsView()
        {
            InitializeComponent();
            DataContextChanged += (_, e) => _vm = e.NewValue as PartnerFinancialsViewModel;
        }

        private void ToggleBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is PartnerFinancialsFlatRow row)
                _vm?.TogglePartner(row);
        }
    }
}

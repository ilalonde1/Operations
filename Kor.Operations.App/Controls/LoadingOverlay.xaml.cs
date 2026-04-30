#nullable enable
using System.Windows;
using System.Windows.Controls;

namespace Kor.Operations.Controls
{
    public partial class LoadingOverlay : UserControl
    {
        public LoadingOverlay()
        {
            InitializeComponent();
        }

        public void Show(string message = "Loading...")
        {
            MessageText.Text = message;
            Visibility = Visibility.Visible;
        }

        public void Hide()
        {
            Visibility = Visibility.Collapsed;
        }
    }
}

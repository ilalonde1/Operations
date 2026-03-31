#nullable enable
using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Kor.Operations.EngineeringTools.PdfToSafe;

namespace Kor.Operations.EngineeringTools
{
    public partial class EngineeringToolsWindow : Window
    {
        private readonly IServiceProvider _services;

        public EngineeringToolsWindow(IServiceProvider services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            InitializeComponent();
        }

        private void OpenPdfToSafe_Click(object sender, RoutedEventArgs e)
        {
            var win = _services.GetRequiredService<PdfToSafeWindow>();
            win.Owner = this;
            win.Show();
        }
    }
}

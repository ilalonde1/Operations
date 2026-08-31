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

        private void OpenStructuralTakeoff_Click(object sender, RoutedEventArgs e)
        {
            var win = _services.GetRequiredService<StructuralTakeoff.StructuralQuantityTakeoffWindow>();
            win.Owner = this;
            win.Show();
        }

        private void OpenDxfToEtabs_Click(object sender, RoutedEventArgs e)
        {
            var win = _services.GetRequiredService<DxfToEtabs.DxfToEtabsWindow>();
            win.Owner = this;
            win.Show();
        }

        private void OpenArchitectureMap_Click(object sender, RoutedEventArgs e)
        {
            var win = _services.GetRequiredService<ArchitectureMap.ArchitectureMapWindow>();
            win.Owner = this;
            win.Show();
        }
    }
}

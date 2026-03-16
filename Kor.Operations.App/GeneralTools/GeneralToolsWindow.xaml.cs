#nullable enable
using System;
using System.Windows;
using Kor.Operations.Services;

namespace Kor.Operations.GeneralTools
{
    public partial class GeneralToolsWindow : Window
    {
        private readonly Func<BrochureBuilderWindow> _brochureBuilderWindowFactory;

        public GeneralToolsWindow(Func<BrochureBuilderWindow> brochureBuilderWindowFactory)
        {
            _brochureBuilderWindowFactory = brochureBuilderWindowFactory ?? throw new ArgumentNullException(nameof(brochureBuilderWindowFactory));
            InitializeComponent();
            Loaded += GeneralToolsWindow_Loaded;
        }

        private async void GeneralToolsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await HeaderLoader.ApplyAsync(HeaderBar);
        }

        private void OpenBrochureBuilder_Click(object sender, RoutedEventArgs e)
        {
            var win = _brochureBuilderWindowFactory();
            win.Owner = this;
            win.Show();
        }
    }
}

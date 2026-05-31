#nullable enable
using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Kor.Operations.Services;

namespace Kor.Operations.PMTools
{
    /// <summary>
    /// Round 38a — small chooser that sits between the Home "PM Tools" tile and
    /// the two new windows (Workload Meeting / PM Capacity &amp; Risk). Until
    /// 38b + 38c land their respective windows, both cards route to the legacy
    /// <see cref="PmToolsWindow"/>; the chooser is wired now so the Home card
    /// behavior is stable through the rest of the split.
    /// </summary>
    public partial class PmToolsChooserWindow : Window
    {
        private readonly IServiceProvider _services;

        public PmToolsChooserWindow(IServiceProvider services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            InitializeComponent();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try { await HeaderLoader.ApplyAsync(HeaderBar); }
            catch (Exception ex)
            {
                // Best-effort header decoration. Chooser is otherwise usable.
                Serilog.Log.Debug(ex, "PmToolsChooserWindow header decoration failed.");
            }
        }

        private void OpenWorkloadMeeting_Click(object sender, RoutedEventArgs e)
        {
            // Round 38b: routes to the dedicated WorkloadMeetingWindow.
            var win = _services.GetRequiredService<WorkloadMeetingWindow>();
            win.Owner = Owner ?? Application.Current?.MainWindow;
            win.Show();
            Close();
        }

        private void OpenPmCapacity_Click(object sender, RoutedEventArgs e)
        {
            // Round 38c: routes to the dedicated PmCapacityWindow.
            var win = _services.GetRequiredService<PmCapacityWindow>();
            win.Owner = Owner ?? Application.Current?.MainWindow;
            win.Show();
            Close();
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
    }
}

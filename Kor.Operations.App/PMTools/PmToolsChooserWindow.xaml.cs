#nullable enable
using System;
using System.Linq;
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
            OpenOrActivate<WorkloadMeetingWindow>();
        }

        private void OpenPmCapacity_Click(object sender, RoutedEventArgs e)
        {
            OpenOrActivate<PmCapacityWindow>();
        }

        /// <summary>
        /// Round 39a (T2.004): enforce single-instance for the PM Tools child
        /// windows. Two capacity windows used to break the AI context registry
        /// (Register dedupes by ProviderName but Unregister removes by
        /// reference — closing either window dropped the singleton provider
        /// for both). Also avoids duplicate event subscriptions on the
        /// singleton meeting VM. We scan <c>Application.Current.Windows</c>
        /// because the chooser does not own the spawned windows' lifecycle.
        /// </summary>
        private void OpenOrActivate<T>() where T : Window
        {
            var existing = Application.Current?.Windows.OfType<T>().FirstOrDefault();
            if (existing is not null)
            {
                if (existing.WindowState == WindowState.Minimized)
                    existing.WindowState = WindowState.Normal;
                existing.Activate();
                Close();
                return;
            }

            var win = _services.GetRequiredService<T>();
            win.Owner = Owner ?? Application.Current?.MainWindow;
            win.Show();
            Close();
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
    }
}

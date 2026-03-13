#nullable enable
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Kor.Operations.App.Services;
using Kor.Operations.Services; // HeaderLoader
using Kor.Operations.StandardDetails;

namespace Kor.Operations
{
    public partial class HomeWindow : Window
    {
        private readonly IServiceProvider _services;

        public HomeWindow(IServiceProvider services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            InitializeComponent();
            ApplyCardSecurity();

            // Existing behavior: load header (avatar, name, email)
            Loaded += HomeWindow_Loaded_Header;

            // NEW: if launched with --email-search, jump straight to EmailSearchWindow
            Loaded += HomeWindow_Loaded_MaybeLaunchEmailSearch;
        }

        // ---------- Loaded handlers ----------

        private async void HomeWindow_Loaded_Header(object? sender, RoutedEventArgs e)
        {
            try
            {
                await HeaderLoader.ApplyAsync(HeaderBar);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HeaderLoader failed: {ex.GetType().Name}: {ex.Message}");
            }

            ApplyCardSecurity();
        }

        private void HomeWindow_Loaded_MaybeLaunchEmailSearch(object? sender, RoutedEventArgs e)
        {
            try
            {
                var args = Environment.GetCommandLineArgs();

                // Outlook button launches:
                //   Kor.Operations.App.exe --email-search
                if (args != null &&
                    args.Any(a => string.Equals(a, "--email-search", StringComparison.OrdinalIgnoreCase)))
                {
                    var win = _services.GetRequiredService<EmailSearchWindow>();
                    win.Owner = this;
                    win.Show();

                    // Close Home so the user only sees the email search window.
                    // (ShutdownMode is OnLastWindowClose, so the app stays alive.)
                    Close();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Auto email search launch failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // ---------- Card click handlers (unchanged behavior) ----------

        private void OpenEmailSearch_Click(object sender, RoutedEventArgs e)
        {
            var win = _services.GetRequiredService<EmailSearchWindow>();
            win.Owner = this;
            win.Show();
        }

        private void OpenTransmittalSearch_Click(object sender, RoutedEventArgs e)
        {
            var win = _services.GetRequiredService<DashboardWindow>();
            win.Owner = this;
            win.Show();
        }

        private void CreateTransmittal_Click(object sender, RoutedEventArgs e)
        {
            var win = _services.GetRequiredService<MainWindow>();
            win.Owner = this;
            win.Show();
        }

        private async void OpenPreferences_Click(object sender, RoutedEventArgs e)
        {
            var win = _services.GetRequiredService<PreferencesWindow>();
            win.Owner = this;
            win.ShowDialog();
            await Task.CompletedTask; // preserve async signature
        }

        private void OpenFinancials_Click(object sender, RoutedEventArgs e)
        {
            var win = new Financials.FinancialsWindow { Owner = this };
            win.Show();
        }

        private void OpenStandardDetails_Click(object sender, RoutedEventArgs e)
        {
            var win = new StandardDetailsWindow { Owner = this };
            win.Show();
        }

        private void ApplyCardSecurity()
        {
            try
            {
                var overrideUpn = ConfigurationManager.AppSettings[Kor.Operations.Services.AppConfigKeys.UserUpnOverride];
                var fallbackUpn = !string.IsNullOrWhiteSpace(overrideUpn)
                    ? overrideUpn.Trim()
                    : $"{NormalizeUserPart(Environment.UserName)}@korstructural.com";

                var userIdentity = !string.IsNullOrWhiteSpace(HeaderBar?.UserEmail)
                    ? HeaderBar.UserEmail
                    : fallbackUpn;

                var canSeeFinancials = SecurityGroupAccess.IsUserInGroup(KnownRoles.Financials, userIdentity);
                FinancialsTileHost.Visibility = canSeeFinancials ? Visibility.Visible : Visibility.Collapsed;

                var canSeeStandardDetails = SecurityGroupAccess.IsUserInGroup(KnownRoles.StandardDetails, userIdentity);
                StandardDetailsTileHost.Visibility = canSeeStandardDetails ? Visibility.Visible : Visibility.Collapsed;

                RebuildHomeCardsLayout();
            }
            catch
            {
                FinancialsTileHost.Visibility = Visibility.Visible;
                StandardDetailsTileHost.Visibility = Visibility.Visible;
                RebuildHomeCardsLayout();
            }
        }

        private void RebuildHomeCardsLayout()
        {
            if (HomeCardsGrid == null)
                return;

            // Keep a stable order with Preferences always last.
            var orderedCards = new List<UIElement>
            {
                SearchEmailsCard,
                SearchTransmittalsCard,
                CreateTransmittalCard,
                FinancialsTileHost,
                StandardDetailsTileHost,
                PreferencesCard
            };

            var visibleCards = orderedCards
                .Where(e => e != null && e.Visibility != Visibility.Collapsed)
                .ToList();

            HomeCardsGrid.Children.Clear();
            foreach (var card in visibleCards)
            {
                HomeCardsGrid.Children.Add(card);
            }

            int count = visibleCards.Count;
            int columns = count switch
            {
                4 => 4,
                5 => 3,
                6 => 3,
                _ => Math.Max(1, Math.Min(3, count))
            };

            int rows = count == 0 ? 1 : (int)Math.Ceiling((double)count / columns);

            HomeCardsGrid.Columns = columns;
            Width = columns >= 4 ? 1240 : 980;
        }

        private static string NormalizeUserPart(string user)
        {
            if (string.IsNullOrWhiteSpace(user)) return "";
            var idx = user.IndexOf('\\');
            return idx >= 0 && idx < user.Length - 1 ? user[(idx + 1)..] : user;
        }
    }
}


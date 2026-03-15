#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Kor.Operations.App.Options;
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

        private void OpenPMTools_Click(object sender, RoutedEventArgs e)
        {
            var win = new Financials.FinancialsWindow();
            ConfigurePmToolsClone(win);
            win.Owner = this;
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
                var overrideUpn = ((global::Kor.Operations.OperationsApp)Application.Current).Services.GetRequiredService<UserOptions>().UserUpnOverride;
                var fallbackUpn = !string.IsNullOrWhiteSpace(overrideUpn)
                    ? overrideUpn.Trim()
                    : $"{NormalizeUserPart(Environment.UserName)}@korstructural.com";

                var userIdentity = !string.IsNullOrWhiteSpace(HeaderBar?.UserEmail)
                    ? HeaderBar.UserEmail
                    : fallbackUpn;

                var canSeeFinancials = SecurityGroupAccess.IsUserInGroup(KnownRoles.Financials, userIdentity);
                FinancialsTileHost.Visibility = canSeeFinancials ? Visibility.Visible : Visibility.Collapsed;

                var canSeePmTools = SecurityGroupAccess.IsUserInGroup(KnownRoles.PMTools, userIdentity);
                PmToolsTileHost.Visibility = canSeePmTools ? Visibility.Visible : Visibility.Collapsed;

                var canSeeStandardDetails = SecurityGroupAccess.IsUserInGroup(KnownRoles.StandardDetails, userIdentity);
                StandardDetailsTileHost.Visibility = canSeeStandardDetails ? Visibility.Visible : Visibility.Collapsed;

                RebuildHomeCardsLayout();
            }
            catch
            {
                FinancialsTileHost.Visibility = Visibility.Visible;
                PmToolsTileHost.Visibility = Visibility.Visible;
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
                PmToolsTileHost,
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

        private static void ConfigurePmToolsClone(Window win)
        {
            win.Title = "KOR NewerForma - PM Tools";
            win.Loaded += (_, __) =>
            {
                HideSectionSwitcher(win);

                var sectionIndex = win.DataContext?.GetType().GetProperty("SectionIndex");
                if (sectionIndex != null && sectionIndex.CanWrite)
                    sectionIndex.SetValue(win.DataContext, 0);
            };

            var headerField = win.GetType().GetField("HeaderBar", BindingFlags.Instance | BindingFlags.NonPublic);
            var header = headerField?.GetValue(win);
            if (header == null)
                return;

            var headerType = header.GetType();
            headerType.GetProperty("HeaderText")?.SetValue(header, "PM Tools");
            headerType.GetProperty("SubtitleText")?.SetValue(header, "Project Manager Toolsets");
        }

        private static void HideButtonByContent(DependencyObject root, string contentText)
        {
            var button = FindVisualChildren<Button>(root)
                .FirstOrDefault(b => string.Equals(Convert.ToString(b.Content), contentText, StringComparison.OrdinalIgnoreCase));

            if (button != null)
                button.Visibility = Visibility.Collapsed;
        }

        private static void HideSectionSwitcher(DependencyObject root)
        {
            if (root is Window window && window.FindName("SectionSwitcherCard") is FrameworkElement namedSectionSwitcher)
            {
                namedSectionSwitcher.Visibility = Visibility.Collapsed;
                return;
            }

            var overviewButton = FindVisualChildren<Button>(root)
                .FirstOrDefault(b => string.Equals(Convert.ToString(b.Content), "Overview", StringComparison.OrdinalIgnoreCase));

            if (overviewButton == null)
                return;

            var current = overviewButton as DependencyObject;
            while (current != null && current is not Border)
                current = VisualTreeHelper.GetParent(current);

            if (current is Border border)
                border.Visibility = Visibility.Collapsed;
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null)
                yield break;

            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match)
                    yield return match;

                foreach (var nested in FindVisualChildren<T>(child))
                    yield return nested;
            }
        }
    }
}


#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Kor.Operations.Core;
using Kor.Operations.App.Options;
using Kor.Operations.App.Services;
using Kor.Operations.Services; // HeaderLoader
using Kor.Operations.StandardDetails;
using Kor.Operations.Brochures;
using Kor.Operations.Compensation;

namespace Kor.Operations
{
    public partial class HomeWindow : Window
    {
        private readonly IServiceProvider _services;
        private readonly Func<BrochureBuilderWindow> _brochureBuilderWindowFactory;
        private PMTools.PmToolsWindow? _pmToolsWindow;

        public HomeWindow(IServiceProvider services, Func<BrochureBuilderWindow> brochureBuilderWindowFactory)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _brochureBuilderWindowFactory = brochureBuilderWindowFactory ?? throw new ArgumentNullException(nameof(brochureBuilderWindowFactory));
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
                    args.Any(a => string.Equals(a, CliArgs.EmailSearch, StringComparison.OrdinalIgnoreCase)))
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
            var authorizationService = _services.GetRequiredService<IAuthorizationService>();
            if (!authorizationService.IsAuthorized("Preferences"))
            {
                MessageBox.Show("You are not authorized to access Preferences.",
                    "Application — Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var win = _services.GetRequiredService<PreferencesWindow>();
            win.Owner = this;
            win.ShowDialog();
            await Task.CompletedTask; // preserve async signature
        }

        private void OpenFinancials_Click(object sender, RoutedEventArgs e)
        {
            var win = _services.GetRequiredService<Financials.FinancialsWindow>();
            win.Owner = this;
            win.Show();
        }

        private void OpenCompensation_Click(object sender, RoutedEventArgs e)
        {
            var win = _services.GetRequiredService<CompensationWindow>();
            win.Owner = this;
            win.Show();
        }

        private void OpenPMTools_Click(object sender, RoutedEventArgs e)
        {
            if (_pmToolsWindow is { IsLoaded: true })
            {
                _pmToolsWindow.Activate();
                return;
            }
            _pmToolsWindow = _services.GetRequiredService<PMTools.PmToolsWindow>();
            _pmToolsWindow.Owner = this;
            _pmToolsWindow.Show();
        }

        private void OpenStandardDetails_Click(object sender, RoutedEventArgs e)
        {
            var win = new StandardDetailsWindow { Owner = this };
            win.Show();
        }

        private void OpenGeneralTools_Click(object sender, RoutedEventArgs e)
        {
            var win = _brochureBuilderWindowFactory();
            win.Owner = this;
            win.Show();
        }

        private void OpenFeeProposal_Click(object sender, RoutedEventArgs e)
        {
            var win = Kor.Operations.Services.AppServices.Get<App.FeeProposal.FeeProposalBuilderWindow>();
            win.Owner = this;
            win.Show();
        }

        private void OpenEngineeringTools_Click(object sender, RoutedEventArgs e)
        {
            var win = _services.GetRequiredService<EngineeringTools.EngineeringToolsWindow>();
            win.Owner = this;
            win.Show();
        }

        private void OpenFileSyncCommandCenter_Click(object sender, RoutedEventArgs e)
        {
            var win = _services.GetRequiredService<App.FileSync.FileSyncCommandCenterWindow>();
            win.Owner = this;
            win.Show();
        }

        private void OpenOpportunities_Click(object sender, RoutedEventArgs e)
        {
            var win = _services.GetRequiredService<App.Opportunities.OpportunitiesWindow>();
            win.Owner = this;
            win.Show();
        }

        private void OpenBusinessDevelopment_Click(object sender, RoutedEventArgs e)
        {
            var win = _services.GetRequiredService<App.BusinessDevelopment.BusinessDevelopmentWindow>();
            win.Owner = this;
            win.Show();
        }

        private void ApplyCardSecurity()
        {
            try
            {
                var overrideUpn = Kor.Operations.Services.AppServices.Get<UserOptions>().UserUpnOverride;
                var fallbackUpn = !string.IsNullOrWhiteSpace(overrideUpn)
                    ? overrideUpn.Trim()
                    : $"{NormalizeUserPart(Environment.UserName)}@korstructural.com";

                var userIdentity = !string.IsNullOrWhiteSpace(HeaderBar?.UserEmail)
                    ? HeaderBar.UserEmail
                    : fallbackUpn;

                // Direct SecurityGroupAccess calls intentionally left here  pre-dates
                // IAuthorizationService centralization. Consolidate in a future refactor.
                var canSeeFinancials = SecurityGroupAccess.IsUserInGroup(KnownRoles.Financials, userIdentity);
                FinancialsTileHost.Visibility = canSeeFinancials ? Visibility.Visible : Visibility.Collapsed;

                var canSeeCompensation = SecurityGroupAccess.IsUserInGroup(KnownRoles.Compensation, userIdentity);
                CompensationTileHost.Visibility = canSeeCompensation ? Visibility.Visible : Visibility.Collapsed;

                var canSeePmTools = SecurityGroupAccess.IsUserInGroup(KnownRoles.PMTools, userIdentity);
                PmToolsTileHost.Visibility = canSeePmTools ? Visibility.Visible : Visibility.Collapsed;

                var canSeeStandardDetails = SecurityGroupAccess.IsUserInGroup(KnownRoles.StandardDetails, userIdentity);
                StandardDetailsTileHost.Visibility = canSeeStandardDetails ? Visibility.Visible : Visibility.Collapsed;

                var canSeeBrochureBuilder = SecurityGroupAccess.IsUserInGroup(KnownRoles.BrochureBuilder, userIdentity);
                GeneralToolsCard.Visibility = canSeeBrochureBuilder ? Visibility.Visible : Visibility.Collapsed;

                var canSeeFeeProposalBuilder = SecurityGroupAccess.IsUserInGroup(KnownRoles.FeeProposalBuilder, userIdentity);
                FeeProposalBuilderCard.Visibility = canSeeFeeProposalBuilder ? Visibility.Visible : Visibility.Collapsed;

                var canSeeEngineeringTools = SecurityGroupAccess.IsUserInGroup(KnownRoles.EngineeringTools, userIdentity);
                EngineeringToolsTileHost.Visibility = canSeeEngineeringTools ? Visibility.Visible : Visibility.Collapsed;

                var canSeeFileSyncCommandCenter = SecurityGroupAccess.IsUserInGroup(KnownRoles.FileSyncCommandCenter, userIdentity);
                FileSyncCommandCenterTileHost.Visibility = canSeeFileSyncCommandCenter ? Visibility.Visible : Visibility.Collapsed;

                var canSeeOpportunities = SecurityGroupAccess.IsUserInGroup(KnownRoles.Opportunities, userIdentity);
                var canSeeBd = SecurityGroupAccess.IsUserInGroup(KnownRoles.BusinessDevelopment, userIdentity);
                BusinessDevelopmentTileHost.Visibility = canSeeBd ? Visibility.Visible : Visibility.Collapsed;

                // BD bundles Opportunities + FeeProposal + Brochure. When the BD tile
                // is visible we hide the three sub-tiles from the Home grid so the
                // BD card is the single entry point — keeps Home uncluttered.
                if (canSeeBd)
                {
                    OpportunitiesTileHost.Visibility = Visibility.Collapsed;
                    GeneralToolsCard.Visibility = Visibility.Collapsed;
                    FeeProposalBuilderCard.Visibility = Visibility.Collapsed;
                }
                else
                {
                    OpportunitiesTileHost.Visibility = canSeeOpportunities ? Visibility.Visible : Visibility.Collapsed;
                }

                RebuildHomeCardsLayout();
            }
            catch
            {
                FinancialsTileHost.Visibility = Visibility.Visible;
                CompensationTileHost.Visibility = Visibility.Visible;
                PmToolsTileHost.Visibility = Visibility.Visible;
                StandardDetailsTileHost.Visibility = Visibility.Visible;
                GeneralToolsCard.Visibility = Visibility.Visible;
                FeeProposalBuilderCard.Visibility = Visibility.Visible;
                EngineeringToolsTileHost.Visibility = Visibility.Visible;
                FileSyncCommandCenterTileHost.Visibility = Visibility.Collapsed;
                OpportunitiesTileHost.Visibility = Visibility.Collapsed;
                BusinessDevelopmentTileHost.Visibility = Visibility.Collapsed;
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
                CompensationTileHost,
                PmToolsTileHost,
                StandardDetailsTileHost,
                BusinessDevelopmentTileHost,
                GeneralToolsCard,
                FeeProposalBuilderCard,
                OpportunitiesTileHost,
                EngineeringToolsTileHost,
                FileSyncCommandCenterTileHost,
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


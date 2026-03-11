using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Kor.Operations;
using Kor.Operations.Services;
namespace Kor.Operations.Controls
{
    public partial class KorHeader : UserControl
    {
        // total vertical padding around avatar used to compute Height
        private const double VerticalPadding = 8;
        private bool _profileApplied;

        public KorHeader()
        {
            InitializeComponent();

            if (string.IsNullOrWhiteSpace(UserDisplayName))
                UserDisplayName = Environment.UserName;
            if (string.IsNullOrWhiteSpace(UserEmail))
                UserEmail = $"{Environment.UserName}@korstructural.com";

            CoerceHeightForAvatar();
            UpdateInitials();

            Loaded += KorHeader_Loaded;
        }

        // DPs
        public static readonly DependencyProperty HeaderTextProperty =
            DependencyProperty.Register(nameof(HeaderText), typeof(string), typeof(KorHeader), new PropertyMetadata(""));

        public static readonly DependencyProperty SubtitleTextProperty =
            DependencyProperty.Register(nameof(SubtitleText), typeof(string), typeof(KorHeader), new PropertyMetadata(""));

        public static readonly DependencyProperty RightContentProperty =
            DependencyProperty.Register(nameof(RightContent), typeof(object), typeof(KorHeader), new PropertyMetadata(null));

        public static readonly DependencyProperty UserDisplayNameProperty =
            DependencyProperty.Register(nameof(UserDisplayName), typeof(string), typeof(KorHeader),
                new PropertyMetadata("", (d, _) => ((KorHeader)d).UpdateInitials()));

        public static readonly DependencyProperty UserEmailProperty =
            DependencyProperty.Register(nameof(UserEmail), typeof(string), typeof(KorHeader),
                new PropertyMetadata("", (d, _) => ((KorHeader)d).UpdateInitials()));

        public static readonly DependencyProperty AvatarImageSourceProperty =
            DependencyProperty.Register(nameof(AvatarImageSource), typeof(ImageSource), typeof(KorHeader), new PropertyMetadata(null, OnAvatarChanged));

        public static readonly DependencyProperty AvatarDiameterProperty =
            DependencyProperty.Register(nameof(AvatarDiameter), typeof(double), typeof(KorHeader), new PropertyMetadata(96.0, OnAvatarSizeChanged));

        public static readonly DependencyProperty AvatarBorderThicknessProperty =
            DependencyProperty.Register(nameof(AvatarBorderThickness), typeof(double), typeof(KorHeader), new PropertyMetadata(2.0));

        public static readonly DependencyProperty ShowDeltekBannerProperty =
            DependencyProperty.Register(nameof(ShowDeltekBanner), typeof(bool), typeof(KorHeader), new PropertyMetadata(false));

        public static readonly DependencyProperty DeltekBannerTextProperty =
            DependencyProperty.Register(nameof(DeltekBannerText), typeof(string), typeof(KorHeader), new PropertyMetadata(""));

        // wrappers
        public string HeaderText { get => (string)GetValue(HeaderTextProperty); set => SetValue(HeaderTextProperty, value); }
        public string SubtitleText { get => (string)GetValue(SubtitleTextProperty); set => SetValue(SubtitleTextProperty, value); }
        public object RightContent { get => GetValue(RightContentProperty); set => SetValue(RightContentProperty, value); }
        public string UserDisplayName { get => (string)GetValue(UserDisplayNameProperty); set => SetValue(UserDisplayNameProperty, value); }
        public string UserEmail { get => (string)GetValue(UserEmailProperty); set => SetValue(UserEmailProperty, value); }
        public ImageSource AvatarImageSource { get => (ImageSource)GetValue(AvatarImageSourceProperty); set => SetValue(AvatarImageSourceProperty, value); }
        public double AvatarDiameter { get => (double)GetValue(AvatarDiameterProperty); set => SetValue(AvatarDiameterProperty, value); }
        public double AvatarBorderThickness { get => (double)GetValue(AvatarBorderThicknessProperty); set => SetValue(AvatarBorderThicknessProperty, value); }
        public bool ShowDeltekBanner { get => (bool)GetValue(ShowDeltekBannerProperty); set => SetValue(ShowDeltekBannerProperty, value); }
        public string DeltekBannerText { get => (string)GetValue(DeltekBannerTextProperty); set => SetValue(DeltekBannerTextProperty, value); }

        // Initials DP
        public static readonly DependencyProperty InitialsDependencyProperty =
            DependencyProperty.Register(nameof(Initials), typeof(string), typeof(KorHeader), new PropertyMetadata(""));

        public string Initials
        {
            get => (string)GetValue(InitialsDependencyProperty);
            private set => SetValue(InitialsDependencyProperty, value);
        }

        private static void OnAvatarSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((KorHeader)d).CoerceHeightForAvatar();

        private static void OnAvatarChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // Nothing special needed; triggers in XAML switch layers. Keep method in case you want side-effects later.
        }

        private void CoerceHeightForAvatar()
        {
            var needed = Math.Max(AvatarDiameter + VerticalPadding, MinHeight);
            Height = needed;
        }

        private void UpdateInitials()
        {
            string src = (UserDisplayName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(src))
                src = (UserEmail ?? "").Split('@').FirstOrDefault() ?? "";

            string initials = "";
            if (!string.IsNullOrWhiteSpace(src))
            {
                var parts = src.Split(new[] { ' ', '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
                initials = parts.Length >= 2
                    ? $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant()
                    : src.Substring(0, Math.Min(2, src.Length)).ToUpperInvariant();
            }
            Initials = initials;
        }

        private async void KorHeader_Loaded(object sender, RoutedEventArgs e)
        {
            // Standardize user identity display across all windows that host KorHeader.
            // Best-effort and cached; avoid re-running on re-load.
            if (!_profileApplied)
            {
                _profileApplied = true;
                try { await HeaderLoader.ApplyAsync(this); }
                catch { /* never block header */ }
            }

            // Best-effort: surface Deltek outages so they don't look like regressions.
            try
            {
                var status = await DeltekHealthProbe.GetStatusAsync();

                if (!status.IsOnline)
                {
                    ShowDeltekBanner = true;
                    DeltekBannerText = status.Message ?? "Deltek is unavailable. Some data may be missing.";
                }
                else
                {
                    ShowDeltekBanner = false;
                    DeltekBannerText = string.Empty;
                }
            }
            catch
            {
                // Never block UI due to the health probe
            }
        }
    }
}

#nullable enable
using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Kor.Opportunities.Data.Portals;
using Serilog;

namespace Kor.Operations.App.Crm;

/// <summary>
/// 2026-10-21 (Ian: APC login sorted, "go"): a person signs in to Alberta Purchasing Connection once, in this window, and the
/// app keeps the session so the Worker (ApcDocumentJob) can download pursued tenders' documents.
///
/// APC's sign-in carries Cloudflare Turnstile, which refuses automated browsers; this is a plain browser window a person types
/// into, and nothing here fills, clicks or answers anything. Once the page has stored its signed-in user (oidc-client-ts keeps it
/// under "oidc.user:…" in the page's storage), the window reads the refresh token, renews it once against the sign-in server (which
/// proves it works and gives the session's lifetime) and saves the renewed token in opportunities.PortalSession.
/// </summary>
public partial class ApcSignInWindow : Window
{
    // Reads whatever oidc-client-ts stored for the signed-in user; '' while nobody is signed in.
    private const string ReadStoredUser =
        "(() => { for (const s of [sessionStorage, localStorage]) { for (let i = 0; i < s.length; i++) { const k = s.key(i); if (k && k.startsWith('oidc.user:')) return s.getItem(k) || ''; } } return ''; })()";

    private readonly SqlPortalSessionStore _sessions;
    private readonly string _signedInBy;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _saving;
    private bool _saved;

    public ApcSignInWindow(SqlPortalSessionStore sessions, string signedInBy)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _signedInBy = string.IsNullOrWhiteSpace(signedInBy) ? Environment.UserName : signedInBy;
        InitializeComponent();
        Loaded += async (_, _) => await StartAsync().ConfigureAwait(true);
        _poll.Tick += async (_, _) => await CheckAsync().ConfigureAwait(true);
    }

    private async Task StartAsync()
    {
        try
        {
            var existing = await _sessions.GetAsync(ApcPortal.PortalKey, CancellationToken.None).ConfigureAwait(true);
            if (existing is not null)
            {
                Status.Text = existing.LastError is null
                    ? $"Saved session: {existing.Account ?? "unknown account"}, signed in {existing.SignedInAtUtc.ToLocalTime():ddd d MMM h:mm tt} by {existing.SignedInBy}. Sign in again below to replace it."
                    : $"The saved session stopped working ({existing.LastError}). Sign in again below.";
            }

            // Its own profile folder: this window never shares cookies with anything else in the app.
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KOR", "ApcSignIn");
            var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, folder).ConfigureAwait(true);
            await Browser.EnsureCoreWebView2Async(env).ConfigureAwait(true);
            Browser.CoreWebView2.Navigate(ApcPortal.SiteUrl);
            if (existing is null) Status.Text = "Click Sign in on the APC page and sign in with the company account.";
            _poll.Start();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "APC sign-in window could not start");
            Status.Text = "The browser could not start: " + ex.Message;
        }
    }

    private async Task CheckAsync()
    {
        if (_saving || _saved || Browser.CoreWebView2 is null) return;
        if (!Uri.TryCreate(Browser.CoreWebView2.Source, UriKind.Absolute, out var at) || !at.Host.Equals("purchasing.alberta.ca", StringComparison.OrdinalIgnoreCase)) return;

        string stored;
        try
        {
            stored = JsonSerializer.Deserialize<string>(await Browser.CoreWebView2.ExecuteScriptAsync(ReadStoredUser).ConfigureAwait(true)) ?? "";
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "APC sign-in: could not read the page's storage yet");
            return;
        }

        if (ApcPortal.ReadBrowserSession(stored) is not { } session) return;

        _saving = true;
        Status.Text = "Signed in. Saving the session…";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0 Safari/537.36");
            var tokens = await new ApcPortal(http).RenewAsync(session.RefreshToken, CancellationToken.None).ConfigureAwait(true);
            await _sessions.SaveSignInAsync(ApcPortal.PortalKey, session.Account, tokens.RefreshToken, tokens.RefreshExpiresUtc, _signedInBy, CancellationToken.None).ConfigureAwait(true);
            _saved = true;
            _poll.Stop();
            Status.Text = $"Saved: {session.Account ?? "the APC account"}. The app now downloads the documents of pursued APC tenders, within about 10 minutes of a grab. You can close this window.";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "APC sign-in: saving the session failed");
            Status.Text = "Signed in, but saving the session failed: " + ex.Message;
        }
        finally
        {
            _saving = false;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _poll.Stop();
        Browser.Dispose();
        base.OnClosed(e);
    }
}

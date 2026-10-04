using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Diagnostics;            // ADDED
using System.IO;                     // ADDED
using System.Runtime.InteropServices;
using System.Text;                   // ADDED
using System.Windows.Forms;
using Office = Microsoft.Office.Core;
using Outlook = Microsoft.Office.Interop.Outlook;

namespace EmailFilerv2
{
    [ComVisible(true)]
    public partial class ThisAddIn
    {
        // If anything goes wrong reading prefs, default to ON (prompt on send)
        private bool _autoFileOnSend = true;

        // Items-To-File processor (background filing on Outlook close)
        private ItemsToFileProcessor _itemsToFileProcessor;

        // Files "file on send" emails from Sent Items once they have actually gone.
        private FileOnSend _fileOnSend;

        // A one-shot timer that runs the folder sync just AFTER load, so its SQL round-trip + MAPI folder work never
        // blocks Outlook's startup. Held in a field so it is not garbage-collected before it fires.
        private System.Windows.Forms.Timer _syncTimer;

        private void ThisAddIn_Startup(object sender, EventArgs e)
        {
            // Load telemetry to the shared filing log (SMB-readable): which version loaded, what app path it resolved, and
            // -- the key number -- how long Startup took. With the SQL round-trip gone and SyncFolders deferred, this is
            // small; that is the load-time fix, measurable instead of asserted.
            var loadClock = System.Diagnostics.Stopwatch.StartNew();
            string app;
            try { app = HostExeResolver.Resolve() ?? "NOT FOUND"; } catch (Exception ex) { app = "resolve error: " + ex.Message; }
            ItemsToFileProcessor.SafeLog($"ADD-IN LOADED: EmailFilerv2 {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version} -- app resolves to [{app}]");

            // _autoFileOnSend keeps its safe default (true) and is re-read from the DB on every send (Application_ItemSend),
            // so startup does NOT open a SQL connection here -- that round-trip to APP01 was part of the slow Outlook load.
            this.Application.ItemSend +=
                new Outlook.ApplicationEvents_11_ItemSendEventHandler(Application_ItemSend);

            try
            {
                _itemsToFileProcessor = new ItemsToFileProcessor(this.Application);

                // Defer the folder sync off the load path: SyncFolders reads SQL favorites/projects AND enumerates and
                // creates Outlook MAPI folders. That work must run on the UI thread but need not block Outlook opening,
                // so a one-shot timer runs it a beat after startup. Outlook loads fast; the folders appear a moment later.
                _syncTimer = new System.Windows.Forms.Timer { Interval = 2000 };
                _syncTimer.Tick += (s, ev) =>
                {
                    _syncTimer.Stop();
                    ItemsToFileProcessor.SafeLog($"deferred SyncFolders firing ({loadClock.ElapsedMilliseconds} ms after load)");
                    try { _itemsToFileProcessor.SyncFolders(); } catch { /* never block; filing still works without the synced folders */ }
                };
                _syncTimer.Start();

                try
                {
                    _fileOnSend = new FileOnSend(this.Application, _itemsToFileProcessor);
                    _fileOnSend.Start();
                }
                catch
                {
                    // Without the Sent Items hook, file-on-send falls back to filing before sending.
                    _fileOnSend = null;
                }

                // CORRECT way to subscribe to Quit
                ((Outlook.ApplicationEvents_11_Event)this.Application).Quit
                    += new Outlook.ApplicationEvents_11_QuitEventHandler(Application_Quit);
            }
            catch (Exception ex)
            {
                // never block Outlook startup, but DO record it.
                ItemsToFileProcessor.SafeLog("ThisAddIn_Startup error (Outlook load not blocked): " + ex.Message);
            }
            ItemsToFileProcessor.SafeLog($"ThisAddIn_Startup returned in {loadClock.ElapsedMilliseconds} ms (folder sync deferred -- Outlook's load was not blocked on it)");
        }

        private void Application_Quit()
        {
            try
            {
                _itemsToFileProcessor?.ProcessOnQuit();
            }
            catch
            {
                // Never block Outlook closing
            }
        }

        private void ThisAddIn_Shutdown(object sender, EventArgs e)
        {
            // no-op; we rely on Application_Quit above
        }

        protected override Office.IRibbonExtensibility CreateRibbonExtensibilityObject()
        {
            return new EmailFilerRibbon();
        }

        private void Application_ItemSend(object item, ref bool cancel)
        {
            // Re-read the flag every time in case the user changed it while Outlook is running
            _autoFileOnSend = LoadAutoFileOnSendFlag();

            // Respect user preference: if they turned off "Prompt to file emails when I send them",
            // do not run any filing logic at all.
            if (!_autoFileOnSend)
            {
                return;
            }

            // Only process real mail messages.
            Outlook.MailItem mailItem = item as Outlook.MailItem;
            if (mailItem == null)
            {
                // Not a MailItem (could be AppointmentItem, MeetingItem, etc.) � bypass our logic.
                return;
            }

            try
            {
                DialogResult result = MessageBox.Show(
                    "Do you want to file this email before sending?",
                    "File Email",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Cancel)
                {
                    cancel = true;
                    return;
                }

                if (result == DialogResult.No)
                {
                    // Just send without filing
                    return;
                }

                // result == DialogResult.Yes: choose the project now, file the Sent Items copy once it
                // has gone (FileOnSend). No project chosen -> don't send, exactly as before.
                if (_fileOnSend != null)
                {
                    string projectNo = EmailFilerRibbon.PickProject();
                    if (string.IsNullOrEmpty(projectNo))
                    {
                        cancel = true;
                        return;
                    }

                    if (_fileOnSend.Arm(mailItem, projectNo))
                        return;
                }

                // Fallback (hook unavailable or the email could not be marked): file now, as before.
                var mails = new List<Outlook.MailItem> { mailItem };
                bool filed = EmailFilerRibbon.FileMailItems(this.Application, mails);

                if (!filed)
                {
                    // User cancelled or filing failed � don't send
                    cancel = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Error while filing email:\n" + ex.Message,
                    "File Email",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                // Be conservative: if filing blows up for a MailItem, prevent sending
                cancel = true;
            }
        }

        /// <summary>
        /// Reads AutoFileOnSend from dbo.UserPreferences for the current user.
        /// Falls back to 'true' (prompt on send) if anything fails.
        /// </summary>
        private bool LoadAutoFileOnSendFlag()
        {
            try
            {
                // Same UPN logic as the WPF app
                string overrideUpn = ConfigurationManager.AppSettings["UserUpnOverride"];
                string userUpn = !string.IsNullOrWhiteSpace(overrideUpn)
                    ? overrideUpn.Trim()
                    : NormalizeUserPart(Environment.UserName) + "@korstructural.com";

                // IMPORTANT: use the existing connection string name from App.config
                var csSetting = ConfigurationManager.ConnectionStrings["KorTransmittals"];
                string cs = csSetting != null ? csSetting.ConnectionString : null;
                if (string.IsNullOrWhiteSpace(cs))
                {
                    // Config issue � safest behaviour is to keep prompts enabled
                    return true;
                }

                using (var con = new SqlConnection(cs))
                {
                    con.Open();
                    using (var cmd = con.CreateCommand())
                    {
                        cmd.CommandText =
                            "SELECT AutoFileOnSend FROM dbo.UserPreferences WHERE UserUpn = @upn";
                        cmd.Parameters.AddWithValue("@upn", userUpn);

                        object value = cmd.ExecuteScalar();

                        if (value == null || value == DBNull.Value)
                        {
                            // No row yet � default to ON
                            return true;
                        }

                        if (value is bool)
                            return (bool)value;

                        int intVal;
                        if (int.TryParse(value.ToString(), out intVal))
                            return intVal != 0;

                        return true;
                    }
                }
            }
            catch
            {
                // Any error reading prefs: do not break sending, just keep prompts ON
                return true;
            }
        }

        /// <summary>
        /// Strip domain / machine prefix from Environment.UserName if present.
        /// </summary>
        private static string NormalizeUserPart(string user)
        {
            if (string.IsNullOrWhiteSpace(user))
                return string.Empty;

            int idx = user.IndexOf('\\');
            if (idx >= 0 && idx < user.Length - 1)
            {
                // substring after the backslash
                return user.Substring(idx + 1);
            }

            return user;
        }

        internal void QuickFileSelectedEmails(string projectNo, IList<Outlook.MailItem> mails)
        {
            try
            {
                if (_itemsToFileProcessor != null)
                {
                    _itemsToFileProcessor.QuickFileSelectionToProject(projectNo, mails);
                }
            }
            catch
            {
                // Swallow here; the ribbon callback already shows any error to the user.
            }
        }

        internal static string ResolveKorExe()
        {
            return HostExeResolver.Resolve();
        }


        private static string QuoteArg(string s)
        {
            if (string.IsNullOrEmpty(s)) return "\"\"";
            // Escape inner quotes for cmdline
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>
        /// Launch Kor.Operations.App.exe with args. Throws if the resolved file does not exist.
        /// Uses ProcessStartInfo.Arguments (Framework-safe).
        /// </summary>
        internal static void LaunchKorTransmittals(IEnumerable<string> args)
        {
            var exe = ResolveKorExe();
            if (!File.Exists(exe))
                throw new FileNotFoundException("Kor.Operations.App.exe was not found at:", exe);

            // Build a single arguments string (Framework doesn't support ArgumentList)
            var sb = new StringBuilder();
            if (args != null)
            {
                foreach (var a in args)
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(QuoteArg(a));
                }
            }

            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                Arguments = sb.ToString()
            };

            Process.Start(psi);
        }

        #region VSTO generated code

        private void InternalStartup()
        {
            this.Startup += new EventHandler(ThisAddIn_Startup);
            this.Shutdown += new EventHandler(ThisAddIn_Shutdown);
        }

        #endregion
    }
}

using stdole;                         // NEW: for IPictureDisp
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Drawing;                 // NEW: for Bitmap/Image
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Office = Microsoft.Office.Core;
using Outlook = Microsoft.Office.Interop.Outlook;

namespace EmailFilerv2
{
    [ComVisible(true)]
    public class EmailFilerRibbon : Office.IRibbonExtensibility
    {
        private Office.IRibbonUI ribbon;

        // NEW: favorites wiring (same DB as ItemsToFileProcessor)
        private readonly SqlFavoritesRepository _favoritesRepo;
        private readonly string _userUpn;
        private readonly string _connectionString;

        public EmailFilerRibbon()
        {
            try
            {
                // Try to read from config first
                var csSetting = ConfigurationManager.ConnectionStrings["KorTransmittals"];
                _connectionString = csSetting != null ? csSetting.ConnectionString : null;

                // FALLBACK: if config isn't being seen, hard-code the same string
                if (string.IsNullOrWhiteSpace(_connectionString))
                {
                    _connectionString =
                        "Server=KOR-APP01\\SQLEXPRESS;Database=KorTransmittals;" +
                        "User Id=transmittals_app;Password=ChangeThisStrongPassword!2025;" +
                        "Encrypt=True;TrustServerCertificate=True;";
                }

                if (!string.IsNullOrWhiteSpace(_connectionString))
                {
                    _favoritesRepo = new SqlFavoritesRepository(_connectionString);
                }

                var overrideUpn = ConfigurationManager.AppSettings["UserUpnOverride"];
                if (!string.IsNullOrWhiteSpace(overrideUpn))
                {
                    _userUpn = overrideUpn.Trim();
                }
                else
                {
                    var user = NormalizeUserPart(Environment.UserName);
                    _userUpn = string.IsNullOrWhiteSpace(user)
                        ? null
                        : user + "@korstructural.com";
                }
            }
            catch
            {
                _connectionString = null;
                _favoritesRepo = null;
                _userUpn = null;
            }
        }



        #region IRibbonExtensibility Members

        public string GetCustomUI(string ribbonID)
        {
            return GetResourceText("EmailFilerv2.EmailFilerRibbon.xml");
        }

        #endregion

        #region Ribbon Callbacks

        public void Ribbon_Load(Office.IRibbonUI ribbonUI)
        {
            this.ribbon = ribbonUI;
        }

        // NEW: return custom KOR icons for Quick File / Quick Transfer
        public IPictureDisp GetQuickFileIcon(Office.IRibbonControl control)
        {
            // Uses ribbon_logo from Resources.resx
            return ImageHelper.ToPictureDisp(Properties.Resources.ribbon_logo);
        }

        public IPictureDisp GetQuickTransferIcon(Office.IRibbonControl control)
        {
            // Currently reuse the same icon; you can change this later if you add another image
            return ImageHelper.ToPictureDisp(Properties.Resources.ribbon_logo);
        }

        #endregion

        // --------------------------------------------------------------------
        // FILE SELECTED EMAILS  (main flow)
        // --------------------------------------------------------------------
        public void OnFileSelectedEmails(Office.IRibbonControl control)
        {
            Outlook.Application outlookApp = Globals.ThisAddIn.Application;
            Outlook.Selection selection = outlookApp.ActiveExplorer().Selection;

            if (selection == null || selection.Count == 0)
            {
                MessageBox.Show("No email selected.", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var mails = new List<Outlook.MailItem>();
            foreach (object obj in selection)
            {
                if (obj is Outlook.MailItem mail)
                    mails.Add(mail);
            }

            if (mails.Count == 0)
            {
                MessageBox.Show("No email selected.", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Reuse the same logic that ItemSend will use
            FileMailItems(outlookApp, mails);
        }

        /// <summary>
        /// Core filing logic used by both the ribbon button and the ItemSend hook.
        /// Saves temp .msg copies, launches the WPF picker, reads the result,
        /// and tags originals with a category.
        /// Returns true if a project was chosen and filing completed; false if
        /// the user cancelled or an error occurred early.
        /// </summary>
        internal static bool FileMailItems(Outlook.Application outlookApp, IList<Outlook.MailItem> mails)
        {
            if (mails == null || mails.Count == 0)
            {
                MessageBox.Show("No email selected.", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            // Prepare temp folder + manifest
            string tempRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KorEmailFiler", "Temp");

            Directory.CreateDirectory(tempRoot);

            string manifestPath = Path.Combine(tempRoot, "Manifest.txt");

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string korDir = Path.Combine(appData, "KOR");
            Directory.CreateDirectory(korDir);
            string resultPath = Path.Combine(korDir, "EmailFilePickerResult.txt");
            string filedPathsResultPath = Path.Combine(korDir, "EmailFilePickerFiledPaths.txt");

            // Clear old files
            try
            {
                if (File.Exists(manifestPath)) File.Delete(manifestPath);
                if (File.Exists(resultPath)) File.Delete(resultPath);
                if (File.Exists(filedPathsResultPath)) File.Delete(filedPathsResultPath);
            }
            catch
            {
                // best-effort cleanup; ignore failures
            }

            var tempFiles = new List<string>();
            var manifestLines = new List<string>();

            foreach (var mail in mails)
            {
                if (mail == null)
                    continue;

                string safeName = BuildSafeEmailFileName(mail);
                string destPath = Path.Combine(tempRoot, safeName);
                destPath = EnsureUniquePath(destPath);

                try
                {
                    // Save a .msg copy
                    mail.SaveAs(destPath, Outlook.OlSaveAsType.olMSGUnicode);
                    tempFiles.Add(destPath);

                    string entryId = mail.EntryID ?? string.Empty;
                    string storeId = string.Empty;

                    try
                    {
                        Outlook.MAPIFolder folder = mail.Parent as Outlook.MAPIFolder;
                        if (folder != null && folder.StoreID != null)
                            storeId = folder.StoreID;
                    }
                    catch
                    {
                        // best effort only
                    }

                    manifestLines.Add(destPath + "|" + entryId + "|" + storeId);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Temp save failed: " + ex);
                }
            }

            if (tempFiles.Count == 0)
            {
                MessageBox.Show("Could not prepare any email files to process.",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            try
            {
                File.WriteAllLines(manifestPath, manifestLines.ToArray(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not write manifest file:\n" + ex.Message,
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            // Resolve Kor.Operations.App.exe in a deployment-safe way
            string exePath = HostExeResolver.Resolve();
            if (string.IsNullOrEmpty(exePath))
            {
                MessageBox.Show(
                    "Filed Email app is not installed or the path is incorrect.\n\n" +
                    "Kor.Operations.App.exe was not found next to the add-in or in the KorTransmittalsAppPath setting.",
                    "File Selected Emails",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            }

            string argString = "--file-emails=\"" + string.Join("|", tempFiles) + "\"";

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = argString,
                    UseShellExecute = false
                };

                using (var proc = Process.Start(psi))
                {
                    if (proc != null)
                        proc.WaitForExit(); // important: wait so result file is ready
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Could not open File Emails app:\n\n" + ex.Message,
                    "File Selected Emails",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return false;
            }

            // Read the chosen project number from the WPF side
            string projectNo = string.Empty;
            try
            {
                if (File.Exists(resultPath))
                {
                    projectNo = File.ReadAllText(resultPath, Encoding.UTF8).Trim();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Reading result file failed: " + ex);
            }

            if (string.IsNullOrEmpty(projectNo))
            {
                // User cancelled or nothing selected – nothing to tag
                return false;
            }

            // Load manifest back into memory
            var manifest = new Dictionary<string, Tuple<string, string>>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (var line in File.ReadAllLines(manifestPath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    string[] parts = line.Split('|');
                    if (parts.Length >= 3)
                    {
                        manifest[parts[0]] = Tuple.Create(parts[1], parts[2]);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Reading manifest failed: " + ex);
            }

            // Read the per-source success list written by the WPF picker.
            // File presence == new contract; absence == old WPF, fall back to tagging everything.
            HashSet<string> filedSet = null;

            if (File.Exists(filedPathsResultPath))
            {
                filedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                try
                {
                    foreach (var line in File.ReadAllLines(filedPathsResultPath, Encoding.UTF8))
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                            filedSet.Add(line.Trim());
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Reading filed-paths file failed: " + ex);
                }
            }

            // Tag the originals
            Outlook.NameSpace session = outlookApp.Session;
            string tag = "Filed in " + projectNo;

            foreach (string tempPath in tempFiles)
            {
                if (filedSet != null && !filedSet.Contains(tempPath))
                    continue;

                if (!manifest.TryGetValue(tempPath, out var info))
                    continue;

                string entryId = info.Item1;
                string storeId = info.Item2;

                if (string.IsNullOrEmpty(entryId))
                    continue;

                try
                {
                    Outlook.MailItem original = null;

                    if (!string.IsNullOrEmpty(storeId))
                    {
                        original = session.GetItemFromID(entryId, storeId) as Outlook.MailItem;
                    }
                    else
                    {
                        original = session.GetItemFromID(entryId) as Outlook.MailItem;
                    }

                    if (original == null)
                        continue;

                    string cats = original.Categories ?? string.Empty;

                    if (string.IsNullOrEmpty(cats))
                    {
                        original.Categories = tag;
                    }
                    else
                    {
                        // Avoid duplicate tag (case-insensitive)
                        if (cats.IndexOf(tag, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            original.Categories = cats + "; " + tag;
                        }
                    }

                    original.Save();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Tagging original failed: " + ex);
                }
            }

            // Surface partial-failure to the user and log per-file losses so we can
            // diagnose Kor-fs01 hiccups during burst filing.
            if (filedSet != null && filedSet.Count < tempFiles.Count)
            {
                var unfiled = new List<string>();
                foreach (var tempPath in tempFiles)
                {
                    if (!filedSet.Contains(tempPath))
                        unfiled.Add(tempPath);
                }

                LogRibbonFileLosses(unfiled);

                MessageBox.Show(
                    "Filed " + filedSet.Count + " of " + tempFiles.Count + ".\n\n" +
                    unfiled.Count + " email(s) could not be saved to disk and were NOT tagged.\n\n" +
                    "See log:\n" + GetRibbonFilingLogPath(),
                    "File Selected Emails",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            return true;
        }

        // Same log directory as ItemsToFileProcessor / EmailProcessor so all
        // filing events end up in one place. Rolled monthly
        // (EmailFilingLog_YYYY-MM.txt) — mirrors GetFilingLogPath() in
        // ItemsToFileProcessor and GetSharedFilingLogPath() on the Operations
        // side; keep all three in sync if the directory changes.
        private const string RibbonFilingLogDir =
            @"\\kor-fs01\Projects\Reporting\Scripts\Logs";

        private static string GetRibbonFilingLogPath()
        {
            return Path.Combine(
                RibbonFilingLogDir,
                "EmailFilingLog_" + DateTime.Now.ToString("yyyy-MM") + ".txt");
        }

        private static void LogRibbonFileLosses(IList<string> unfiledTempPaths)
        {
            if (unfiledTempPaths == null || unfiledTempPaths.Count == 0)
                return;

            try
            {
                var lines = new List<string>(unfiledTempPaths.Count);
                string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string user = Environment.UserName ?? string.Empty;

                foreach (var p in unfiledTempPaths)
                {
                    lines.Add(ts + " | " + user + " | RIBBON-FILE-LOST | " + p);
                }

                File.AppendAllLines(GetRibbonFilingLogPath(), lines);
            }
            catch
            {
                // Never break Outlook because logging failed
            }
        }

        // --------------------------------------------------------------------
        // NEW: QUICK ACCESS "JUST FILE IT" (favorites)
        // --------------------------------------------------------------------

        // Builds the inner XML for the dynamic menu from UserFavorites
        public string GetQuickFileMenuContent(Office.IRibbonControl control)
        {
            var sb = new StringBuilder();
            sb.Append("<menu xmlns=\"http://schemas.microsoft.com/office/2009/07/customui\">");

            var favorites = GetUserFavorites();

            if (favorites.Count == 0)
            {
                sb.Append("<button id=\"btnQuickFileNone\" label=\"No favorites configured\" enabled=\"false\" />");
            }
            else
            {
                foreach (var fav in favorites)
                {
                    if (fav == null) continue;

                    var projNo = fav.ProjectNo ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(projNo))
                        continue;

                    var label = projNo;
                    if (!string.IsNullOrWhiteSpace(fav.ProjectName))
                        label += " " + fav.ProjectName;

                    var id = "btnQuickFile_" + projNo.Replace(" ", "_");

                    sb.Append("<button id=\"")
                      .Append(EscapeForXml(id))
                      .Append("\" label=\"")
                      .Append(EscapeForXml(label))
                      .Append("\" tag=\"")
                      .Append(EscapeForXml(projNo))
                      .Append("\" onAction=\"OnQuickFileToFavorite\" />");
                }
            }

            sb.Append("</menu>");
            return sb.ToString();
        }

        // Called when user picks a project from the "Just File It" menu
        public void OnQuickFileToFavorite(Office.IRibbonControl control)
        {
            try
            {
                string projectNo = control.Tag as string;
                if (string.IsNullOrWhiteSpace(projectNo))
                {
                    MessageBox.Show("Unable to determine project for Quick File.",
                        "Quick File",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                Outlook.Application outlookApp = Globals.ThisAddIn.Application;
                Outlook.Selection selection = outlookApp.ActiveExplorer().Selection;

                if (selection == null || selection.Count == 0)
                {
                    // Silent: user will see nothing happen if nothing is selected
                    return;
                }

                var mails = new List<Outlook.MailItem>();
                foreach (object obj in selection)
                {
                    if (obj is Outlook.MailItem mail)
                        mails.Add(mail);
                }

                if (mails.Count == 0)
                    return;

                // Delegate to the same autofile engine (ItemsToFileProcessor)
                Globals.ThisAddIn.QuickFileSelectedEmails(projectNo, mails);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Quick File failed:\n" + ex.Message,
                    "Quick File",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private List<SqlFavoritesRepository.FavoriteProject> GetUserFavorites()
        {
            var list = new List<SqlFavoritesRepository.FavoriteProject>();

            if (_favoritesRepo == null || string.IsNullOrWhiteSpace(_userUpn))
                return list;

            try
            {
                var favs = _favoritesRepo.GetFavorites(_userUpn);
                if (favs != null)
                    list.AddRange(favs);
            }
            catch
            {
                // fail silent; menu will show "No favorites configured"
            }

            return list;
        }

        // --------------------------------------------------------------------
        // SEARCH FILED EMAILS
        // --------------------------------------------------------------------
        public void OnSearchFiledEmails(Office.IRibbonControl control)
        {
            try
            {
                string exePath = HostExeResolver.Resolve();
                if (string.IsNullOrEmpty(exePath))
                {
                    MessageBox.Show(
                        "Filed Email Search app is not installed or the path is incorrect.\n\n" +
                        "Kor.Operations.App.exe was not found next to the add-in or in the KorTransmittalsAppPath setting.",
                        "Filed Email Search",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "--email-search",
                    UseShellExecute = false
                };

                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Could not open Filed Email Search:\n\n" + ex.Message,
                    "Filed Email Search",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        // --------------------------------------------------------------------
        // QUICK TRANSFER
        // --------------------------------------------------------------------
        public void OnQuickTransfer(Office.IRibbonControl control)
        {
            try
            {
                string exePath = HostExeResolver.Resolve();
                if (string.IsNullOrEmpty(exePath))
                {
                    MessageBox.Show(
                        "Quick Transfer app is not installed or the path is incorrect.\r\n\r\n" +
                        "Kor.Operations.App.exe was not found next to the add-in or in the KorTransmittalsAppPath setting.",
                        "Quick Transfer",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                var app = Globals.ThisAddIn.Application;
                Outlook.MailItem mail = GetActiveMailItem(app);

                if (mail == null)
                {
                    MessageBox.Show(
                        "Open or select a mail item first.",
                        "Quick Transfer",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                // From = current user
                string fromEmail = GetCurrentUserEmail(app);

                // Build reply-all To / Cc lists with SMTP addresses, excluding me
                BuildReplyAllAddresses(mail, fromEmail, out string to, out string cc);

                // Fallback to raw To/Cc strings (user can edit) if SMTP resolution failed
                if (string.IsNullOrWhiteSpace(to))
                    to = mail.To ?? string.Empty;
                if (string.IsNullOrWhiteSpace(cc))
                    cc = mail.CC ?? string.Empty;

                // Ensure subject starts with "RE:"
                string subject = mail.Subject ?? string.Empty;
                if (!subject.TrimStart().StartsWith("RE:", StringComparison.OrdinalIgnoreCase))
                {
                    subject = "RE: " + subject;
                }

                // Build command-line args for Kor.Operations.App.exe
                var args = new StringBuilder();
                args.Append("--quick-transfer ");
                args.Append(BuildArg("from", fromEmail)).Append(' ');
                args.Append(BuildArg("to", to)).Append(' ');
                args.Append(BuildArg("cc", cc)).Append(' ');
                args.Append(BuildArg("subject", subject));

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = args.ToString(),
                    UseShellExecute = false
                };

                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Quick Transfer failed:\r\n\r\n" + ex.Message,
                    "Quick Transfer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        // Get the active MailItem from inspector or explorer
        private static Outlook.MailItem GetActiveMailItem(Outlook.Application app)
        {
            // Try active inspector first (open window)
            var inspector = app.ActiveInspector();
            if (inspector != null && inspector.CurrentItem is Outlook.MailItem mi1)
                return mi1;

            // Fallback: selected item in explorer
            var explorer = app.ActiveExplorer();
            var selection = explorer?.Selection;
            if (selection != null && selection.Count > 0)
            {
                for (int i = 1; i <= selection.Count; i++)
                {
                    if (selection[i] is Outlook.MailItem mi2)
                        return mi2;
                }
            }

            return null;
        }

        // Use _userUpn if available, otherwise derive SMTP from Outlook / Windows
        private string GetCurrentUserEmail(Outlook.Application app)
        {
            if (!string.IsNullOrWhiteSpace(_userUpn))
                return _userUpn;

            var smtp = TryGetCurrentUserSmtp(app);
            if (!string.IsNullOrWhiteSpace(smtp))
                return smtp;

            var user = NormalizeUserPart(Environment.UserName);
            if (string.IsNullOrWhiteSpace(user))
                return "noreply@korstructural.com";

            return user + "@korstructural.com";
        }

        // Build reply-all SMTP To / Cc lists, excluding current user
        private static void BuildReplyAllAddresses(
            Outlook.MailItem mail,
            string currentUserEmail,
            out string to,
            out string cc)
        {
            var toList = new List<string>();
            var ccList = new List<string>();

            string senderSmtp = GetSenderSmtp(mail);
            if (!string.IsNullOrWhiteSpace(senderSmtp) &&
                !string.Equals(senderSmtp, currentUserEmail, StringComparison.OrdinalIgnoreCase))
            {
                toList.Add(senderSmtp);
            }

            foreach (Outlook.Recipient r in mail.Recipients)
            {
                string smtp = GetSmtpAddress(r);
                if (string.IsNullOrWhiteSpace(smtp))
                    continue;

                if (string.Equals(smtp, currentUserEmail, StringComparison.OrdinalIgnoreCase))
                    continue;

                var type = (Outlook.OlMailRecipientType)r.Type;
                if (type == Outlook.OlMailRecipientType.olTo)
                {
                    if (!toList.Exists(x => string.Equals(x, smtp, StringComparison.OrdinalIgnoreCase)))
                        toList.Add(smtp);
                }
                else if (type == Outlook.OlMailRecipientType.olCC)
                {
                    if (!ccList.Exists(x => string.Equals(x, smtp, StringComparison.OrdinalIgnoreCase)) &&
                        !toList.Exists(x => string.Equals(x, smtp, StringComparison.OrdinalIgnoreCase)))
                    {
                        ccList.Add(smtp);
                    }
                }
            }

            to = string.Join("; ", toList);
            cc = string.Join("; ", ccList);
        }

        // Safely build an argument like: --name="value with \"quotes\""
        private static string BuildArg(string name, string value)
        {
            if (value == null) value = string.Empty;
            value = value.Replace("\"", "\"\"");
            return $"--{name}=\"{value}\"";
        }

        // --------------------------------------------------------------------
        // Helpers
        // --------------------------------------------------------------------
        private static string BuildSafeEmailFileName(Outlook.MailItem mail)
        {
            const int maxLen = 120;
            const string ext = ".msg";

            string subject = mail.Subject ?? "Email";
            string sent = (mail.SentOn == DateTime.MinValue ? DateTime.Now : mail.SentOn)
                .ToString("yyyy-MM-dd HHmm");

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                subject = subject.Replace(c, '_');
            }

            // Normalize odd trailing punctuation/whitespace from Outlook subjects
            // so we do not produce endings like ". .msg".
            subject = subject.Trim().TrimEnd('.');
            if (string.IsNullOrWhiteSpace(subject))
                subject = "Email";

            string prefix = sent + " - ";
            int maxSubjectLen = Math.Max(1, maxLen - prefix.Length - ext.Length);
            if (subject.Length > maxSubjectLen)
                subject = subject.Substring(0, maxSubjectLen).Trim().TrimEnd('.');

            if (string.IsNullOrWhiteSpace(subject))
                subject = "Email";

            return prefix + subject + ext;
        }

        private static string EnsureUniquePath(string path)
        {
            if (!File.Exists(path))
                return path;

            string dir = Path.GetDirectoryName(path) ?? string.Empty;
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);

            int i = 1;
            string candidate;

            do
            {
                candidate = Path.Combine(dir, string.Format("{0} ({1}){2}", name, i, ext));
                i++;
            } while (File.Exists(candidate));

            return candidate;
        }

        private static string GetResourceText(string resourceName)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream stream = asm.GetManifestResourceStream(resourceName))
            {
                if (stream != null)
                {
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }
            return null;
        }

        private static string EscapeForXml(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }

        private static string NormalizeUserPart(string user)
        {
            if (string.IsNullOrWhiteSpace(user)) return string.Empty;

            int idx = user.IndexOf('\\');
            if (idx >= 0 && idx < user.Length - 1)
                return user.Substring(idx + 1);

            return user;
        }

        private static string TryGetCurrentUserSmtp(Outlook.Application app)
        {
            try
            {
                var ns = app.Session;
                var me = ns.CurrentUser;
                if (me == null || me.AddressEntry == null)
                    return null;

                var addrEntry = me.AddressEntry;

                // Exchange user
                if (addrEntry.Type == "EX")
                {
                    var exUser = addrEntry.GetExchangeUser();
                    if (exUser != null && !string.IsNullOrWhiteSpace(exUser.PrimarySmtpAddress))
                        return exUser.PrimarySmtpAddress;
                }

                // SMTP already
                if (!string.IsNullOrWhiteSpace(addrEntry.Address))
                    return addrEntry.Address;

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static string GetSmtpAddress(Outlook.Recipient r)
        {
            if (r == null)
                return null;

            try
            {
                var addrEntry = r.AddressEntry;
                if (addrEntry == null)
                    return null;

                if (addrEntry.Type == "EX")
                {
                    var exUser = addrEntry.GetExchangeUser();
                    if (exUser != null && !string.IsNullOrWhiteSpace(exUser.PrimarySmtpAddress))
                        return exUser.PrimarySmtpAddress;

                    var exDl = addrEntry.GetExchangeDistributionList();
                    if (exDl != null && !string.IsNullOrWhiteSpace(exDl.PrimarySmtpAddress))
                        return exDl.PrimarySmtpAddress;
                }

                // Internet / SMTP recipients
                return addrEntry.Address;
            }
            catch
            {
                return null;
            }
        }

        private static string GetSenderSmtp(Outlook.MailItem mail)
        {
            if (mail == null)
                return null;

            try
            {
                var addr = mail.Sender;
                if (addr == null)
                    return null;

                if (addr.AddressEntryUserType == Outlook.OlAddressEntryUserType.olExchangeUserAddressEntry ||
                    addr.AddressEntryUserType == Outlook.OlAddressEntryUserType.olExchangeRemoteUserAddressEntry)
                {
                    var exUser = addr.GetExchangeUser();
                    if (exUser != null && !string.IsNullOrWhiteSpace(exUser.PrimarySmtpAddress))
                        return exUser.PrimarySmtpAddress;
                }

                if (!string.IsNullOrWhiteSpace(addr.Address))
                    return addr.Address;

                return null;
            }
            catch
            {
                return null;
            }
        }
        private static string ResolveOperationsExe()
        {
            try
            {
                var configured = System.Configuration.ConfigurationManager.AppSettings["KorTransmittalsAppPath"];
                if (!string.IsNullOrWhiteSpace(configured) && System.IO.File.Exists(configured))
                    return configured;

                var dllDir = System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);

                var fallback = System.IO.Path.Combine(dllDir, "Kor.Operations.App.exe");
                if (System.IO.File.Exists(fallback))
                    return fallback;
            }
            catch {}

            return null;
        }
    }

    // NEW: helper to convert Bitmap → IPictureDisp for Ribbon images
    internal static class ImageHelper
    {
        private sealed class PictureDispConverter : AxHost
        {
            private PictureDispConverter() : base("") { }

            public static IPictureDisp Convert(Image image)
            {
                return (IPictureDisp)GetIPictureDispFromPicture(image);
            }
        }

        public static IPictureDisp ToPictureDisp(Image image)
        {
            if (image == null) return null;
            return PictureDispConverter.Convert(image);
        }
    }
}

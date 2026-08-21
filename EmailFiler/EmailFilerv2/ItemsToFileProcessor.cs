using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Outlook = Microsoft.Office.Interop.Outlook;

namespace EmailFilerv2
{
    /// <summary>
    /// Handles:
    ///  - Keeping the "Emails To File" folder in Outlook in sync with the user's favourites.
    ///  - On Outlook Quit, filing any MailItems in those folders to the
    ///    proper project Emails folder, and deleting them from Outlook only after a successful save.
    ///  - Inserting filed emails into KorEmailIndex.dbo.Emails.
    /// </summary>
    internal sealed class ItemsToFileProcessor
    {
        private const string RootFolderName = "Emails To File";
        private const string ProjectsRoot = @"\\Kor-fs01\Projects\Projects";

        // Writer label written to dbo.Emails.Source. Mirrors
        // Kor.Operations.App.Email.EmailSources.Vsto in the Operations
        // solution — keep both in sync if the value ever changes.
        private const string SourceLabel = "VSTO";

        private readonly Outlook.Application _application;
        private readonly string _korTransmittalsConnectionString;
        private readonly string _emailIndexConnectionString;
        private readonly string _userUpn;
        private readonly SqlFavoritesRepository _favoritesRepo;

        // Log to the same place as the manual filing path. Rolled monthly
        // (EmailFilingLog_YYYY-MM.txt) so the file never grows unbounded —
        // mirrors GetSharedFilingLogPath() in EmailFilingService.cs on the
        // Operations side; keep both in sync if the directory changes.
        private const string FilingLogDir =
            @"\\kor-fs01\Projects\Reporting\Scripts\Logs";

        private static string GetFilingLogPath()
        {
            return Path.Combine(
                FilingLogDir,
                "EmailFilingLog_" + DateTime.Now.ToString("yyyy-MM") + ".txt");
        }

        private sealed class ProjectEntry
        {
            public string Code { get; set; } = string.Empty;        // e.g. "30694-01"
            public string DisplayName { get; set; } = string.Empty; // folder name e.g. "30694-01 Project Name"
            public string FullPath { get; set; } = string.Empty;    // \\Kor-fs01\Projects\Projects\<Cat>\<ProjectFolder>
        }

        public ItemsToFileProcessor(Outlook.Application application)
        {
            _application = application ?? throw new ArgumentNullException(nameof(application));

            // Same UPN logic as elsewhere
            var overrideUpn = ConfigurationManager.AppSettings["UserUpnOverride"];
            _userUpn = !string.IsNullOrWhiteSpace(overrideUpn)
                ? overrideUpn.Trim()
                : NormalizeUserPart(Environment.UserName) + "@korstructural.com";

            var korTrans = ConfigurationManager.ConnectionStrings["KorTransmittals"];
            _korTransmittalsConnectionString = korTrans != null ? korTrans.ConnectionString : null;

            var idx = ConfigurationManager.ConnectionStrings["KorEmailIndex"];
            _emailIndexConnectionString = idx != null ? idx.ConnectionString : null;

            if (!string.IsNullOrWhiteSpace(_korTransmittalsConnectionString))
            {
                _favoritesRepo = new SqlFavoritesRepository(_korTransmittalsConnectionString);
            }
        }

        /// <summary>
        /// Called on Outlook startup (from ThisAddIn).
        /// Keeps the "Emails To File" subfolder structure aligned with favourites.
        /// </summary>
        public void SyncFolders()
        {
            SafeLog("SyncFolders: starting.");

            if (!IsItemsToFileEnabled())
            {
                SafeLog("SyncFolders: ItemsToFileEnabled returned FALSE; skipping folder sync.");
                return;
            }

            if (string.IsNullOrWhiteSpace(_korTransmittalsConnectionString) || _favoritesRepo == null)
                return;

            var rootFolder = EnsureRootFolder();
            if (rootFolder == null)
                return;

            var favorites = SafeGetFavorites();
            if (favorites.Count == 0)
                return;

            var projectMap = ResolveProjectsForFavorites(favorites);
            if (projectMap.Count == 0)
                return;

            // Desired folder names
            var desiredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var fav in favorites)
            {
                ProjectEntry proj;
                if (!projectMap.TryGetValue(fav.ProjectNo, out proj))
                    continue;

                // Use the actual project folder name as Outlook subfolder name
                var desiredFolderName = proj.DisplayName;
                if (string.IsNullOrWhiteSpace(desiredFolderName))
                    desiredFolderName = fav.ProjectNo;

                desiredNames.Add(desiredFolderName);
            }

            // Existing subfolders under "Emails To File"
            var existing = rootFolder.Folders.Cast<Outlook.MAPIFolder>().ToList();

            // Remove obsolete folders that are not favourites anymore AND are empty
            foreach (var folder in existing)
            {
                if (desiredNames.Contains(folder.Name))
                    continue;

                if (folder.Items.Count == 0)
                {
                    try
                    {
                        folder.Delete();
                    }
                    catch
                    {
                        // ignore – safest is to leave folder there if delete fails
                    }
                }
            }

            // Add missing folders for favourites
            existing = rootFolder.Folders.Cast<Outlook.MAPIFolder>().ToList();
            var existingNames = new HashSet<string>(
                existing.Select(f => f.Name),
                StringComparer.OrdinalIgnoreCase);

            foreach (var fav in favorites)
            {
                ProjectEntry proj;
                if (!projectMap.TryGetValue(fav.ProjectNo, out proj))
                    continue;

                var name = proj.DisplayName;
                if (string.IsNullOrWhiteSpace(name))
                    name = fav.ProjectNo;

                if (!existingNames.Contains(name))
                {
                    try
                    {
                        rootFolder.Folders.Add(name, Outlook.OlDefaultFolders.olFolderInbox);
                    }
                    catch
                    {
                        // best-effort only
                    }
                }
            }
        }


        /// <summary>
        /// Called on Outlook Quit (hooked via Application.Quit in ThisAddIn).
        /// For each favourite project folder under "Emails To File":
        ///  - Find the corresponding project on the file server
        ///  - Save each MailItem to \\Kor-fs01\Projects\Projects\...\Emails\yyyy-MM as .msg
        ///  - Insert a row into KorEmailIndex.dbo.Emails
        ///  - Delete the MailItem from Outlook ONLY if the save succeeded.
        /// </summary>
        public void ProcessOnQuit()
        {
            SafeLog("ProcessOnQuit: Outlook is quitting; starting Items-to-File processing.");

            if (!IsItemsToFileEnabled())
            {
                SafeLog("ProcessOnQuit: ItemsToFileEnabled returned FALSE; nothing will be filed.");
                return;
            }

            if (string.IsNullOrWhiteSpace(_korTransmittalsConnectionString) || _favoritesRepo == null)
                return;

            var rootFolder = FindRootFolder();
            if (rootFolder == null)
                return;

            var favorites = SafeGetFavorites();
            if (favorites.Count == 0)
                return;

            var projectMap = ResolveProjectsForFavorites(favorites);
            if (projectMap.Count == 0)
                return;

            // Build a mapping from Outlook folder name -> ProjectEntry
            // We match Outlook folder names that start with the project code, e.g. "30694-01"
            var outlookFolderNameToProject = new Dictionary<string, ProjectEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (Outlook.MAPIFolder sub in rootFolder.Folders)
            {
                string folderName = sub.Name ?? string.Empty;
                if (string.IsNullOrWhiteSpace(folderName))
                    continue;

                foreach (var fav in favorites)
                {
                    ProjectEntry proj;
                    if (!projectMap.TryGetValue(fav.ProjectNo, out proj))
                        continue;

                    // if folder name starts with "30694-01" (code), treat as this project's queue
                    if (folderName.StartsWith(proj.Code, StringComparison.OrdinalIgnoreCase))
                    {
                        outlookFolderNameToProject[folderName] = proj;
                        break;
                    }
                }
            }

            if (outlookFolderNameToProject.Count == 0)
                return;

            foreach (Outlook.MAPIFolder sub in rootFolder.Folders)
            {
                string folderName = sub.Name ?? string.Empty;
                if (string.IsNullOrWhiteSpace(folderName))
                    continue;

                ProjectEntry project;
                if (!outlookFolderNameToProject.TryGetValue(folderName, out project))
                    continue;

                // Snapshot MailItems in this folder
                var mailItems = new List<Outlook.MailItem>();
                foreach (object item in sub.Items)
                {
                    Outlook.MailItem mail = item as Outlook.MailItem;
                    if (mail != null)
                        mailItems.Add(mail);
                }

                foreach (var mail in mailItems)
                {
                    try
                    {
                        bool filed = FileMailToProject(mail, project);
                        if (filed)
                        {
                            // Tag and move to Deleted Items (AutoFile behavior)
                            MarkAsFiledAndMoveToDeleted(mail, project.Code);
                        }
                        // If not filed, leave it there so user can see it didn't move.
                    }
                    catch
                    {
                        // On any failure, do NOT delete or move from Outlook
                    }
                }
            }
        }


        // --------------------------------------------------------------------
        // Helpers
        // --------------------------------------------------------------------

        private void MarkAsFiledAndMoveToDeleted(Outlook.MailItem mail, string projectNumber)
        {
            if (mail == null)
                return;

            // 1) Apply the same category tag as manual filing: "Filed in <project>"
            try
            {
                string tag = "Filed in " + (projectNumber ?? string.Empty);
                string cats = mail.Categories ?? string.Empty;

                if (string.IsNullOrWhiteSpace(cats))
                {
                    mail.Categories = tag;
                }
                else if (cats.IndexOf(tag, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    mail.Categories = cats + "; " + tag;
                }

                mail.Save();
            }
            catch
            {
                // Non-fatal – if tagging fails, still attempt move
            }

            // 2) Move to the user's Deleted Items folder
            try
            {
                var deleted = _application?.Session?
                    .GetDefaultFolder(Outlook.OlDefaultFolders.olFolderDeletedItems)
                    as Outlook.MAPIFolder;

                if (deleted != null)
                {
                    mail.Move(deleted);
                }
            }
            catch
            {
                // If move fails, leave the item where it is
            }
        }

        private static void SafeLog(string message)
        {
            try
            {
                var line = string.Format(
                    "{0:yyyy-MM-dd HH:mm:ss} | ITEMS-TO-FILE | {1}",
                    DateTime.Now,
                    message ?? string.Empty);

                File.AppendAllLines(GetFilingLogPath(), new[] { line });
            }
            catch
            {
                // Never break Outlook because logging failed
            }
        }

        private bool IsItemsToFileEnabled()
        {
            // If we don't have a UPN at all, we can't look up preferences.
            if (string.IsNullOrWhiteSpace(_userUpn))
            {
                SafeLog("IsItemsToFileEnabled: _userUpn is empty; disabling Items-to-File processing.");
                return false;
            }

            // If there is no KorTransmittals connection string configured in this add-in,
            // we cannot read the shared preferences table. In that case we log and fall
            // back to the WPF default (enabled).
            if (string.IsNullOrWhiteSpace(_korTransmittalsConnectionString))
            {
                SafeLog("IsItemsToFileEnabled: KorTransmittals connection string is missing; defaulting to ENABLED.");
                return true;
            }

            try
            {
                using (var con = new SqlConnection(_korTransmittalsConnectionString))
                using (var cmd = con.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT ItemsToFileEnabled FROM dbo.UserPreferences WHERE UserUpn = @upn";
                    cmd.Parameters.AddWithValue("@upn", _userUpn);

                    con.Open();
                    object value = cmd.ExecuteScalar();

                    if (value == null || value == DBNull.Value)
                    {
                        // Match WPF default: first time -> ItemsToFileEnabled = true
                        SafeLog("IsItemsToFileEnabled: no row in UserPreferences for '" + _userUpn +
                                "'; defaulting to ENABLED.");
                        return true;
                    }

                    bool enabled;

                    if (value is bool)
                    {
                        enabled = (bool)value;
                    }
                    else if (value is int)
                    {
                        enabled = ((int)value) != 0;
                    }
                    else if (!bool.TryParse(value.ToString(), out enabled))
                    {
                        // Unknown type in DB; safest behaviour is to honour the WPF default (enabled).
                        enabled = true;
                    }

                    SafeLog("IsItemsToFileEnabled: preference for '" + _userUpn + "' = " + enabled);
                    return enabled;
                }
            }
            catch (Exception ex)
            {
                // Any error talking to the preferences database (schema change, network issue, etc.)
                // should not silently disable Items-to-File. We log the error and fall back to ENABLED.
                SafeLog(
                    "IsItemsToFileEnabled: exception reading UserPreferences for '" + _userUpn +
                    "': " + ex.Message + "  -> defaulting to ENABLED.");
                return true;
            }
        }


        private List<SqlFavoritesRepository.FavoriteProject> SafeGetFavorites()
        {
            var list = new List<SqlFavoritesRepository.FavoriteProject>();

            if (_favoritesRepo == null)
                return list;

            try
            {
                list = _favoritesRepo.GetFavorites(_userUpn) ?? new List<SqlFavoritesRepository.FavoriteProject>();
            }
            catch
            {
                // return empty list on failure
            }

            return list;
        }

        private Dictionary<string, ProjectEntry> ResolveProjectsForFavorites(
            List<SqlFavoritesRepository.FavoriteProject> favorites)
        {
            var result = new Dictionary<string, ProjectEntry>(StringComparer.OrdinalIgnoreCase);

            if (!Directory.Exists(ProjectsRoot) || favorites.Count == 0)
                return result;

            var codesNeeded = new HashSet<string>(
                favorites
                    .Select(f => f.ProjectNo ?? string.Empty)
                    .Where(p => !string.IsNullOrWhiteSpace(p)),
                StringComparer.OrdinalIgnoreCase);

            if (codesNeeded.Count == 0)
                return result;

            try
            {
                var categories = Directory.GetDirectories(ProjectsRoot);

                foreach (var category in categories)
                {
                    string[] subfolders;
                    try
                    {
                        subfolders = Directory.GetDirectories(category);
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (var folder in subfolders)
                    {
                        var name = Path.GetFileName(folder);
                        if (string.IsNullOrEmpty(name) || name.Length < 8)
                            continue;

                        // Skip template/training projects where the number prefix uses x-placeholders,
                        // e.g. 000xx-01, 30xxx-01, etc.
                        var prefix = name.Substring(0, Math.Min(5, name.Length));
                        if (prefix.IndexOf('x') >= 0 || prefix.IndexOf('X') >= 0)
                            continue;

                        var codePart = name.Substring(0, 8);
                        if (!codePart.Contains("-"))
                            continue;

                        if (!codesNeeded.Contains(codePart))
                            continue;

                        if (!result.ContainsKey(codePart))
                        {
                            var entry = new ProjectEntry
                            {
                                Code = codePart,
                                DisplayName = name,
                                FullPath = folder
                            };
                            result[codePart] = entry;
                        }
                    }
                }
            }
            catch
            {
                // On failure, just return what we found so far (likely empty)
            }

            return result;
        }

        private Outlook.MAPIFolder EnsureRootFolder()
        {
            var folder = FindRootFolder();
            if (folder != null)
                return folder;

            var store = _application.Session.DefaultStore;
            var root = store.GetRootFolder();

            try
            {
                return root.Folders.Add(RootFolderName, Outlook.OlDefaultFolders.olFolderInbox);
            }
            catch
            {
                // If add fails, fall back to any existing or the store root
                var existing = FindRootFolder();
                return existing ?? root;
            }
        }

        private Outlook.MAPIFolder FindRootFolder()
        {
            var store = _application.Session.DefaultStore;
            var root = store.GetRootFolder();

            foreach (Outlook.MAPIFolder f in root.Folders)
            {
                if (string.Equals(f.Name, RootFolderName, StringComparison.OrdinalIgnoreCase))
                    return f;
            }

            return null;
        }

        private bool FileMailToProject(Outlook.MailItem mail, ProjectEntry project)
        {
            if (mail == null || project == null)
                return false;

            if (string.IsNullOrWhiteSpace(project.FullPath) || !Directory.Exists(project.FullPath))
                return false;

            try
            {
                // New folder structure: <Project>\Newforma\email\yyyy-MM
                string monthFolder = Path.Combine(
                    project.FullPath,
                    "Newforma",
                    "email",
                    DateTime.Now.ToString("yyyy-MM"));

                Directory.CreateDirectory(monthFolder);

                string projectNumber = project.Code ?? string.Empty;
                string rawSubject = mail.Subject ?? "No Subject";

                // Remove invalid filename chars
                string safeSubject = new string(
                    rawSubject.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());

                if (string.IsNullOrWhiteSpace(safeSubject))
                    safeSubject = "No_Subject";

                string dateStamp = DateTime.Now.ToString("yyyy-MM-dd");

                // Default: just subject.msg
                string filename = safeSubject + ".msg";
                string destPath = Path.Combine(monthFolder, filename);

                // Filename too long safeguard
                if (destPath.Length >= 255)
                {
                    int maxSubjectLength = 255 - monthFolder.Length - projectNumber.Length - 15 - 10;
                    if (maxSubjectLength < 1)
                        maxSubjectLength = 1;

                    if (safeSubject.Length > maxSubjectLength)
                        safeSubject = safeSubject.Substring(0, maxSubjectLength);

                    filename = string.Format("{0} - {1} - {2}.msg", projectNumber, dateStamp, safeSubject);
                    destPath = Path.Combine(monthFolder, filename);
                }

                // Avoid overwriting if same name already exists
                destPath = EnsureUniquePath(destPath);

                // Save .msg to disk
                mail.SaveAs(destPath, Outlook.OlSaveAsType.olMSGUnicode);

                if (!File.Exists(destPath))
                {
                    SafeLog($"FileMailToProject FAILED (no file) for Project={projectNumber}, Path={destPath}");
                    return false;
                }

                SafeLog($"Saved to disk for index: Project={projectNumber}, Path={destPath}");

                // Still return true even if indexing fails — the file IS on
                // disk and the Outlook "filed" tag is correct. The
                // INDEX_NOT_PERSISTED log marker is what surfaces the failure.
                bool indexed = TryInsertEmailRecord(projectNumber, destPath, mail);
                if (!indexed)
                {
                    SafeLog($"INDEX_NOT_PERSISTED: Email saved to disk but NOT indexed in KorEmailIndex. Project={projectNumber}, Path={destPath}. Search will not find this email until reindexed.");
                }

                return true;
            }
            catch (Exception ex)
            {
                SafeLog(string.Format(
                    "FileMailToProject FAILED for Project={0}: {1}: {2}",
                    project != null ? project.Code : "NULL",
                    ex.GetType().Name,
                    ex.Message));
                // Any error -> treat as not filed (Outlook item will stay in the folder)
                return false;
            }
        }

        private bool TryInsertEmailRecord(string projectNumber, string filePath, Outlook.MailItem mail)
        {
            if (string.IsNullOrWhiteSpace(_emailIndexConnectionString))
            {
                SafeLog("INDEX_NOT_PERSISTED: KorEmailIndex connection string missing; skipping DB insert.");
                return false;
            }

            try
            {
                var fi = new FileInfo(filePath);
                if (!fi.Exists)
                {
                    SafeLog($"TryInsertEmailRecord: file does not exist: {filePath}");
                    return false;
                }

                string fileName = fi.Name;
                long sizeBytes = fi.Length;
                DateTime lastWriteUtc = fi.LastWriteTimeUtc;
                string sha1Hex = ComputeSha1Hex(filePath);

                string ext = Path.GetExtension(filePath) ?? string.Empty;
                string format = ext.Equals(".msg", StringComparison.OrdinalIgnoreCase)
                    ? "MSG"
                    : ext.Equals(".eml", StringComparison.OrdinalIgnoreCase)
                        ? "EML"
                        : string.Empty;

                // Mail-based metadata
                string subject = mail.Subject;
                string fromDisplay = mail.SenderName;
                string fromEmail = mail.SenderEmailAddress;
                string toList = BuildRecipientList(mail.Recipients, Outlook.OlMailRecipientType.olTo);
                string ccList = BuildRecipientList(mail.Recipients, Outlook.OlMailRecipientType.olCC);
                string bccList = BuildRecipientList(mail.Recipients, Outlook.OlMailRecipientType.olBCC);

                DateTime? sentUtc = ToUtcOrNull(mail.SentOn);
                DateTime? receivedUtc = ToUtcOrNull(mail.ReceivedTime);

                bool hasAttachments = mail.Attachments != null && mail.Attachments.Count > 0;
                int attachmentCount = mail.Attachments != null ? mail.Attachments.Count : 0;

                // Body text (plain text; matches how EmailSearch currently works with BodyText)
                string bodyText = mail.Body;

                // Older PIAs may not expose InternetMessageID; keep this nullable.
                string messageId = null;

                if (!string.IsNullOrEmpty(messageId) && messageId.Length > 512)
                    messageId = messageId.Substring(0, 512);

                // Source='VSTO' so this writer is distinguishable from the WPF
                // picker (which passes 'WPF-PICKER') in the audit log. The proc
                // requires @Source — there is no silent FILEDROP fallback.
                using (var cn = new SqlConnection(_emailIndexConnectionString))
                using (var cmd = new SqlCommand("dbo.UpsertEmail", cn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    cmd.Parameters.Add("@ProjectNumber", SqlDbType.VarChar, 32).Value = (object)(projectNumber ?? string.Empty);
                    cmd.Parameters.Add("@FilePath", SqlDbType.NVarChar, 1024).Value = filePath;
                    cmd.Parameters.Add("@FileName", SqlDbType.NVarChar, 260).Value = fileName;
                    cmd.Parameters.Add("@FileSizeBytes", SqlDbType.BigInt).Value = sizeBytes;
                    cmd.Parameters.Add("@FileLastWriteUtc", SqlDbType.DateTime2).Value = lastWriteUtc;
                    cmd.Parameters.Add("@FileHashSha1", SqlDbType.Char, 40).Value = sha1Hex ?? string.Empty;
                    cmd.Parameters.Add("@Format", SqlDbType.VarChar, 8).Value = format ?? string.Empty;
                    cmd.Parameters.Add("@MessageId", SqlDbType.NVarChar, 512).Value = (object)messageId ?? DBNull.Value;
                    cmd.Parameters.Add("@Subject", SqlDbType.NVarChar, 512).Value = (object)subject ?? DBNull.Value;
                    cmd.Parameters.Add("@FromDisplay", SqlDbType.NVarChar, 256).Value = (object)fromDisplay ?? DBNull.Value;
                    cmd.Parameters.Add("@FromEmail", SqlDbType.NVarChar, 320).Value = (object)fromEmail ?? DBNull.Value;
                    cmd.Parameters.Add("@ToList", SqlDbType.NVarChar, -1).Value = (object)toList ?? DBNull.Value;
                    cmd.Parameters.Add("@CcList", SqlDbType.NVarChar, -1).Value = (object)ccList ?? DBNull.Value;
                    cmd.Parameters.Add("@BccList", SqlDbType.NVarChar, -1).Value = (object)bccList ?? DBNull.Value;
                    cmd.Parameters.Add("@SentOnUtc", SqlDbType.DateTime2).Value = sentUtc.HasValue ? (object)sentUtc.Value : DBNull.Value;
                    cmd.Parameters.Add("@ReceivedOnUtc", SqlDbType.DateTime2).Value = receivedUtc.HasValue ? (object)receivedUtc.Value : DBNull.Value;
                    cmd.Parameters.Add("@BodyText", SqlDbType.NVarChar, -1).Value = (object)bodyText ?? DBNull.Value;
                    cmd.Parameters.Add("@HasAttachments", SqlDbType.Bit).Value = hasAttachments;
                    cmd.Parameters.Add("@AttachmentCount", SqlDbType.Int).Value = attachmentCount;
                    cmd.Parameters.Add("@Source", SqlDbType.VarChar, 32).Value = SourceLabel;
                    cmd.Parameters.Add("@IsCorrupt", SqlDbType.Bit).Value = false;

                    var rv = new SqlParameter("@__Return", SqlDbType.Int) { Direction = ParameterDirection.ReturnValue };
                    cmd.Parameters.Add(rv);

                    cn.Open();
                    cmd.ExecuteNonQuery();

                    int rowsAffected = rv.Value is int n ? n : 0;
                    if (rowsAffected == 1)
                        SafeLog($"DB insert OK for Project={projectNumber}, Path={filePath}");
                    else
                        SafeLog($"DB insert SKIPPED (already indexed) for Project={projectNumber}, Path={filePath}");
                }

                return true;
            }
            catch (Exception ex)
            {
                SafeLog(string.Format(
                    "DB insert FAILED for Project={0}, Path={1}: {2}: {3}",
                    projectNumber ?? "NULL",
                    filePath ?? "NULL",
                    ex.GetType().Name,
                    ex.Message));
                return false;
            }
        }

        private static string BuildRecipientList(Outlook.Recipients recipients, Outlook.OlMailRecipientType type)
        {
            if (recipients == null || recipients.Count == 0)
                return null;

            var list = new List<string>();

            try
            {
                for (int i = 1; i <= recipients.Count; i++)
                {
                    Outlook.Recipient r = recipients[i];
                    if (r == null)
                        continue;

                    if (r.Type == (int)type)
                    {
                        string name = r.Name;
                        if (string.IsNullOrWhiteSpace(name))
                            name = r.Address;
                        if (!string.IsNullOrWhiteSpace(name))
                            list.Add(name);
                    }
                }
            }
            catch
            {
                // best effort only
            }

            if (list.Count == 0)
                return null;

            return string.Join("; ", list);
        }

        private static DateTime? ToUtcOrNull(DateTime dt)
        {
            if (!OutlookDateGuard.IsPlausibleSentOn(dt, DateTime.Now))
                return null;

            if (dt.Kind == DateTimeKind.Unspecified)
                dt = DateTime.SpecifyKind(dt, DateTimeKind.Local);

            return dt.ToUniversalTime();
        }

        private static string ComputeSha1Hex(string path)
        {
            using (var sha1 = SHA1.Create())
            using (var stream = File.OpenRead(path))
            {
                var hash = sha1.ComputeHash(stream);
                // 40-char lowercase hex string
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
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

        private static string NormalizeUserPart(string user)
        {
            if (string.IsNullOrWhiteSpace(user)) return string.Empty;

            var idx = user.IndexOf('\\');
            if (idx >= 0 && idx < user.Length - 1)
                return user.Substring(idx + 1);

            return user;
        }

        /// <summary>
        /// QUICK FILE:
        /// - Uses same FileMailToProject (disk + DB insert) as AutoFile
        /// - Tags with "Filed in &lt;project&gt;"
        /// - DOES NOT move/delete the original (stays in Inbox)
        /// </summary>
        public void QuickFileSelectionToProject(string projectNo, IList<Outlook.MailItem> mails)
        {
            if (string.IsNullOrWhiteSpace(projectNo) || mails == null || mails.Count == 0)
                return;

            var favorites = SafeGetFavorites();
            if (favorites.Count == 0)
                return;

            var projectMap = ResolveProjectsForFavorites(favorites);
            if (!projectMap.TryGetValue(projectNo, out var proj))
                return;

            foreach (var mail in mails)
            {
                try
                {
                    if (mail == null)
                        continue;

                    bool filed = FileMailToProject(mail, proj);

                    if (filed)
                    {
                        // Tag like manual filing / autofile, but DO NOT move/delete.
                        try
                        {
                            string tag = "Filed in " + (projectNo ?? string.Empty);
                            string cats = mail.Categories ?? string.Empty;

                            if (string.IsNullOrWhiteSpace(cats))
                            {
                                mail.Categories = tag;
                            }
                            else if (cats.IndexOf(tag, StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                mail.Categories = cats + "; " + tag;
                            }

                            mail.Save();
                        }
                        catch
                        {
                            // best-effort tagging; never throw back to Outlook
                        }

                        // IMPORTANT:
                        // For "Just File It", we leave the mail in its current folder (e.g. Inbox).
                        // No Delete(), no Move() here.
                    }
                }
                catch
                {
                    // Never bubble up to Outlook
                }
            }
        }

    }
}

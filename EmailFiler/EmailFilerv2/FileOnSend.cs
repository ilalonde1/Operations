using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Outlook = Microsoft.Office.Interop.Outlook;

namespace EmailFilerv2
{
    /// <summary>
    /// "File this email before sending?" -> Yes used to file the email right there, before it was sent:
    /// the filed .msg was an unsent draft with no sent date, so it sorted to the bottom of Filed Email
    /// Search and opened as a draft (Kevin, 2026-09-30). Now the project is chosen at send time, as before,
    /// and the email is filed when its copy lands in Sent Items -- with its real sent date and Message-ID.
    ///
    /// The outgoing email carries an opaque token in an X-header (PS_INTERNET_HEADERS), which survives onto
    /// the Sent Items copy. Deliberately NOT an Outlook UserProperty: those can turn mail to outside
    /// recipients into winmail.dat. The token -> project map is kept on disk, so a message that sits in the
    /// Outbox across an Outlook restart is still filed when it goes.
    /// </summary>
    internal sealed class FileOnSend
    {
        private const string TokenProperty =
            "http://schemas.microsoft.com/mapi/string/{00020386-0000-0000-C000-000000000046}/X-KOR-FileOnSend";

        private static readonly TimeSpan KeepPendingFor = TimeSpan.FromDays(14);

        private readonly Outlook.Application _application;
        private readonly ItemsToFileProcessor _processor;
        private readonly string _pendingPath;

        // Held in a field: a COM event source that goes out of scope stops raising events.
        private Outlook.Items _sentItems;

        public FileOnSend(Outlook.Application application, ItemsToFileProcessor processor)
        {
            _application = application;
            _processor = processor;
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KorEmailFiler");
            Directory.CreateDirectory(dir);
            _pendingPath = Path.Combine(dir, "PendingFileOnSend.txt");
        }

        public void Start()
        {
            var sent = _application.Session.GetDefaultFolder(Outlook.OlDefaultFolders.olFolderSentMail);
            _sentItems = sent.Items;
            _sentItems.ItemAdd += OnSentItemAdded;
        }

        /// <summary>Marks an outgoing email to be filed to projectNo once sent. False if it could not be marked.</summary>
        public bool Arm(Outlook.MailItem mail, string projectNo)
        {
            try
            {
                var token = Guid.NewGuid().ToString("N");
                mail.PropertyAccessor.SetProperty(TokenProperty, token);
                var lines = ReadPending();
                lines.Add(string.Join("|", token, projectNo.Trim(), DateTime.UtcNow.ToString("o")));
                File.WriteAllLines(_pendingPath, lines);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void OnSentItemAdded(object item)
        {
            var mail = item as Outlook.MailItem;
            if (mail == null)
                return;

            string token = null;
            try { token = mail.PropertyAccessor.GetProperty(TokenProperty) as string; } catch { }
            if (string.IsNullOrWhiteSpace(token))
                return;

            var lines = ReadPending();
            var entry = lines.FirstOrDefault(l => l.StartsWith(token + "|", StringComparison.Ordinal));
            if (entry == null)
                return;

            var projectNo = entry.Split('|')[1];
            bool filed = false;
            try { filed = _processor.FileSentItemToProject(projectNo, mail); } catch { }

            if (filed)
            {
                lines.Remove(entry);
                try { File.WriteAllLines(_pendingPath, lines); } catch { }
                return;
            }

            MessageBox.Show(
                "Your email was sent, but it could not be filed to " + projectNo + ".\n\n" +
                "File it from Sent Items with File Selected Emails.",
                "File Email",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        private List<string> ReadPending()
        {
            try
            {
                if (!File.Exists(_pendingPath))
                    return new List<string>();

                var cutoff = DateTime.UtcNow - KeepPendingFor;
                return File.ReadAllLines(_pendingPath)
                    .Where(l =>
                    {
                        var parts = l.Split('|');
                        return parts.Length == 3
                            && DateTime.TryParse(parts[2], null, System.Globalization.DateTimeStyles.RoundtripKind, out var at)
                            && at >= cutoff;
                    })
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }
    }
}

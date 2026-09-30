using System;
using Outlook = Microsoft.Office.Interop.Outlook;

namespace EmailFilerv2
{
    /// <summary>
    /// Sender address and Internet Message-ID for a MailItem, in the same shape the
    /// WPF picker writes to KorEmailIndex (EmailParser: lowercase SMTP sender,
    /// Message-ID with its angle brackets).
    ///
    /// MailItem.SenderEmailAddress is NOT an SMTP address for internal senders: Outlook
    /// returns the Exchange legacy DN (/O=EXCHANGELABS/OU=.../CN=...). The SMTP address
    /// lives in PR_SENDER_SMTP_ADDRESS, or failing that on the Exchange user.
    ///
    /// Every lookup is best-effort. If nothing resolves, the caller gets exactly what the
    /// add-in stored before this class existed, so it can never be worse than that.
    /// </summary>
    internal static class OutlookMailIdentity
    {
        private const string PrSenderSmtpAddress = "http://schemas.microsoft.com/mapi/proptag/0x5D01001F";
        private const string PrInternetMessageId = "http://schemas.microsoft.com/mapi/proptag/0x1035001F";

        internal static string GetSenderEmail(Outlook.MailItem mail)
        {
            if (mail == null)
                return null;

            string rawAddress = null;
            string addressType = null;
            try { rawAddress = mail.SenderEmailAddress; } catch { }
            try { addressType = mail.SenderEmailType; } catch { }

            // External senders: SenderEmailAddress already is the SMTP address.
            if (string.Equals(addressType, "SMTP", StringComparison.OrdinalIgnoreCase) && LooksLikeSmtp(rawAddress))
                return NormalizeSmtp(rawAddress);

            string fromProperty = TryGetStringProperty(mail, PrSenderSmtpAddress);
            if (LooksLikeSmtp(fromProperty))
                return NormalizeSmtp(fromProperty);

            try
            {
                var sender = mail.Sender;
                if (sender != null)
                {
                    var exUser = sender.GetExchangeUser();
                    if (exUser != null && LooksLikeSmtp(exUser.PrimarySmtpAddress))
                        return NormalizeSmtp(exUser.PrimarySmtpAddress);
                }
            }
            catch
            {
                // Address book unavailable (offline, quitting) - fall through.
            }

            if (LooksLikeSmtp(rawAddress))
                return NormalizeSmtp(rawAddress);

            // Unresolvable: keep the previous behaviour (the raw value, DN or not).
            return rawAddress;
        }

        internal static string GetInternetMessageId(Outlook.MailItem mail)
        {
            if (mail == null)
                return null;

            string id = TryGetStringProperty(mail, PrInternetMessageId);
            if (string.IsNullOrWhiteSpace(id))
                return null;

            id = id.Trim();
            return id.Length > 512 ? id.Substring(0, 512) : id;
        }

        private static string TryGetStringProperty(Outlook.MailItem mail, string schemaName)
        {
            try
            {
                var accessor = mail.PropertyAccessor;
                return accessor == null ? null : accessor.GetProperty(schemaName) as string;
            }
            catch
            {
                // GetProperty throws when the property is not set on the item.
                return null;
            }
        }

        private static bool LooksLikeSmtp(string address)
        {
            return !string.IsNullOrWhiteSpace(address)
                && address.IndexOf('@') > 0
                && !address.TrimStart().StartsWith("/", StringComparison.Ordinal);
        }

        private static string NormalizeSmtp(string address)
        {
            return address.Trim().ToLowerInvariant();
        }
    }
}

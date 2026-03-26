#nullable enable
#pragma warning disable SA1649
using System;
using System.Collections.Generic;
namespace Kor.Operations.Core
{
    /// <summary>
    /// Describes the values required to compose a Graph-backed transmittal email.
    /// </summary>
    public interface IGraphMailHeader
    {
        /// <summary>
        /// Gets the transmittal identifier.
        /// </summary>
        string TransmittalNo { get; }

        /// <summary>
        /// Gets the project number associated with the message.
        /// </summary>
        string ProjectNumber { get; }

        /// <summary>
        /// Gets the project name associated with the message.
        /// </summary>
        string ProjectName { get; }

        /// <summary>
        /// Gets the message subject.
        /// </summary>
        string? Subject { get; }

        /// <summary>
        /// Gets the business purpose shown in the email body.
        /// </summary>
        string? Purpose { get; }

        /// <summary>
        /// Gets the HTML or plain-text remarks content.
        /// </summary>
        string Remarks { get; }

        /// <summary>
        /// Gets the internal sharing link.
        /// </summary>
        string? InternalLink { get; }

        /// <summary>
        /// Gets the external sharing link when one was requested.
        /// </summary>
        string? ExternalLink { get; }

        /// <summary>
        /// Gets the recipients associated with the message.
        /// </summary>
        IReadOnlyList<Recipient> Recipients { get; }
    }

    /// <summary>
    /// Represents a transmittal and its delivery metadata.
    /// </summary>
    public sealed class Transmittal : IGraphMailHeader
    {
        // Identifiers / project
        /// <summary>
        /// Gets or sets the transmittal identifier.
        /// </summary>
        public string TransmittalNo { get; set; } = "";

        /// <summary>
        /// Gets or sets the related project number.
        /// </summary>
        public string ProjectNumber { get; set; } = "";

        /// <summary>
        /// Gets or sets the related project name.
        /// </summary>
        public string ProjectName { get; set; } = "";

        // Primary fields
        /// <summary>Subject shown on the cover sheet and used in email.</summary>
        public string? Subject { get; set; }

        /// <summary>Business “purpose” (e.g., For Review, For Record) – displayed separately from Subject.</summary>
        public string? Purpose { get; set; }

        /// <summary>Free-form message body (remarks) only.</summary>
        public string Remarks { get; set; } = "";

        // Timestamps
        /// <summary>Record creation time (UTC) for auditing.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Effective date of the transmittal (UTC). Null means “not set”; UI can display local with DST.</summary>
        public DateTime? DateUtc { get; set; }

        /// <summary>Optional due date (UTC).</summary>
        public DateTime? DueDateUtc { get; set; }

        // From / Sender
        /// <summary>
        /// Gets or sets the sender display name.
        /// </summary>
        public string? FromName { get; set; }

        /// <summary>
        /// Gets or sets the sender email address.
        /// </summary>
        public string? FromEmail { get; set; }

        // Kept for compatibility if other code still references these
        /// <summary>
        /// Gets or sets the legacy sender name value.
        /// </summary>
        public string SenderName { get; set; } = "";

        /// <summary>
        /// Gets or sets the legacy sender email value.
        /// </summary>
        public string SenderEmail { get; set; } = "";

        // Recipients
        /// <summary>
        /// Gets or sets the recipients for the transmittal.
        /// </summary>
        public List<Recipient> Recipients { get; set; } = new();
        IReadOnlyList<Recipient> IGraphMailHeader.Recipients => Recipients;

        /// <summary>Optional strong lists used by the renderer/UI (To and Cc split).</summary>
        public List<string>? ToRecipients { get; set; }
        public List<string>? CcRecipients { get; set; }

        // Transport / delivery
        /// <summary>"Email", "SharePoint", etc.</summary>
        public string? SendVia { get; set; }

        /// <summary>
        /// Gets or sets the SharePoint folder path for the transmittal.
        /// </summary>
        public string SharePointFolderPath { get; set; } = "";

        /// <summary>
        /// Gets or sets the external sharing link.
        /// </summary>
        public string? ExternalLink { get; set; }

        /// <summary>
        /// Gets or sets the internal sharing link.
        /// </summary>
        public string? InternalLink { get; set; }

        // Output
        /// <summary>
        /// Gets or sets the file name used for the generated cover sheet.
        /// </summary>
        public string CoverSheetFileName { get; set; } = "Transmittal.pdf";

        // Files included in the transmittal
        /// <summary>
        /// Gets or sets the files included in the transmittal.
        /// </summary>
        public List<TransmittalFile> Files { get; set; } = new();
    }

    /// <summary>
    /// Represents a note associated with a PDF bookmark.
    /// </summary>
    public sealed class BookmarkNote
    {
        /// <summary>
        /// Gets or sets the bookmark label.
        /// </summary>
        public string Bookmark { get; set; } = "";

        /// <summary>
        /// Gets or sets the note text for the bookmark.
        /// </summary>
        public string? Note { get; set; }
    }

    /// <summary>
    /// Represents a file included in a transmittal.
    /// </summary>
    public sealed class TransmittalFile
    {
        /// <summary>
        /// Gets or sets the local file path.
        /// </summary>
        public string LocalPath { get; set; } = "";

        /// <summary>
        /// Gets or sets the display file name.
        /// </summary>
        public string FileName { get; set; } = "";

        /// <summary>
        /// Gets or sets the file size in bytes.
        /// </summary>
        public long SizeBytes { get; set; }

        /// <summary>
        /// Gets or sets the SHA-256 hash of the file.
        /// </summary>
        public string? Sha256 { get; set; }

        /// <summary>
        /// Gets or sets the uploaded SharePoint path or URL.
        /// </summary>
        public string? SharePointPath { get; set; }

        /// <summary>
        /// Gets or sets the PDF bookmark titles found in the file.
        /// </summary>
        public List<string>? PdfBookmarks { get; set; }

        /// <summary>
        /// Gets or sets the bookmark notes captured for the file.
        /// </summary>
        public List<string> PdfBookmarkNotes { get; set; } = new();
    }

    /// <summary>
    /// Represents a message recipient.
    /// </summary>
    public sealed class Recipient
    {
        /// <summary>
        /// Gets or sets the recipient display name.
        /// </summary>
        public string DisplayName { get; set; } = "";

        /// <summary>
        /// Gets or sets the recipient email address.
        /// </summary>
        public string Email { get; set; } = "";

        /// <summary>
        /// Gets or sets a value indicating whether the recipient is external.
        /// </summary>
        public bool External { get; set; }

        // Optional flag some code paths may use to split To/Cc.
        /// <summary>
        /// Gets or sets a value indicating whether the recipient is copied rather than directly addressed.
        /// </summary>
        public bool IsCc { get; set; }
    }

    /// <summary>
    /// Tracks the state of the transmittal wizard workflow.
    /// </summary>
    public sealed class WizardState
    {
        /// <summary>
        /// Gets or sets the current wizard step.
        /// </summary>
        public int Step { get; set; }

        /// <summary>
        /// Gets the transmittal header being edited.
        /// </summary>
        public Transmittal Header { get; } = new();

        /// <summary>
        /// Gets the files associated with the current header.
        /// </summary>
        public List<TransmittalFile> Files => Header.Files;
    }

    /// <summary>
    /// Holds application configuration values used across the transmittal workflow.
    /// </summary>
    public sealed class AppConfig
    {
        /// <summary>
        /// Gets or sets the SharePoint site identifier.
        /// </summary>
        public string SharePointSiteId { get; set; } = "";

        /// <summary>
        /// Gets or sets the SharePoint drive identifier.
        /// </summary>
        public string SharePointDriveId { get; set; } = "";

        /// <summary>
        /// Gets or sets the local projects root folder.
        /// </summary>
        public string ProjectsRoot { get; set; } = "";

        /// <summary>
        /// Gets or sets the Deltek DSN name.
        /// </summary>
        public string DeltekDsn { get; set; } = "Deltek";

        /// <summary>
        /// Gets or sets the attachment size threshold in bytes.
        /// </summary>
        public long AttachThresholdBytes { get; set; } = 15L * 1024 * 1024;

        /// <summary>
        /// Gets or sets the default sharing-link expiry in days.
        /// </summary>
        public int LinkExpiryDays { get; set; } = 21;
    }
}

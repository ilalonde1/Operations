#nullable enable
using System;
using System.Collections.Generic;
namespace Kor.Operations.Core
{
    public interface IGraphMailHeader
    {
        string TransmittalNo { get; }
        string ProjectNumber { get; }
        string ProjectName { get; }
        string? Subject { get; }
        string? Purpose { get; }
        string Remarks { get; }
        string? InternalLink { get; }
        string? ExternalLink { get; }
        IReadOnlyList<Recipient> Recipients { get; }
    }

    public sealed class Transmittal : IGraphMailHeader
    {
        // Identifiers / project
        public string TransmittalNo { get; set; } = "";
        public string ProjectNumber { get; set; } = "";
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
        public string? FromName { get; set; }
        public string? FromEmail { get; set; }

        // Kept for compatibility if other code still references these
        public string SenderName { get; set; } = "";
        public string SenderEmail { get; set; } = "";

        // Recipients
        public List<Recipient> Recipients { get; set; } = new();
        IReadOnlyList<Recipient> IGraphMailHeader.Recipients => Recipients;

        /// <summary>Optional strong lists used by the renderer/UI (To and Cc split).</summary>
        public List<string>? ToRecipients { get; set; }
        public List<string>? CcRecipients { get; set; }

        // Transport / delivery
        /// <summary>"Email", "SharePoint", etc.</summary>
        public string? SendVia { get; set; }

        public string SharePointFolderPath { get; set; } = "";
        public string? ExternalLink { get; set; }
        public string? InternalLink { get; set; }

        // Output
        public string CoverSheetFileName { get; set; } = "Transmittal.pdf";

        // Files included in the transmittal
        public List<TransmittalFile> Files { get; set; } = new();
    }

    public sealed class BookmarkNote
    {
        public string Bookmark { get; set; } = "";
        public string? Note { get; set; }
    }
    public sealed class TransmittalFile
    {
        public string LocalPath { get; set; } = "";
        public string FileName { get; set; } = "";
        public long SizeBytes { get; set; }
        public string? Sha256 { get; set; }
        public string? SharePointPath { get; set; }
        public List<string>? PdfBookmarks { get; set; }
        public List<string> PdfBookmarkNotes { get; set; } = new();
    }

    public sealed class Recipient
    {
        public string DisplayName { get; set; } = "";
        public string Email { get; set; } = "";
        public bool External { get; set; }

        // Optional flag some code paths may use to split To/Cc.
        public bool IsCc { get; set; }
    }

    public sealed class WizardState
    {
        public int Step { get; set; }
        public Transmittal Header { get; } = new();
        public List<TransmittalFile> Files => Header.Files;
    }

    public sealed class AppConfig
    {
        public string SharePointSiteId { get; set; } = "";
        public string SharePointDriveId { get; set; } = "";
        public string ProjectsRoot { get; set; } = "";
        public string DeltekDsn { get; set; } = "Deltek";
        public long AttachThresholdBytes { get; set; } = 15L * 1024 * 1024;
        public int LinkExpiryDays { get; set; } = 21;
    }
}

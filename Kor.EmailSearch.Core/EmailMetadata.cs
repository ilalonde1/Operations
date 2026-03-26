#nullable enable
using System;

namespace Kor.EmailSearch.Core;

/// <summary>
/// Minimal metadata needed to populate KorEmailIndex.dbo.Emails
/// for either .msg or .eml files.
/// </summary>
public sealed class EmailMetadata
{
    public string ProjectNumber { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// "MSG" or "EML" (if empty, will be inferred from file extension).
    /// </summary>
    public string Format { get; set; } = string.Empty;

    public string? MessageId { get; set; }

    public string? Subject { get; set; }

    public string? FromDisplay { get; set; }

    public string? FromEmail { get; set; }

    public string? ToList { get; set; }

    public string? CcList { get; set; }

    public string? BccList { get; set; }

    public DateTime? SentOnUtc { get; set; }

    public DateTime? ReceivedOnUtc { get; set; }

    public string? BodyText { get; set; }

    public bool HasAttachments { get; set; }

    public int AttachmentCount { get; set; }
}

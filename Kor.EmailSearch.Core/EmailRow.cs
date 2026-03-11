namespace Kor.EmailSearch.Core;

public sealed class EmailRow
{
    public int EmailId { get; set; }
    public string ProjectNumber { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public System.DateTime? SentOnUtc { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public int AttachmentCount { get; set; }
    public bool HasAttachments { get; set; }
}

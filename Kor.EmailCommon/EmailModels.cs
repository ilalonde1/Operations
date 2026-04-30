namespace Kor.EmailCommon;

public sealed record ParsedEmail(
    string? Subject,
    string? FromDisplay,
    string? FromEmail,
    string ToList,
    string CcList,
    string BccList,
    DateTime? SentOnUtc,
    DateTime? ReceivedOnUtc,
    string BodyText,
    int AttachmentCount,
    bool HasAttachments,
    string? MessageId = null
);

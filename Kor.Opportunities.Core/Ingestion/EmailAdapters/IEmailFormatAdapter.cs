#nullable enable
using System;
using Kor.Opportunities.Core.Ingestion;

namespace Kor.Opportunities.Core.Ingestion.EmailAdapters;

/// <summary>
/// Parses one sender's email-alert format into an OpportunityCandidate.
/// Implementations register via DI; the registry dispatches by matching
/// CanHandle against the message's sender address.
/// </summary>
public interface IEmailFormatAdapter
{
    /// <summary>Stable name for logging + diagnostics (e.g. "Generic", "Apc", "Biddingo").</summary>
    string AdapterName { get; }

    /// <summary>
    /// Returns true if this adapter handles emails from <paramref name="senderAddress"/>.
    /// Case-insensitive. The GenericEmailFormatAdapter MUST return false here - it is invoked
    /// explicitly as the fallback by the registry, never via CanHandle dispatch.
    /// </summary>
    bool CanHandle(string senderAddress);

    /// <summary>
    /// Parse a Graph message into an OpportunityCandidate. Return null if unparseable
    /// (URL not findable, required fields missing). The provider will leave unparseable messages
    /// unread for a retry on the next polling cycle.
    /// </summary>
    OpportunityCandidate? Parse(EmailMessage message);
}

/// <summary>
/// Slim DTO so the adapter interface lives in Core (no Microsoft.Graph dependency in Core).
/// GraphEmailOpportunityProvider in the Data project maps Microsoft.Graph.Models.Message to this DTO
/// before invoking adapters.
/// </summary>
public sealed record EmailMessage(
    string MessageId,
    string SenderAddress,
    string? Subject,
    string? BodyHtmlOrPlain,
    DateTimeOffset? ReceivedUtc);

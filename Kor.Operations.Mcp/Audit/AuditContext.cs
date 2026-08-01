#nullable enable
using System;
using System.Threading;

namespace Kor.Operations.Mcp.Audit;

/// <summary>
/// Per-request audit correlation, propagated through async/await via
/// AsyncLocal&lt;T&gt;. AskService sets these at the start of each /ask;
/// AuditLogger.WriteAsync auto-fills any nulls on an AuditEntry from
/// here. The result: every audit row written during an /ask call -
/// including all tool rows - carries the same UserUpn + ClientApp, so
/// the smoke harness can correlate per-request even under concurrent live
/// traffic. ConversationKey is propagated for downstream tool calls and
/// future correlation use, but is not currently persisted to Mcp.AuditLog.
/// (Batch 93b.)
/// </summary>
public static class AuditContext
{
    private static readonly AsyncLocal<string?> _userUpn = new();
    private static readonly AsyncLocal<string?> _clientApp = new();
    private static readonly AsyncLocal<Guid?> _conversationKey = new();

    public static string? UserUpn { get => _userUpn.Value; set => _userUpn.Value = value; }
    public static string? ClientApp { get => _clientApp.Value; set => _clientApp.Value = value; }
    public static Guid? ConversationKey { get => _conversationKey.Value; set => _conversationKey.Value = value; }
}

#nullable enable
using System.Collections.Generic;
using Kor.Operations.Services;

namespace Kor.Operations.Controls;

internal static class AiConversationHistory
{
    internal static string ApplyAssistantResult(
        IList<(string Role, string Content)> history,
        AppAiResult result)
    {
        if (!result.IsSuccess)
        {
            return result.ErrorMessage ?? "AI request failed. Try again.";
        }

        history.Add(("assistant", result.Text));
        return result.Text;
    }
}

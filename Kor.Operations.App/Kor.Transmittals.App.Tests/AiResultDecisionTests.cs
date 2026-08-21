#nullable enable
using System.Collections.Generic;
using System;
using System.IO;
using Kor.Operations.Controls;
using Kor.Operations.Services;
using Xunit;

namespace Kor.Operations.App.Tests;

public sealed class AiResultDecisionTests
{
    [Fact]
    public void Failure_result_cannot_be_read_as_successful_answer()
    {
        var result = AppAiResult.Failure("Unable to reach AI service: No such host is known.");

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Text);
        Assert.Equal("Unable to reach AI service: No such host is known.", result.ErrorMessage);
    }

    [Fact]
    public void Failed_ai_response_is_not_appended_to_conversation_history()
    {
        var history = new List<(string Role, string Content)>
        {
            ("user", "What should I do next?"),
        };

        var rendered = AiConversationHistory.ApplyAssistantResult(
            history,
            AppAiResult.Failure("AI service returned HTTP 503."));

        Assert.Equal("AI service returned HTTP 503.", rendered);
        Assert.Single(history);
        Assert.DoesNotContain(history, turn => turn.Role == "assistant");
    }

    [Fact]
    public void AiQueryPanel_uses_result_classifier_instead_of_unconditional_assistant_append()
    {
        var source = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "Kor.Operations.App",
            "Controls",
            "AiQueryPanel.xaml.cs"));

        Assert.Contains("AiConversationHistory.ApplyAssistantResult(_history, result)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_history.Add((\"assistant\", response));", source, StringComparison.Ordinal);
    }

    private static string GetRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Kor.Operations.App")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
    }
}

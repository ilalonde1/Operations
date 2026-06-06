#nullable enable
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Kor.Opportunities.Worker.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AnthropicTool = Anthropic.SDK.Common.Tool;

namespace Kor.Opportunities.Worker.Services.Research;

public sealed class AnthropicResearchExecutorService : IResearchExecutorService
{
    // Fallback structured-output schema used when a ResearchTarget does
    // not supply its own. Matches the org/FirmNarrative shape consumed
    // by CanonicalSchemaExtractor("FirmNarrative") and the dossier UI.
    // Project research (R91b) passes its own schema via
    // ResearchTarget.StructuredOutputJsonSchema and never hits this.
    private const string OrgFirmNarrativeSchema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""displayName"": { ""type"": ""string"" },
    ""kind"": { ""type"": ""string"" },
    ""_providerName"": { ""type"": ""string"" },
    ""_generatedAt"": { ""type"": ""string"" },
    ""_confidence"": { ""type"": ""string"" },
    ""decisionMakers"": { ""type"": ""array"", ""items"": {
      ""type"": ""object"",
      ""properties"": {
        ""name"": { ""type"": ""string"" },
        ""title"": { ""type"": [""string"", ""null""] },
        ""email"": { ""type"": [""string"", ""null""] },
        ""phone"": { ""type"": [""string"", ""null""] },
        ""linkedinUrl"": { ""type"": [""string"", ""null""] },
        ""notes"": { ""type"": [""string"", ""null""] }
      }
    }},
    ""signals"": { ""type"": ""array"", ""items"": {
      ""type"": ""object"",
      ""properties"": {
        ""signalType"": { ""type"": ""string"" },
        ""subject"": { ""type"": ""string"" },
        ""detail"": { ""type"": [""string"", ""null""] },
        ""occurredAtApprox"": { ""type"": [""string"", ""null""] },
        ""sourceUrl"": { ""type"": [""string"", ""null""] }
      }
    }},
    ""actions"": { ""type"": ""array"", ""items"": {
      ""type"": ""object"",
      ""properties"": {
        ""actionType"": { ""type"": ""string"" },
        ""recommendation"": { ""type"": ""string"" },
        ""targetPersonName"": { ""type"": [""string"", ""null""] },
        ""timingNotes"": { ""type"": [""string"", ""null""] }
      }
    }},
    ""works"": { ""type"": ""array"", ""items"": {
      ""type"": ""object"",
      ""properties"": {
        ""projectName"": { ""type"": ""string"" },
        ""role"": { ""type"": [""string"", ""null""] },
        ""yearApprox"": { ""type"": [""string"", ""null""] },
        ""estimatedValueCad"": { ""type"": [""number"", ""null""] },
        ""estimatedValueText"": { ""type"": [""string"", ""null""] },
        ""notes"": { ""type"": [""string"", ""null""] }
      }
    }},
    ""risks"": {
      ""type"": ""array"",
      ""description"": ""Flag risks the org carries (capacity strain on their preferred SE partner, key-person dependency, ownership uncertainty, exploitable competitor weakness). Leave empty only if no risks are evident from research."",
      ""items"": {
      ""type"": ""object"",
      ""properties"": {
        ""riskType"": { ""type"": ""string"" },
        ""description"": { ""type"": ""string"" },
        ""mitigationNotes"": { ""type"": [""string"", ""null""] }
      }
    }},
    ""narratives"": {
      ""type"": ""array"",
      ""minItems"": 2,
      ""description"": ""Always include at least 2 narratives: one with narrativeType=Current summarizing the org's current state in 2-4 sentences, and one with narrativeType=Action summarizing what KOR should do this quarter in 2-4 sentences. May include additional History or Summary narratives."",
      ""items"": {
        ""type"": ""object"",
        ""properties"": {
          ""narrativeType"": { ""type"": ""string"", ""enum"": [""Current"", ""History"", ""Action"", ""Summary""] },
          ""paragraphText"": { ""type"": ""string"" }
        },
        ""required"": [""narrativeType"", ""paragraphText""]
      }
    }
  },
  ""required"": [""displayName"", ""kind"", ""_providerName"", ""_generatedAt"", ""_confidence"", ""narratives""]
}";

    private readonly BdResearchExecutorOptions _options;
    private readonly ILogger<AnthropicResearchExecutorService> _logger;

    public AnthropicResearchExecutorService(
        IOptions<BdResearchExecutorOptions> options,
        ILogger<AnthropicResearchExecutorService> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ExecutedResearch?> ExecuteAsync(ResearchTarget target, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning(
                "BD research execution skipped for org {CanonicalOrgId}/{ProviderName}: Anthropic API key is not configured.",
                target.CanonicalOrgId,
                target.ProviderName);
            return null;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            // Sonnet's web_search tool loop typically runs 1-4 minutes per org
            // depending on how many searches the model decides to do. The
            // default HttpClient timeout (100s) is too short — bump to 10 min.
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            using var client = new AnthropicClient(new APIAuthentication(apiKey), httpClient);

            var (researchText, p1In, p1Out, p1Tools) = await SearchPhaseAsync(client, target, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(researchText))
            {
                _logger.LogWarning(
                    "Phase-1 search returned no text for org {CanonicalOrgId}/{ProviderName}.",
                    target.CanonicalOrgId,
                    target.ProviderName);
                return null;
            }

            var (json, p2In, p2Out) = await FormatPhaseAsync(client, target, researchText, ct).ConfigureAwait(false);
            sw.Stop();
            if (string.IsNullOrWhiteSpace(json))
            {
                _logger.LogWarning(
                    "Phase-2 format returned no JSON for org {CanonicalOrgId}/{ProviderName}.",
                    target.CanonicalOrgId,
                    target.ProviderName);
                return null;
            }

            _logger.LogInformation(
                "BD research execution succeeded for org {CanonicalOrgId}/{ProviderName}. SearchInTok={SearchInTok}, SearchOutTok={SearchOutTok}, ToolCalls={ToolCalls}, FormatInTok={FormatInTok}, FormatOutTok={FormatOutTok}, Elapsed={Elapsed}.",
                target.CanonicalOrgId,
                target.ProviderName,
                p1In,
                p1Out,
                p1Tools,
                p2In,
                p2Out,
                sw.Elapsed);

            return new ExecutedResearch(
                target.CanonicalOrgId,
                target.ProviderName,
                json,
                p1In + p2In,
                p1Out + p2Out,
                p1Tools,
                sw.Elapsed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            _logger.LogWarning(
                ex,
                "BD research execution failed for org {CanonicalOrgId}/{ProviderName} after {Elapsed}.",
                target.CanonicalOrgId,
                target.ProviderName,
                sw.Elapsed);
            return null;
        }
    }

    private async Task<(string Text, long InTok, long OutTok, int ToolCalls)> SearchPhaseAsync(
        AnthropicClient client,
        ResearchTarget target,
        CancellationToken ct)
    {
        var messages = new List<Message>
        {
            new(RoleType.User, target.UserPromptOverride),
        };

        var parameters = new MessageParameters
        {
            Model = _options.Model,
            MaxTokens = 16000,
            Stream = false,
            System = new List<SystemMessage>
            {
                new(target.SystemPromptOverride),
            },
            Messages = messages,
            Tools = new List<AnthropicTool>
            {
                // Anthropic.SDK 5.5.1 exposes server-side Web Search through
                // Messaging.ServerTools. The search executes inside Anthropic.
                ServerTools.GetWebSearchTool(15, null, null, null),
            },
            ToolChoice = new ToolChoice
            {
                Type = ToolChoiceType.Auto,
            },
        };

        ct.ThrowIfCancellationRequested();
        var response = await client.Messages.GetClaudeMessageAsync(parameters, ct).ConfigureAwait(false);
        var sb = new StringBuilder();
        foreach (var text in response.Content.OfType<TextContent>())
        {
            sb.Append(text.Text);
        }

        return (
            sb.ToString(),
            response.Usage?.InputTokens ?? 0,
            response.Usage?.OutputTokens ?? 0,
            CountToolUseBlocks(response));
    }

    private async Task<(string Json, long InTok, long OutTok)> FormatPhaseAsync(
        AnthropicClient client,
        ResearchTarget target,
        string researchText,
        CancellationToken ct)
    {
        const string submitToolName = "submit_research";
        const string submitToolDescription = "Submit the structured research findings for this organization.";
        var submitToolSchema = target.StructuredOutputJsonSchema ?? OrgFirmNarrativeSchema;

        var includeSentence = target.StructuredOutputFormatInstruction
            ?? "Include all decisionMakers, signals, actions, works, risks, and narratives found in the research.";
        var formatSystemPrompt =
            "You are a JSON formatter. Convert the research findings below into a single submit_research tool call. " +
            "Do not perform additional research. Do not emit text. Call the submit_research tool exactly once with the structured arguments. " +
            "Omit fields where you have no information. " + includeSentence;

        var formatUserPrompt =
            $"Research findings for {target.OrgDisplayName}:{Environment.NewLine}{Environment.NewLine}{researchText}{Environment.NewLine}{Environment.NewLine}" +
            $"Call submit_research with the structured output for provider {target.ProviderName}, generated date {DateTime.UtcNow:yyyy-MM-dd}.";

        var submitResearchTool = new AnthropicTool(
            new Anthropic.SDK.Common.Function(submitToolName, submitToolDescription, submitToolSchema));
        var messages = new List<Message>
        {
            new(RoleType.User, formatUserPrompt),
        };

        var parameters = new MessageParameters
        {
            Model = _options.Model,
            MaxTokens = _options.MaxOutputTokens,
            Stream = false,
            System = new List<SystemMessage>
            {
                new(formatSystemPrompt),
            },
            Messages = messages,
            Tools = new List<AnthropicTool>
            {
                submitResearchTool,
            },
            ToolChoice = new ToolChoice
            {
                Type = ToolChoiceType.Tool,
                Name = submitToolName,
            },
        };

        ct.ThrowIfCancellationRequested();
        var response = await client.Messages.GetClaudeMessageAsync(parameters, ct).ConfigureAwait(false);
        var toolUse = response.Content.OfType<ToolUseContent>().FirstOrDefault(t => t.Name == submitToolName);
        if (toolUse is null)
        {
            _logger.LogWarning(
                "Format phase did not return submit_research tool call for org {CanonicalOrgId}/{ProviderName}.",
                target.CanonicalOrgId,
                target.ProviderName);
            return ("", response.Usage?.InputTokens ?? 0, response.Usage?.OutputTokens ?? 0);
        }

        var json = JsonSerializer.Serialize(toolUse.Input);
        return (json, response.Usage?.InputTokens ?? 0, response.Usage?.OutputTokens ?? 0);
    }

    private string? ResolveApiKey()
    {
        // Check standard Anthropic SDK env var first, then KOR-specific name
        // (KOR_ANTHROPIC_KEY is the existing Machine-scope env var on both dev
        // and KOR-APP01 used by other Worker services), then options fallback.
        var env = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        env = Environment.GetEnvironmentVariable("KOR_ANTHROPIC_KEY");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        return _options.ApiKey;
    }

    private static int CountToolUseBlocks(MessageResponse response)
    {
        return response.Content.Count(c => c is ToolUseContent or ServerToolUseContent);
    }
}

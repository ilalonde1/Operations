#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Ingestion.EmailAdapters;
using Kor.Opportunities.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.Messages.Item.Move;

namespace Kor.Opportunities.Data.Ingestion.Providers;

public sealed record GraphEmailRuntimeOptions(
    string TenantId,
    string ClientId,
    string ClientSecret,
    string UserEmail,
    string MailFolderName,
    string ProcessedFolderName,
    bool MarkAsReadInsteadOfMove,
    int MaxEmailsPerRun,
    bool SmokeTestMode);

public sealed class GraphEmailOpportunityProvider : IOpportunityProvider
{
    private const int ProcessedMessageIdsMaxSize = 10_000;
    private static readonly string[] GraphScopes = ["https://graph.microsoft.com/.default"];
    private static readonly ConcurrentDictionary<string, DateTimeOffset> ProcessedMessageIds = new(StringComparer.Ordinal);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Func<GraphEmailRuntimeOptions> _optionsAccessor;
    private readonly EmailFormatAdapterRegistry _registry;
    private readonly ILogger<GraphEmailOpportunityProvider> _logger;

    public GraphEmailOpportunityProvider(
        IHttpClientFactory httpClientFactory,
        Func<GraphEmailRuntimeOptions> optionsAccessor,
        EmailFormatAdapterRegistry registry,
        ILogger<GraphEmailOpportunityProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _optionsAccessor = optionsAccessor;
        _registry = registry;
        _logger = logger;
    }

    public OpportunitySourceType SourceType => OpportunitySourceType.GraphEmail;

    public async Task<IReadOnlyList<OpportunityCandidate>> FetchAsync(
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig,
        CancellationToken ct)
    {
        var options = _optionsAccessor();
        if (string.IsNullOrWhiteSpace(options.TenantId)
            || string.IsNullOrWhiteSpace(options.ClientId)
            || string.IsNullOrWhiteSpace(options.ClientSecret)
            || string.IsNullOrWhiteSpace(options.UserEmail))
        {
            _logger.LogWarning("GraphEmail not configured; skipping.");
            return Array.Empty<OpportunityCandidate>();
        }

        var client = BuildClient(options);

        var folderId = await ResolveFolderIdAsync(client, options, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(folderId))
        {
            _logger.LogWarning(
                "GraphEmail provider {SourceName} could not find folder {FolderName} for user {UserEmail}.",
                source.Name,
                options.MailFolderName,
                options.UserEmail);
            return Array.Empty<OpportunityCandidate>();
        }

        var maxEmails = Math.Clamp(options.MaxEmailsPerRun, 1, 500);
        var messagesPage = await client.Users[options.UserEmail].MailFolders[folderId].Messages.GetAsync(request =>
        {
            request.QueryParameters.Filter = "isRead eq false";
            request.QueryParameters.Top = maxEmails;
            request.QueryParameters.Orderby = ["receivedDateTime desc"];
            request.QueryParameters.Select = ["id", "subject", "body", "from", "receivedDateTime", "isRead"];
        }, ct).ConfigureAwait(false);

        var candidates = new List<OpportunityCandidate>();
        var fetchedCount = 0;
        var parsedCount = 0;
        var skippedCount = 0;
        var failedCount = 0;
        var adaptersUsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? processedFolderId = null;
        TrimProcessedMessageIdsIfNeeded();

        foreach (var message in messagesPage?.Value ?? [])
        {
            ct.ThrowIfCancellationRequested();
            fetchedCount++;

            if (string.IsNullOrWhiteSpace(message.Id))
            {
                skippedCount++;
                continue;
            }

            if (ProcessedMessageIds.ContainsKey(message.Id))
            {
                skippedCount++;
                continue;
            }

            try
            {
                var senderAddress = message.From?.EmailAddress?.Address ?? string.Empty;
                var emailMessage = new EmailMessage(
                    message.Id,
                    senderAddress,
                    message.Subject,
                    message.Body?.Content,
                    message.ReceivedDateTime);

                var adapter = _registry.Resolve(senderAddress);
                adaptersUsed.Add(adapter.AdapterName);

                var candidate = adapter.Parse(emailMessage);
                if (candidate is null)
                {
                    skippedCount++;
                    continue;
                }

                var messageId = message.Id!;
                if (!options.SmokeTestMode)
                {
                    candidate = candidate with
                    {
                        OnPersistedAsync = async ackCt =>
                        {
                            await MarkMessageReadOrMoveAsync(
                                client,
                                options,
                                messageId,
                                async innerCt =>
                                {
                                    processedFolderId ??= await ResolveOrCreateFolderIdAsync(client, options, innerCt)
                                        .ConfigureAwait(false);
                                    return processedFolderId;
                                },
                                ackCt).ConfigureAwait(false);
                            ProcessedMessageIds[messageId] = DateTimeOffset.UtcNow;
                        },
                    };
                }

                candidates.Add(candidate);
                parsedCount++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failedCount++;
                _logger.LogError(
                    ex,
                    "GraphEmail provider {SourceName} failed processing message {MessageId}.",
                    source.Name,
                    message.Id);
            }
        }

        _logger.LogInformation(
            "GraphEmail provider {SourceName}: {Kept} candidate(s) from {Fetched} message(s) (parsed={Parsed}, skipped={Skipped}, failed={Failed}, smokeTest={SmokeTestMode}, adaptersUsed={AdapterNamesCsv}).",
            source.Name,
            candidates.Count,
            fetchedCount,
            parsedCount,
            skippedCount,
            failedCount,
            options.SmokeTestMode,
            string.Join(",", adaptersUsed.Order(StringComparer.OrdinalIgnoreCase)));

        return candidates;
    }

    private static void TrimProcessedMessageIdsIfNeeded()
    {
        if (ProcessedMessageIds.Count <= ProcessedMessageIdsMaxSize)
        {
            return;
        }

        var threshold = ProcessedMessageIds
            .OrderBy(kv => kv.Value)
            .Skip((int)(ProcessedMessageIdsMaxSize * 0.2))
            .First()
            .Value;
        var stale = ProcessedMessageIds
            .Where(kv => kv.Value < threshold)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in stale)
        {
            ProcessedMessageIds.TryRemove(key, out _);
        }
    }

    private GraphServiceClient BuildClient(GraphEmailRuntimeOptions options)
    {
        TokenCredential credential = new ClientSecretCredential(options.TenantId, options.ClientId, options.ClientSecret);
        var httpClient = _httpClientFactory.CreateClient(nameof(GraphEmailOpportunityProvider));
        return new GraphServiceClient(httpClient, credential, GraphScopes);
    }

    private static async Task<string?> ResolveFolderIdAsync(
        GraphServiceClient client,
        GraphEmailRuntimeOptions options,
        CancellationToken cancellationToken)
    {
        var folderName = string.IsNullOrWhiteSpace(options.MailFolderName) ? "Inbox" : options.MailFolderName.Trim();
        var escaped = folderName.Replace("'", "''", StringComparison.Ordinal);
        var folders = await client.Users[options.UserEmail].MailFolders.GetAsync(request =>
        {
            request.QueryParameters.Filter = $"displayName eq '{escaped}'";
            request.QueryParameters.Top = 5;
            request.QueryParameters.Select = ["id", "displayName"];
        }, cancellationToken).ConfigureAwait(false);

        return folders?.Value?
            .FirstOrDefault(x => string.Equals(x.DisplayName, folderName, StringComparison.OrdinalIgnoreCase))?
            .Id;
    }

    private static async Task<string?> ResolveFolderIdByNameAsync(
        GraphServiceClient client,
        string userEmail,
        string folderName,
        CancellationToken cancellationToken)
    {
        var escaped = folderName.Replace("'", "''", StringComparison.Ordinal);
        var folders = await client.Users[userEmail].MailFolders.GetAsync(request =>
        {
            request.QueryParameters.Filter = $"displayName eq '{escaped}'";
            request.QueryParameters.Top = 5;
            request.QueryParameters.Select = ["id", "displayName"];
        }, cancellationToken).ConfigureAwait(false);

        return folders?.Value?
            .FirstOrDefault(x => string.Equals(x.DisplayName, folderName, StringComparison.OrdinalIgnoreCase))?
            .Id;
    }

    private async Task<string?> ResolveOrCreateFolderIdAsync(
        GraphServiceClient client,
        GraphEmailRuntimeOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var folderName = string.IsNullOrWhiteSpace(options.ProcessedFolderName)
            ? "Processed-OpportunityAlerts"
            : options.ProcessedFolderName.Trim();

        var existingFolderId = await ResolveFolderIdByNameAsync(client, options.UserEmail, folderName, cancellationToken)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(existingFolderId))
        {
            return existingFolderId;
        }

        try
        {
            var created = await client.Users[options.UserEmail].MailFolders.PostAsync(
                new MailFolder
                {
                    DisplayName = folderName,
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return created?.Id;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "GraphEmail provider could not create folder {FolderName}.",
                folderName);
            return null;
        }
    }

    private async Task<bool> TryMarkReadAsync(
        GraphServiceClient client,
        string userEmail,
        string messageId,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.Users[userEmail].Messages[messageId].PatchAsync(
                new Message { IsRead = true },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "GraphEmail provider could not mark message {MessageId} as read.",
                messageId);
            return false;
        }
    }

    private async Task MarkMessageReadOrMoveAsync(
        GraphServiceClient client,
        GraphEmailRuntimeOptions options,
        string messageId,
        Func<CancellationToken, Task<string?>> resolveProcessedFolderIdAsync,
        CancellationToken cancellationToken)
    {
        if (options.MarkAsReadInsteadOfMove)
        {
            if (await TryMarkReadAsync(client, options.UserEmail, messageId, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            throw new InvalidOperationException($"GraphEmail could not mark message {messageId} as read.");
        }

        var processedFolderId = await resolveProcessedFolderIdAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(processedFolderId))
        {
            throw new InvalidOperationException($"GraphEmail could not resolve processed folder for message {messageId}.");
        }

        if (await TryMoveAsync(client, options.UserEmail, messageId, processedFolderId, cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        throw new InvalidOperationException($"GraphEmail could not move message {messageId} to processed folder.");
    }

    private async Task<bool> TryMoveAsync(
        GraphServiceClient client,
        string userEmail,
        string messageId,
        string destinationFolderId,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.Users[userEmail].Messages[messageId].Move.PostAsync(
                new MovePostRequestBody
                {
                    DestinationId = destinationFolderId,
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "GraphEmail provider could not move message {MessageId} to folder {FolderId}.",
                messageId,
                destinationFolderId);
            return false;
        }
    }
}

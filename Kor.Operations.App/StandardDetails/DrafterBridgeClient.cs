#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Kor.Operations.StandardDetails;

internal sealed record BridgeReply(
    string Id,
    string Verb,
    bool Ok,
    JsonElement Result,
    string? Error,
    string? ActiveDoc,
    IReadOnlyList<string> Dialogs);

internal sealed class DrafterBridgeClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _bridgeRoot;

    internal DrafterBridgeClient(string bridgeRoot)
    {
        _bridgeRoot = string.IsNullOrWhiteSpace(bridgeRoot)
            ? throw new ArgumentException("Bridge root is required.", nameof(bridgeRoot))
            : bridgeRoot.Trim();
    }

    internal async Task<BridgeReply> SendAsync(object command, TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Bridge timeout must be greater than zero.");
        }

        var id = $"ops-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
        var inbox = Path.Combine(_bridgeRoot, "inbox");
        var outbox = Path.Combine(_bridgeRoot, "outbox");
        var stagedPath = Path.Combine(inbox, $"{id}.tmp");
        var commandPath = Path.Combine(inbox, $"{id}.json");
        var replyPath = Path.Combine(outbox, $"{id}.json");

        Directory.CreateDirectory(inbox);
        Directory.CreateDirectory(outbox);

        var payload = JsonSerializer.SerializeToNode(command, JsonOptions) as JsonObject
            ?? throw new InvalidOperationException("Bridge command must serialize to a JSON object.");

        payload["id"] = id;
        var verb = payload.TryGetPropertyValue("verb", out var verbNode)
            ? verbNode?.GetValue<string>() ?? ""
            : "";

        try
        {
            await File.WriteAllTextAsync(stagedPath, payload.ToJsonString(JsonOptions));
            File.Move(stagedPath, commandPath);
        }
        catch (Exception ex)
        {
            TryDelete(stagedPath);
            throw new InvalidOperationException($"Could not queue Drafter bridge command '{verb}' in '{inbox}'.", ex);
        }

        var deadline = DateTime.UtcNow.Add(timeout);
        Exception? lastReadError = null;
        while (DateTime.UtcNow < deadline)
        {
            if (!File.Exists(replyPath))
            {
                await Task.Delay(250);
                continue;
            }

            try
            {
                var json = await File.ReadAllTextAsync(replyPath);
                var reply = ParseReply(json, id, verb);
                if (!string.Equals(reply.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Drafter bridge reply id '{reply.Id}' did not match command id '{id}'.");
                }

                if (!reply.Ok)
                {
                    var message = string.IsNullOrWhiteSpace(reply.Error)
                        ? $"Drafter bridge command '{verb}' failed."
                        : reply.Error!;
                    throw new InvalidOperationException(message);
                }

                return reply;
            }
            catch (IOException ex)
            {
                lastReadError = ex;
            }
            catch (JsonException ex)
            {
                lastReadError = ex;
            }

            await Task.Delay(250);
        }

        var detail = lastReadError is null ? "" : $" Last read error: {lastReadError.Message}";
        throw new TimeoutException($"Timed out waiting {timeout:g} for Drafter bridge command '{verb}' reply at '{replyPath}'.{detail}");
    }

    private static BridgeReply ParseReply(string json, string expectedId, string fallbackVerb)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var id = TryGetString(root, "id") ?? expectedId;
        var verb = TryGetString(root, "verb") ?? fallbackVerb;
        var ok = TryGetBool(root, "ok");
        var error = TryGetString(root, "error");
        var activeDoc = TryGetString(root, "activeDoc");
        var result = TryGetProperty(root, "result", out var resultElement)
            ? resultElement.Clone()
            : default;
        var dialogs = TryGetProperty(root, "dialogs", out var dialogsElement)
            ? ReadDialogs(dialogsElement)
            : Array.Empty<string>();

        return new BridgeReply(id, verb, ok, result, error, activeDoc, dialogs);
    }

    private static IReadOnlyList<string> ReadDialogs(JsonElement dialogsElement)
    {
        if (dialogsElement.ValueKind != JsonValueKind.Array)
        {
            return dialogsElement.ValueKind == JsonValueKind.String
                ? new[] { dialogsElement.GetString() ?? "" }
                : Array.Empty<string>();
        }

        var dialogs = new List<string>();
        foreach (var item in dialogsElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                dialogs.Add(item.GetString() ?? "");
            }
            else if (TryGetString(item, "message") is { } message)
            {
                dialogs.Add(message);
            }
            else if (TryGetString(item, "text") is { } text)
            {
                dialogs.Add(text);
            }
            else
            {
                dialogs.Add(item.GetRawText());
            }
        }

        return dialogs;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? TryGetString(JsonElement element, string name)
        => TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryGetBool(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => false
        };
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup only; the original queueing error is more useful.
        }
    }
}

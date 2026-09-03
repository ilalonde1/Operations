#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Kor.Operations.StandardDetails;

internal sealed record StandardDetailsMasterPublishOptions(
    string AuthoringPath,
    string MasterPath,
    string BridgeRoot,
    string PreviewCachePath = "")
{
    internal bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AuthoringPath)
        && !string.IsNullOrWhiteSpace(MasterPath)
        && !string.IsNullOrWhiteSpace(BridgeRoot);
}

internal sealed record MasterPublishRemovedView(long ViewId, string DetailNumber, string ViewName);

internal sealed record MasterPublishResult(
    int ApprovedCount,
    int AuthoringDetailCount,
    int MasterDetailCount,
    IReadOnlyList<MasterPublishRemovedView> RemovedViews,
    IReadOnlyList<string> ApprovedMissingFromAuthoring,
    bool Verified);

internal sealed class MasterPublisher
{
    private const int ParameterBatchSize = 300;
    private static readonly Regex DetailPrefixPattern = new("^KOR-D-\\d{5}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly DrafterBridgeClient _bridge;
    private readonly KorStandardsReadRepository _standardsRepository;
    private readonly StandardDetailsMasterPublishOptions _options;

    internal MasterPublisher(
        DrafterBridgeClient bridge,
        KorStandardsReadRepository standardsRepository,
        StandardDetailsMasterPublishOptions options)
    {
        _bridge = bridge;
        _standardsRepository = standardsRepository;
        _options = options;
    }

    internal async Task<MasterPublishResult> PublishAsync(TimeSpan bridgeTimeout)
    {
        ValidateOptions();

        var masterRevitDirectory = Path.GetDirectoryName(Path.GetFullPath(_options.MasterPath))
            ?? throw new InvalidOperationException("MASTER path must include a directory.");

        var tempMasterPath = Path.Combine(
            masterRevitDirectory,
            $"{Path.GetFileNameWithoutExtension(_options.MasterPath)}.publishing.{DateTime.UtcNow:yyyyMMddHHmmss}.{Guid.NewGuid():N}{Path.GetExtension(_options.MasterPath)}");

        var tempDocToken = Path.GetFileNameWithoutExtension(tempMasterPath);
        var controllerMasterPath = ResolveControllerFilePath(_options.MasterPath);
        var controllerTempMasterPath = ResolveControllerFilePath(tempMasterPath);
        var controllerMasterDirectory = Path.GetDirectoryName(controllerMasterPath)
            ?? throw new InvalidOperationException("MASTER controller path must include a directory.");
        Directory.CreateDirectory(controllerMasterDirectory);
        using var publishLock = AcquirePublishLock(controllerMasterPath);

        var tempDocumentMayBeOpen = false;
        var liveMasterReplaced = false;
        try
        {
            await _bridge.SendAsync(new { verb = "ping" }, bridgeTimeout);
            await OpenAuthoringAsync(bridgeTimeout);
            tempDocumentMayBeOpen = true;
            await SaveDocumentAsync(tempMasterPath, bridgeTimeout);
            await AssertTempMasterActiveAsync(tempDocToken, "initial temp save", bridgeTimeout);

            var approved = await _standardsRepository.LoadApprovedDetailNumbersAsync();
            if (approved.Count == 0)
            {
                throw new InvalidOperationException("Publish to MASTER refused because the approved detail set is empty.");
            }

            var initialViews = await QueryViewsAsync(bridgeTimeout);
            var initialPrefixes = await GetViewPrefixesAsync(initialViews.Select(x => x.Id), bridgeTimeout);
            var authoringDetails = BuildDetailViews(initialViews, initialPrefixes);
            if (authoringDetails.Count == 0)
            {
                throw new InvalidOperationException("Publish to MASTER refused because AUTHORING contains no KOR-D detail views.");
            }

            var authoringDetailNumbers = authoringDetails
                .Select(x => x.DetailNumber)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var approvedMissingFromAuthoring = approved
                .Where(x => !authoringDetailNumbers.Contains(x))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var toRemove = authoringDetails
                .Where(x => !approved.Contains(x.DetailNumber))
                .OrderBy(x => x.DetailNumber, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.ViewName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (toRemove.Count == authoringDetails.Count)
            {
                throw new InvalidOperationException("Publish to MASTER refused because it would remove every KOR-D detail view.");
            }

            await AssertTempMasterActiveAsync(tempDocToken, "delete", bridgeTimeout);
            if (toRemove.Count > 0)
            {
                await _bridge.SendAsync(new { verb = "delete", ids = toRemove.Select(x => x.ViewId).ToArray() }, bridgeTimeout);
            }

            await AssertTempMasterActiveAsync(tempDocToken, "post-delete save", bridgeTimeout);
            await SaveDocumentAsync(tempMasterPath, bridgeTimeout);
            await AssertTempMasterActiveAsync(tempDocToken, "verification", bridgeTimeout);

            var verifiedViews = await QueryViewsAsync(bridgeTimeout);
            var verifiedPrefixes = await GetViewPrefixesAsync(verifiedViews.Select(x => x.Id), bridgeTimeout);
            var verifiedDetails = BuildDetailViews(verifiedViews, verifiedPrefixes);
            var strayDetails = verifiedDetails
                .Where(x => !approved.Contains(x.DetailNumber))
                .OrderBy(x => x.DetailNumber, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.ViewName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (strayDetails.Count > 0)
            {
                var sample = string.Join(", ", strayDetails.Take(10).Select(x => $"{x.DetailNumber} ({x.ViewId})"));
                throw new InvalidOperationException($"MASTER verification failed. Non-approved KOR-D views remain in the temp file: {sample}");
            }

            await ReleaseTempDocumentAsync(tempDocToken, bridgeTimeout);
            try
            {
                ReplaceMaster(controllerTempMasterPath, controllerMasterPath);
                liveMasterReplaced = true;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("MASTER replacement failed after verification. Verify the live MASTER file before retrying.", ex);
            }

            return new MasterPublishResult(
                approved.Count,
                authoringDetails.Count,
                verifiedDetails.Count,
                toRemove,
                approvedMissingFromAuthoring,
                Verified: true);
        }
        finally
        {
            if (!liveMasterReplaced)
            {
                var cleanupTimeout = bridgeTimeout < TimeSpan.FromSeconds(30)
                    ? bridgeTimeout
                    : TimeSpan.FromSeconds(30);
                await CleanupTempArtifactsAsync(tempDocumentMayBeOpen, tempDocToken, controllerTempMasterPath, cleanupTimeout);
            }
        }
    }

    private void ValidateOptions()
    {
        if (!_options.IsConfigured)
        {
            throw new InvalidOperationException("Standard Details publish-to-master settings are incomplete.");
        }

        var authoringPath = Path.GetFullPath(_options.AuthoringPath);
        var masterPath = Path.GetFullPath(_options.MasterPath);
        if (string.Equals(authoringPath, masterPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("AUTHORING and MASTER paths must be different.");
        }
    }

    private async Task OpenAuthoringAsync(TimeSpan bridgeTimeout)
    {
        await _bridge.SendAsync(new
        {
            verb = "opendoc",
            path = _options.AuthoringPath,
            detach = false,
            worksets = "preserve"
        }, bridgeTimeout);
    }

    private async Task SaveDocumentAsync(string path, TimeSpan bridgeTimeout)
    {
        await _bridge.SendAsync(new
        {
            verb = "savedoc",
            path
        }, bridgeTimeout);
    }

    private async Task ReleaseTempDocumentAsync(string tempDocToken, TimeSpan bridgeTimeout)
    {
        await OpenAuthoringAsync(bridgeTimeout);
        await _bridge.SendAsync(new
        {
            verb = "closedoc",
            doc = tempDocToken
        }, bridgeTimeout);
    }

    private async Task AssertTempMasterActiveAsync(string tempDocToken, string stage, TimeSpan bridgeTimeout)
    {
        var reply = await _bridge.SendAsync(new { verb = "ping" }, bridgeTimeout);
        if (!string.IsNullOrWhiteSpace(reply.ActiveDoc)
            && reply.ActiveDoc.Contains(tempDocToken, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var activeDoc = string.IsNullOrWhiteSpace(reply.ActiveDoc) ? "(none)" : reply.ActiveDoc;
        throw new InvalidOperationException($"Drafter active document is {activeDoc}; expected temp MASTER '{tempDocToken}' before {stage}.");
    }

    private async Task CleanupTempArtifactsAsync(bool tempDocumentMayBeOpen, string tempDocToken, string controllerTempMasterPath, TimeSpan bridgeTimeout)
    {
        if (tempDocumentMayBeOpen)
        {
            try
            {
                await ReleaseTempDocumentAsync(tempDocToken, bridgeTimeout);
            }
            catch
            {
                // Best-effort cleanup only; preserve the original publish failure.
            }
        }

        try
        {
            if (File.Exists(controllerTempMasterPath))
            {
                File.Delete(controllerTempMasterPath);
            }
        }
        catch
        {
            // Best-effort cleanup only; preserve the original publish failure.
        }
    }

    private void ReplaceMaster(string tempMasterPath, string masterPath)
    {
        if (!File.Exists(tempMasterPath))
        {
            throw new FileNotFoundException("Verified temp MASTER file was not found for replacement.", tempMasterPath);
        }

        if (File.Exists(masterPath))
        {
            File.Replace(tempMasterPath, masterPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            return;
        }

        File.Move(tempMasterPath, masterPath);
    }

    private static FileStream AcquirePublishLock(string controllerMasterPath)
    {
        var lockPath = $"{controllerMasterPath}.publish.lock";
        try
        {
            var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            stream.SetLength(0);
            using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8, bufferSize: 1024, leaveOpen: true);
            writer.WriteLine($"Publish to MASTER lock");
            writer.WriteLine($"Machine: {Environment.MachineName}");
            writer.WriteLine($"User: {Environment.UserName}");
            writer.WriteLine($"Utc: {DateTime.UtcNow:O}");
            writer.Flush();
            stream.Position = 0;
            return stream;
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException("Another publish-to-master run appears to be in progress.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException($"Cannot create publish-to-master lock file '{lockPath}'.", ex);
        }
    }

    private string ResolveControllerFilePath(string revitHostPath)
    {
        var fullPath = Path.GetFullPath(revitHostPath);
        if (!IsLocalDrivePath(fullPath))
        {
            return fullPath;
        }

        if (!TryGetBridgeDriveShare(out var host, out var driveLetter))
        {
            var bridgeRoot = Path.GetPathRoot(_options.BridgeRoot);
            if (!string.IsNullOrWhiteSpace(bridgeRoot) && bridgeRoot.StartsWith(@"\\", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("BridgeRoot must use an admin-share root like \\\\host\\C$ when MASTER uses a Revit-host local drive path.");
            }

            return fullPath;
        }

        if (!char.Equals(char.ToUpperInvariant(fullPath[0]), driveLetter))
        {
            throw new InvalidOperationException($"MASTER path '{fullPath}' is on drive {char.ToUpperInvariant(fullPath[0])}: but BridgeRoot maps drive {driveLetter}:.");
        }

        var relativePath = fullPath[3..];
        return $@"\\{host}\{driveLetter}$\{relativePath}";
    }

    private static bool IsLocalDrivePath(string path)
        => path.Length >= 3
           && path[1] == ':'
           && (path[2] == Path.DirectorySeparatorChar || path[2] == Path.AltDirectorySeparatorChar)
           && char.IsLetter(path[0]);

    private bool TryGetBridgeDriveShare(out string host, out char driveLetter)
    {
        host = "";
        driveLetter = '\0';

        var root = Path.GetPathRoot(_options.BridgeRoot);
        if (string.IsNullOrWhiteSpace(root) || !root.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return false;
        }

        var parts = root.Trim('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts[1].Length != 2 || parts[1][1] != '$' || !char.IsLetter(parts[1][0]))
        {
            return false;
        }

        host = parts[0];
        driveLetter = char.ToUpperInvariant(parts[1][0]);
        return true;
    }

    private async Task<IReadOnlyList<BridgeView>> QueryViewsAsync(TimeSpan bridgeTimeout)
    {
        var reply = await _bridge.SendAsync(new
        {
            verb = "query",
            kind = "views"
        }, bridgeTimeout);

        var views = new List<BridgeView>();
        foreach (var item in EnumerateResultItems(reply.Result, "views", "items", "elements", "results"))
        {
            if (!TryGetInt64(item, "id", out var id))
            {
                continue;
            }

            var name = TryGetString(item, "name") ?? "";
            views.Add(new BridgeView(id, name));
        }

        if (views.Count == 0)
        {
            throw new InvalidOperationException("Drafter bridge query returned zero views; refusing to publish an unverifiable MASTER.");
        }

        return views;
    }

    private async Task<IReadOnlyDictionary<long, string>> GetViewPrefixesAsync(IEnumerable<long> viewIds, TimeSpan bridgeTimeout)
    {
        var prefixes = new Dictionary<long, string>();
        var batch = new List<long>(ParameterBatchSize);

        foreach (var id in viewIds)
        {
            batch.Add(id);
            if (batch.Count == ParameterBatchSize)
            {
                await ReadPrefixBatchAsync(batch, prefixes, bridgeTimeout);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await ReadPrefixBatchAsync(batch, prefixes, bridgeTimeout);
        }

        return prefixes;
    }

    private async Task ReadPrefixBatchAsync(IReadOnlyList<long> ids, IDictionary<long, string> prefixes, TimeSpan bridgeTimeout)
    {
        var reply = await _bridge.SendAsync(new
        {
            verb = "getparams",
            ids = ids.ToArray()
        }, bridgeTimeout);

        var expected = ids.ToHashSet();
        var seen = new HashSet<long>();
        foreach (var item in EnumerateResultItems(reply.Result, "elements", "items", "results"))
        {
            if (!TryGetInt64(item, "id", out var id))
            {
                continue;
            }

            seen.Add(id);
            // A view with no (or empty) "View Prefix" is a legitimate NON-detail and must be KEPT:
            // verified live that schedules and sheets carry no View Prefix parameter, and 3D/plan
            // views carry it empty. Only a KOR-D-##### prefix marks a detail. Do NOT throw here —
            // that broke every publish on the first schedule. The real unverifiable case (a requested
            // view DROPPED entirely from the reply) is the missing-id check below.
            if (TryReadViewPrefix(item, out var prefix))
            {
                prefixes[id] = prefix;
            }
        }

        var missing = expected
            .Where(x => !seen.Contains(x))
            .Take(10)
            .ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"Drafter bridge getparams omitted {missing.Count} requested view(s): {string.Join(", ", missing)}.");
        }
    }

    private static IReadOnlyList<MasterPublishRemovedView> BuildDetailViews(
        IReadOnlyList<BridgeView> views,
        IReadOnlyDictionary<long, string> prefixes)
    {
        var viewNames = views.ToDictionary(x => x.Id, x => x.Name);
        return prefixes
            .Where(x => DetailPrefixPattern.IsMatch(x.Value))
            .Select(x => new MasterPublishRemovedView(
                x.Key,
                x.Value.ToUpperInvariant(),
                viewNames.TryGetValue(x.Key, out var name) ? name : ""))
            .ToList();
    }

    private static bool TryReadViewPrefix(JsonElement item, out string prefix)
    {
        prefix = "";
        if (!TryGetProperty(item, "parameters", out var parameters))
        {
            return false;
        }

        if (parameters.ValueKind == JsonValueKind.Array)
        {
            foreach (var parameter in parameters.EnumerateArray())
            {
                var name = TryGetString(parameter, "name");
                if (!string.Equals(name, "View Prefix", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                prefix = TryGetString(parameter, "displayValue")
                    ?? TryGetString(parameter, "display")
                    ?? TryGetString(parameter, "value")
                    ?? TryGetString(parameter, "stringValue")
                    ?? "";
                prefix = prefix.Trim();
                return true;
            }
        }
        else if (parameters.ValueKind == JsonValueKind.Object && TryGetProperty(parameters, "View Prefix", out var value))
        {
            prefix = ReadScalarOrDisplayValue(value).Trim();
            return true;
        }

        return false;
    }

    private static string ReadScalarOrDisplayValue(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? "";
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            return TryGetString(value, "displayValue")
                ?? TryGetString(value, "display")
                ?? TryGetString(value, "value")
                ?? TryGetString(value, "stringValue")
                ?? "";
        }

        return "";
    }

    private static IEnumerable<JsonElement> EnumerateResultItems(JsonElement result, params string[] arrayPropertyNames)
    {
        if (result.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in result.EnumerateArray())
            {
                yield return item;
            }

            yield break;
        }

        if (result.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var propertyName in arrayPropertyNames)
        {
            if (!TryGetProperty(result, propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in array.EnumerateArray())
            {
                yield return item;
            }

            yield break;
        }
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

    private static bool TryGetInt64(JsonElement element, string name, out long value)
    {
        value = 0;
        if (!TryGetProperty(element, name, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out value))
        {
            return true;
        }

        return property.ValueKind == JsonValueKind.String && long.TryParse(property.GetString(), out value);
    }

    private sealed record BridgeView(long Id, string Name);
}

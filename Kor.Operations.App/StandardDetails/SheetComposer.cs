#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Kor.Operations.StandardDetails;

internal sealed record SheetComposerPlacement(
    string DetailNumber,
    string CanonicalViewName,
    double Xmm,
    double Ymm,
    int? Scale);

internal sealed record SheetComposerRequest(
    string SheetNumber,
    string SheetName,
    string LikeSheet,
    IReadOnlyList<SheetComposerPlacement> Placements);

internal sealed record SheetComposerOccupiedDetail(
    string DetailNumber,
    string ViewName,
    string SheetNumber,
    string SheetName);

internal sealed record SheetComposerResult(
    string SheetNumber,
    string SheetName,
    int PlacementCount,
    long? GovernanceDocumentId);

internal sealed class StandardDetailsSheetComposer
{
    private const int SheetNumberMax = 40;
    private const int LikeSheetMax = 80;
    private const int GovernanceTitleMax = 300;
    private const int GovernanceDescriptionMax = 2000;
    private const int ParameterBatchSize = 300;
    private static readonly TimeSpan TempPdfRetention = TimeSpan.FromDays(1);
    // The KOR-D identity lives in a view's "View Prefix" parameter, not necessarily in its name.
    // Match occupancy on that, the same way MasterPublisher does.
    private static readonly Regex DetailPrefixPattern = new(@"^KOR-D-\d{5}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly DrafterBridgeClient _bridge;
    private readonly StandardDetailsMasterPublishOptions _options;

    internal StandardDetailsSheetComposer(DrafterBridgeClient bridge, StandardDetailsMasterPublishOptions options)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    internal async Task<IReadOnlyDictionary<string, SheetComposerOccupiedDetail>> LoadOccupiedDetailsAsync(TimeSpan bridgeTimeout)
    {
        await AssertAuthoringActiveAsync("load sheet occupancy", bridgeTimeout);
        var sheets = await QuerySheetsAsync(bridgeTimeout);

        // The detail number is the placed view's View Prefix parameter, read over getparams; the
        // query-sheets reply carries only view id/name, and the name is descriptive, not a KOR-D.
        var placedViewIds = sheets.SelectMany(s => s.Views.Select(v => v.Id)).Distinct();
        var prefixes = await GetViewPrefixesAsync(placedViewIds, bridgeTimeout);

        var occupied = new Dictionary<string, SheetComposerOccupiedDetail>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in sheets)
        {
            foreach (var view in sheet.Views)
            {
                var isDetail = prefixes.TryGetValue(view.Id, out var prefix) && DetailPrefixPattern.IsMatch(prefix);
                var detailNumber = isDetail ? NormalizeDetailNumber(prefix) : "";
                var record = new SheetComposerOccupiedDetail(detailNumber, view.Name, sheet.Number, sheet.Name);

                // Primary key: the KOR-D detail number, so a placement is matched by its real identity.
                if (isDetail)
                {
                    occupied.TryAdd(detailNumber, record);
                }

                // Secondary key: the view name, so an already-placed view is still caught where
                // the catalog and Revit disagree on the number. Revit's one-sheet rule is the final backstop.
                if (!string.IsNullOrWhiteSpace(view.Name))
                {
                    occupied.TryAdd($"view:{view.Name}", record);
                }
            }
        }

        return occupied;
    }

    internal async Task<SheetComposerResult> ComposeAsync(
        SheetComposerRequest request,
        StandardDetailsRepository governanceRepository,
        bool groupSchemaAvailable,
        long? selectedGroupId,
        Guid actorUserId,
        TimeSpan bridgeTimeout)
    {
        ValidateRequest(request);
        await AssertAuthoringActiveAsync("compose sheet", bridgeTimeout);

        var occupied = await LoadOccupiedDetailsAsync(bridgeTimeout);
        var alreadyPlaced = request.Placements
            .Select(x => (Placement: x, Occupancy: ResolveOccupiedPlacement(x, occupied)))
            .Where(x => x.Occupancy is not null)
            .Select(x => $"{x.Placement.DetailNumber} ({x.Occupancy!.SheetNumber})")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (alreadyPlaced.Count > 0)
        {
            throw new InvalidOperationException("Save refused because these details are already committed to sheets: " + string.Join(", ", alreadyPlaced));
        }

        await ValidatePlacementViewsAsync(request, bridgeTimeout);

        long? sheetId = null;
        long? governanceDocumentId = null;
        var governanceTitle = BuildGovernanceTitle(request);
        var previousScales = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await AssertAuthoringActiveAsync("create sheet", bridgeTimeout);
            var sheetReply = await _bridge.SendAsync(new
            {
                verb = "newsheet",
                number = request.SheetNumber,
                name = request.SheetName,
                like = request.LikeSheet
            }, bridgeTimeout);
            sheetId = await ResolveCreatedSheetIdAsync(sheetReply.Result, request.SheetNumber, bridgeTimeout);

            foreach (var placement in request.Placements)
            {
                await AssertAuthoringActiveAsync($"place {placement.DetailNumber}", bridgeTimeout);
                if (placement.Scale is { } scale)
                {
                    var scaleReply = await _bridge.SendAsync(new
                    {
                        verb = "setscale",
                        view = placement.CanonicalViewName,
                        scale
                    }, bridgeTimeout);

                    if (TryGetInt32(scaleReply.Result, "was", out var was)
                        && TryGetString(scaleReply.Result, "view") is { } scaledView
                        && !previousScales.ContainsKey(scaledView))
                    {
                        previousScales.Add(scaledView, was);
                    }
                }

                var placeReply = await _bridge.SendAsync(new
                {
                    verb = "placeview",
                    sheet = request.SheetNumber,
                    view = placement.CanonicalViewName,
                    x_mm = placement.Xmm,
                    y_mm = placement.Ymm
                }, bridgeTimeout);
                AssertPlacedCenter(placement, placeReply.Result);
            }

            governanceDocumentId = await governanceRepository.CreateDocumentAsync(
                governanceTitle,
                BuildGovernanceDescription(request),
                groupSchemaAvailable,
                selectedGroupId,
                actorUserId);

            await AssertAuthoringActiveAsync("save composed sheet", bridgeTimeout);
            await _bridge.SendAsync(new { verb = "savedoc" }, bridgeTimeout);

            return new SheetComposerResult(request.SheetNumber, request.SheetName, request.Placements.Count, governanceDocumentId);
        }
        catch (Exception ex)
        {
            var rollbackFailures = await RollBackAsync(sheetId, previousScales, bridgeTimeout);
            if (governanceDocumentId is { } documentId)
            {
                try
                {
                    await governanceRepository.DeleteRecordAsync(documentId, governanceTitle, actorUserId);
                }
                catch (Exception cleanupEx)
                {
                    rollbackFailures.Add($"governance record {documentId}: {cleanupEx.Message}");
                }
            }

            if (rollbackFailures.Count > 0)
            {
                throw new InvalidOperationException(
                    ex.Message + Environment.NewLine + "Rollback also reported: " + string.Join(" | ", rollbackFailures),
                    ex);
            }

            throw;
        }
    }

    internal async Task<string> OpenSheetPdfAsync(string sheetNumber, TimeSpan bridgeTimeout)
    {
        if (!_options.IsConfigured)
        {
            throw new InvalidOperationException("Standard Details sheet-composer settings are incomplete.");
        }

        sheetNumber = (sheetNumber ?? "").Trim();
        if (string.IsNullOrWhiteSpace(sheetNumber))
        {
            throw new InvalidOperationException("Sheet number is required before opening a PDF.");
        }

        await AssertAuthoringActiveAsync("export sheet PDF", bridgeTimeout);

        var exportFolder = Path.Combine(_options.BridgeRoot, "exports", "standard-details-sheets");
        var exportFileName = $"KOR-StandardDetails-{SanitizeFileName(sheetNumber)}-{DateTime.Now:yyyyMMdd-HHmmss}";
        Directory.CreateDirectory(exportFolder);

        var reply = await _bridge.SendAsync(new
        {
            verb = "exportsheets",
            folder = exportFolder,
            filename = exportFileName,
            sheets = new[] { sheetNumber }
        }, bridgeTimeout);

        var exportedPdf = ResolveExportedSheetPdf(reply.Result, sheetNumber);
        if (!File.Exists(exportedPdf))
        {
            throw new InvalidOperationException($"Drafter exported '{sheetNumber}' to '{exportedPdf}', but the app could not read the PDF from that share path.");
        }

        return CopyPdfToTempAndOpen(exportedPdf, sheetNumber);
    }

    internal async Task<string> OpenDetailPdfAsync(string detailNumber, KorStandardsReadRepository reader, TimeSpan bridgeTimeout)
    {
        if (!_options.IsConfigured)
        {
            throw new InvalidOperationException("Standard Details bridge settings are incomplete.");
        }

        detailNumber = (detailNumber ?? "").Trim();
        if (string.IsNullOrWhiteSpace(detailNumber))
        {
            throw new InvalidOperationException("Detail number is required before opening a PDF.");
        }

        var viewElementId = await reader.GetCanonicalViewElementIdAsync(detailNumber);
        if (viewElementId is null)
        {
            throw new InvalidOperationException($"No drawing view is recorded for {detailNumber}; it cannot be exported as a PDF from the catalog.");
        }

        await AssertDocumentActiveAsync("export catalog PDF", bridgeTimeout);

        var exportFolder = Path.Combine(_options.BridgeRoot, "exports", "standard-details-views");
        Directory.CreateDirectory(exportFolder);

        var reply = await _bridge.SendAsync(new
        {
            verb = "exportviews",
            folder = exportFolder,
            colors = "color",
            views = new[] { new { id = viewElementId.Value, key = detailNumber } }
        }, bridgeTimeout);

        var exportedPdf = ResolveExportedViewPdf(reply.Result, viewElementId.Value, detailNumber);
        if (!File.Exists(exportedPdf))
        {
            throw new InvalidOperationException($"Drafter exported '{detailNumber}' to '{exportedPdf}', but the app could not read the PDF from that share path.");
        }

        return CopyPdfToTempAndOpen(exportedPdf, detailNumber);
    }

    private void ValidateRequest(SheetComposerRequest request)
    {
        if (!_options.IsConfigured)
        {
            throw new InvalidOperationException("Standard Details sheet-composer settings are incomplete.");
        }

        if (string.IsNullOrWhiteSpace(request.SheetNumber))
        {
            throw new InvalidOperationException("Sheet number is required.");
        }

        if (request.SheetNumber.Length > SheetNumberMax)
        {
            throw new InvalidOperationException($"Sheet number cannot exceed {SheetNumberMax} characters.");
        }

        if (string.IsNullOrWhiteSpace(request.SheetName))
        {
            throw new InvalidOperationException("Sheet name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.LikeSheet))
        {
            throw new InvalidOperationException("A title-block source sheet number/name is required.");
        }

        if (request.LikeSheet.Length > LikeSheetMax)
        {
            throw new InvalidOperationException($"Title-block source sheet cannot exceed {LikeSheetMax} characters.");
        }

        if (request.Placements.Count == 0)
        {
            throw new InvalidOperationException("Add at least one detail to the sheet.");
        }

        var title = BuildGovernanceTitle(request);
        if (title.Length > GovernanceTitleMax)
        {
            throw new InvalidOperationException($"Sheet number and name cannot exceed {GovernanceTitleMax} characters combined.");
        }

        var duplicateDetails = request.Placements
            .GroupBy(x => NormalizeDetailNumber(x.DetailNumber), StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .ToList();
        if (duplicateDetails.Count > 0)
        {
            throw new InvalidOperationException("Save refused because the sheet contains duplicate detail selections: " + string.Join(", ", duplicateDetails));
        }

        foreach (var placement in request.Placements)
        {
            if (string.IsNullOrWhiteSpace(placement.DetailNumber) || string.IsNullOrWhiteSpace(placement.CanonicalViewName))
            {
                throw new InvalidOperationException("Every placement must carry a detail number and canonical view name.");
            }

            if (!double.IsFinite(placement.Xmm) || !double.IsFinite(placement.Ymm) || placement.Xmm < 0 || placement.Ymm < 0)
            {
                throw new InvalidOperationException($"{placement.DetailNumber}: placement coordinates must be finite, non-negative sheet millimetres.");
            }
        }
    }

    private async Task AssertAuthoringActiveAsync(string stage, TimeSpan bridgeTimeout)
    {
        var reply = await _bridge.SendAsync(new { verb = "ping" }, bridgeTimeout);
        var expected = System.IO.Path.GetFileNameWithoutExtension(_options.AuthoringPath);
        if (!string.IsNullOrWhiteSpace(expected)
            && !string.IsNullOrWhiteSpace(reply.ActiveDoc)
            && reply.ActiveDoc.Contains(expected, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var activeDoc = string.IsNullOrWhiteSpace(reply.ActiveDoc) ? "(none)" : reply.ActiveDoc;
        throw new InvalidOperationException($"Drafter active document is {activeDoc}; expected AUTHORING '{expected}' before {stage}.");
    }

    private async Task AssertDocumentActiveAsync(string stage, TimeSpan bridgeTimeout)
    {
        var reply = await _bridge.SendAsync(new { verb = "ping" }, bridgeTimeout);
        if (!string.IsNullOrWhiteSpace(reply.ActiveDoc))
        {
            return;
        }

        throw new InvalidOperationException($"Drafter bridge has no active Revit document before {stage}.");
    }

    private async Task<IReadOnlyList<BridgeSheet>> QuerySheetsAsync(TimeSpan bridgeTimeout)
    {
        var reply = await _bridge.SendAsync(new
        {
            verb = "query",
            kind = "sheets"
        }, bridgeTimeout);

        var sheets = new List<BridgeSheet>();
        foreach (var item in EnumerateResultItems(reply.Result, "sheets", "items", "results"))
        {
            if (!TryGetInt64(item, "id", out var id))
            {
                continue;
            }

            var views = new List<BridgeSheetView>();
            if (TryGetProperty(item, "views", out var viewArray) && viewArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var view in viewArray.EnumerateArray())
                {
                    if (TryGetInt64(view, "id", out var viewId))
                    {
                        views.Add(new BridgeSheetView(viewId, TryGetString(view, "name") ?? ""));
                    }
                }
            }

            sheets.Add(new BridgeSheet(
                id,
                TryGetString(item, "number") ?? "",
                TryGetString(item, "name") ?? "",
                views));
        }

        return sheets;
    }

    private async Task<long> ResolveCreatedSheetIdAsync(JsonElement sheetResult, string sheetNumber, TimeSpan bridgeTimeout)
    {
        if (TryGetInt64(sheetResult, "id", out var createdId))
        {
            return createdId;
        }

        var sheets = await QuerySheetsAsync(bridgeTimeout);
        var match = sheets.FirstOrDefault(x => string.Equals(x.Number, sheetNumber, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            return match.Id;
        }

        throw new InvalidOperationException($"Drafter created sheet '{sheetNumber}' but did not return a sheet id; rollback cannot be verified.");
    }

    private async Task ValidatePlacementViewsAsync(SheetComposerRequest request, TimeSpan bridgeTimeout)
    {
        var views = await QueryViewsAsync(bridgeTimeout);
        var viewNames = views.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = request.Placements
            .Where(x => !viewNames.Contains(x.CanonicalViewName))
            .Select(x => $"{x.DetailNumber} ({x.CanonicalViewName})")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException("Save refused because these canonical views are not present in active AUTHORING: " + string.Join(", ", missing));
        }
    }

    private async Task<IReadOnlyList<BridgeView>> QueryViewsAsync(TimeSpan bridgeTimeout)
    {
        var reply = await _bridge.SendAsync(new
        {
            verb = "query",
            kind = "views"
        }, bridgeTimeout);

        var views = new List<BridgeView>();
        foreach (var item in EnumerateResultItems(reply.Result, "views", "items", "results"))
        {
            if (TryGetInt64(item, "id", out var id))
            {
                views.Add(new BridgeView(id, TryGetString(item, "name") ?? ""));
            }
        }

        return views;
    }

    private async Task<List<string>> RollBackAsync(long? sheetId, IReadOnlyDictionary<string, int> previousScales, TimeSpan bridgeTimeout)
    {
        var failures = new List<string>();

        foreach (var previousScale in previousScales)
        {
            try
            {
                await AssertAuthoringActiveAsync($"restore scale for {previousScale.Key}", bridgeTimeout);
                await _bridge.SendAsync(new
                {
                    verb = "setscale",
                    view = previousScale.Key,
                    scale = previousScale.Value
                }, bridgeTimeout);
            }
            catch
            {
                failures.Add($"scale restore for {previousScale.Key} failed");
            }
        }

        if (sheetId is null)
        {
            return failures;
        }

        try
        {
            await AssertAuthoringActiveAsync("rollback composed sheet", bridgeTimeout);
            await _bridge.SendAsync(new
            {
                verb = "delete",
                ids = new[] { sheetId.Value }
            }, bridgeTimeout);
        }
        catch (Exception ex)
        {
            failures.Add($"sheet delete for id {sheetId.Value} failed: {ex.Message}");
        }

        return failures;
    }

    private static string ResolveExportedSheetPdf(JsonElement result, string sheetNumber)
    {
        if (TryGetBool(result, "exists", out var topExists) && !topExists)
        {
            throw new InvalidOperationException($"Drafter did not export sheet '{sheetNumber}'.");
        }

        if (TryFindSheetExportItem(result, sheetNumber, out var item))
        {
            if (TryGetBool(item, "exists", out var itemExists) && !itemExists)
            {
                throw new InvalidOperationException($"Drafter did not export sheet '{sheetNumber}'.");
            }

            if (TryGetString(item, "pdf") is { Length: > 0 } itemPdf)
            {
                return itemPdf;
            }
        }

        if (TryGetString(result, "pdf") is { Length: > 0 } pdf)
        {
            return pdf;
        }

        throw new InvalidOperationException($"Drafter export for sheet '{sheetNumber}' did not return a PDF path.");
    }

    private static bool TryFindSheetExportItem(JsonElement result, string sheetNumber, out JsonElement item)
    {
        item = default;
        var fallback = default(JsonElement);
        var count = 0;
        foreach (var candidate in EnumerateResultItems(result, "sheets", "items", "results"))
        {
            count++;
            fallback = candidate;
            if (string.Equals(TryGetString(candidate, "number"), sheetNumber, StringComparison.OrdinalIgnoreCase)
                || string.Equals(TryGetString(candidate, "sheet"), sheetNumber, StringComparison.OrdinalIgnoreCase)
                || string.Equals(TryGetString(candidate, "sheetNumber"), sheetNumber, StringComparison.OrdinalIgnoreCase))
            {
                item = candidate;
                return true;
            }
        }

        if (count == 1)
        {
            item = fallback;
            return true;
        }

        return false;
    }

    private static string ResolveExportedViewPdf(JsonElement result, long viewElementId, string detailNumber)
    {
        if (TryGetBool(result, "exists", out var topExists) && !topExists)
        {
            throw new InvalidOperationException($"Drafter did not export view '{detailNumber}'.");
        }

        if (TryFindViewExportItem(result, viewElementId, detailNumber, out var item))
        {
            if (TryGetBool(item, "exists", out var itemExists) && !itemExists)
            {
                throw new InvalidOperationException($"Drafter did not export view '{detailNumber}'.");
            }

            if (TryGetString(item, "pdf") is { Length: > 0 } itemPdf)
            {
                return itemPdf;
            }
        }

        if (TryGetString(result, "pdf") is { Length: > 0 } pdf)
        {
            return pdf;
        }

        throw new InvalidOperationException($"Drafter export for view '{detailNumber}' did not return a PDF path.");
    }

    private static bool TryFindViewExportItem(JsonElement result, long viewElementId, string detailNumber, out JsonElement item)
    {
        item = default;
        var fallback = default(JsonElement);
        var count = 0;
        foreach (var candidate in EnumerateResultItems(result, "views", "items", "results"))
        {
            count++;
            fallback = candidate;
            if ((TryGetInt64(candidate, "id", out var id) && id == viewElementId)
                || string.Equals(TryGetString(candidate, "key"), detailNumber, StringComparison.OrdinalIgnoreCase)
                || string.Equals(TryGetString(candidate, "detailNumber"), detailNumber, StringComparison.OrdinalIgnoreCase))
            {
                item = candidate;
                return true;
            }
        }

        if (count == 1)
        {
            item = fallback;
            return true;
        }

        return false;
    }

    private static string CopyPdfToTempAndOpen(string exportedPdf, string identity)
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), "KOR-StandardDetails");
        Directory.CreateDirectory(tempFolder);
        CleanOldTempPdfs(tempFolder);

        var tempPath = Path.Combine(tempFolder, $"{SanitizeFileName(identity)}-{DateTime.Now:yyyyMMdd-HHmmss}.pdf");
        File.Copy(exportedPdf, tempPath, overwrite: true);

        Process.Start(new ProcessStartInfo(tempPath)
        {
            UseShellExecute = true
        });

        return tempPath;
    }

    private static void CleanOldTempPdfs(string tempFolder)
    {
        try
        {
            var cutoff = DateTime.UtcNow.Subtract(TempPdfRetention);
            foreach (var file in Directory.EnumerateFiles(tempFolder, "*.pdf", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // Best-effort temp cleanup only.
                }
            }
        }
        catch
        {
            // Best-effort temp cleanup only.
        }
    }

    private static string SanitizeFileName(string sheetNumber)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var cleaned = new string(sheetNumber.Trim()
            .Select(ch => invalid.Contains(ch) ? '-' : ch)
            .ToArray());
        cleaned = Regex.Replace(cleaned, @"\s+", "-");
        cleaned = Regex.Replace(cleaned, "-{2,}", "-").Trim('-', '.');
        return string.IsNullOrWhiteSpace(cleaned) ? "sheet" : cleaned;
    }

    private static SheetComposerOccupiedDetail? ResolveOccupiedPlacement(
        SheetComposerPlacement placement,
        IReadOnlyDictionary<string, SheetComposerOccupiedDetail> occupied)
    {
        if (occupied.TryGetValue(NormalizeDetailNumber(placement.DetailNumber), out var byNumber))
        {
            return byNumber;
        }

        return occupied.Values.FirstOrDefault(x => string.Equals(x.ViewName, placement.CanonicalViewName, StringComparison.OrdinalIgnoreCase));
    }

    // View Prefix ("KOR-D-#####") read over getparams, the same reader MasterPublisher uses.
    // If one view is omitted from an otherwise valid reply, the view-name fallback and Revit's
    // one-sheet rule still catch duplicate placement.
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
        if (ids.Count == 0)
        {
            return;
        }

        var reply = await _bridge.SendAsync(new { verb = "getparams", ids = ids.ToArray() }, bridgeTimeout);
        foreach (var item in EnumerateResultItems(reply.Result, "elements", "items", "results"))
        {
            if (TryGetInt64(item, "id", out var id) && TryReadViewPrefix(item, out var prefix))
            {
                prefixes[id] = prefix;
            }
        }
    }

    private static bool TryReadViewPrefix(JsonElement item, out string prefix)
    {
        prefix = "";
        if (!TryGetProperty(item, "parameters", out var parameters) || parameters.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var parameter in parameters.EnumerateArray())
        {
            if (!string.Equals(TryGetString(parameter, "name"), "View Prefix", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            prefix = (TryGetString(parameter, "displayValue")
                ?? TryGetString(parameter, "display")
                ?? TryGetString(parameter, "value")
                ?? TryGetString(parameter, "stringValue")
                ?? "").Trim();
            return true;
        }

        return false;
    }

    private static string NormalizeDetailNumber(string? detailNumber)
        => (detailNumber ?? "").Trim().ToUpperInvariant();

    private static void AssertPlacedCenter(SheetComposerPlacement placement, JsonElement result)
    {
        if (!TryGetDouble(result, "x_mm", out var actualX) || !TryGetDouble(result, "y_mm", out var actualY))
        {
            throw new InvalidOperationException($"Drafter bridge did not echo placed center for {placement.DetailNumber}.");
        }

        if (Math.Abs(actualX - placement.Xmm) > 1.0 || Math.Abs(actualY - placement.Ymm) > 1.0)
        {
            throw new InvalidOperationException($"Drafter placed {placement.DetailNumber} at {actualX:0.0}, {actualY:0.0} mm instead of requested {placement.Xmm:0.0}, {placement.Ymm:0.0} mm.");
        }
    }

    private static string BuildGovernanceTitle(SheetComposerRequest request)
        => $"{request.SheetNumber} - {request.SheetName}";

    private static string BuildGovernanceDescription(SheetComposerRequest request)
    {
        var text = "Composed Standard Details sheet from approved AUTHORING detail views." + Environment.NewLine
                   + $"Sheet: {request.SheetNumber} - {request.SheetName}" + Environment.NewLine
                   + $"Like sheet: {request.LikeSheet}" + Environment.NewLine
                   + $"Created UTC: {DateTime.UtcNow:O}" + Environment.NewLine
                   + "Placed details:" + Environment.NewLine
                   + string.Join(Environment.NewLine, request.Placements.Select(x =>
                       $"- {x.DetailNumber} | {x.CanonicalViewName} | x={x.Xmm:0.0}mm y={x.Ymm:0.0}mm"
                       + (x.Scale is { } scale ? $" scale=1:{scale}" : "")));

        const string suffix = "...";
        return text.Length <= GovernanceDescriptionMax
            ? text
            : text[..(GovernanceDescriptionMax - suffix.Length)] + suffix;
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

        return property.ValueKind == JsonValueKind.String
               && long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetInt32(JsonElement element, string name, out int value)
    {
        value = 0;
        if (!TryGetProperty(element, name, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value))
        {
            return true;
        }

        return property.ValueKind == JsonValueKind.String
               && int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetBool(JsonElement element, string name, out bool value)
    {
        value = false;
        if (!TryGetProperty(element, name, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => SetBool(out value, true),
            JsonValueKind.False => SetBool(out value, false),
            JsonValueKind.String when bool.TryParse(property.GetString(), out var parsed) => SetBool(out value, parsed),
            _ => false
        };
    }

    private static bool SetBool(out bool value, bool parsed)
    {
        value = parsed;
        return true;
    }

    private static bool TryGetDouble(JsonElement element, string name, out double value)
    {
        value = 0;
        if (!TryGetProperty(element, name, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out value))
        {
            return true;
        }

        return property.ValueKind == JsonValueKind.String
               && double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private sealed record BridgeSheet(long Id, string Number, string Name, IReadOnlyList<BridgeSheetView> Views);
    private sealed record BridgeSheetView(long Id, string Name);
    private sealed record BridgeView(long Id, string Name);
}

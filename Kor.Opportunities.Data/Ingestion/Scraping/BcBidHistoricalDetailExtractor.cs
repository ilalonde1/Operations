#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Kor.Opportunities.Data.Ingestion.Scraping;

/// <summary>
/// Pure-parsing extractor for a BC Bid historical detail page. The caller is
/// responsible for navigating Playwright to the per-row detail URL and ensuring
/// the page is fully loaded; this class just reads field values out of the DOM.
/// </summary>
public static class BcBidHistoricalDetailExtractor
{
    public sealed record DetailExtraction
    {
        public string? Commodities { get; init; }
        public int? AmendmentCount { get; init; }
        public string? NoticeType { get; init; }
        public string? Background { get; init; }
        public string? Scope { get; init; }
        public string? MinistryResponsibility { get; init; }
        public string? EstimatedAmountText { get; init; }
        public string? InitialDurationMonths { get; init; }
        public string? FullDescription { get; init; }
        public IReadOnlyList<DocumentLink> Documents { get; init; } = Array.Empty<DocumentLink>();
    }

    public sealed record DocumentLink(string FileName, string SourceUrl);

    public static async Task<DetailExtraction> ExtractAsync(IPage page, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var commodities = await TextareaValueAsync(
            page,
            "#body_x_tabc_rfp_ext_prxrfp_ext_x_txtBpmNeed").ConfigureAwait(false);
        var amendmentText = await InnerTextAsync(
            page,
            "#body_x_tabc_rfp_ext_prxrfp_ext_x_lblRound").ConfigureAwait(false);
        var noticeType = await InnerTextAsync(
            page,
            "[data-iv-control=\"body_x_tabc_rfp_ext_prxrfp_ext_x_selNtypeCode\"] .text").ConfigureAwait(false);

        var background = await CkEditorTextAsync(
            page,
            "#body_x_tabc_rfpadditional_rfx_info_prxrfpadditional_rfx_info_x_txtRfpSituationBackground_1")
            .ConfigureAwait(false);
        var scope = await CkEditorTextAsync(
            page,
            "#body_x_tabc_rfpadditional_rfx_info_prxrfpadditional_rfx_info_x_txtRfpSituationScope2_1")
            .ConfigureAwait(false);
        var ministryResp = await CkEditorTextAsync(
            page,
            "#body_x_tabc_rfpadditional_rfx_info_prxrfpadditional_rfx_info_x_txtRfpSituationOrgResponsibility_1")
            .ConfigureAwait(false);
        var estimatedAmount = await InputValueAsync(
            page,
            "#body_x_tabc_rfpadditional_rfx_info_prxrfpadditional_rfx_info_x_txtRfpEstAmount")
            .ConfigureAwait(false);
        var initialDuration = await InputValueAsync(
            page,
            "#body_x_tabc_rfpadditional_rfx_info_prxrfpadditional_rfx_info_x_txtRfpCtrInitialDuration_1")
            .ConfigureAwait(false);

        int? amendmentCount = null;
        if (int.TryParse(amendmentText?.Trim(), out var amend))
        {
            amendmentCount = amend;
        }

        var fullDescription = ComposeFullDescription(background, scope, ministryResp);
        var documents = await ExtractDocumentsAsync(page).ConfigureAwait(false);

        return new DetailExtraction
        {
            Commodities = NullIfEmpty(commodities),
            AmendmentCount = amendmentCount,
            NoticeType = NullIfEmpty(noticeType),
            Background = NullIfEmpty(background),
            Scope = NullIfEmpty(scope),
            MinistryResponsibility = NullIfEmpty(ministryResp),
            EstimatedAmountText = NullIfEmpty(estimatedAmount),
            InitialDurationMonths = NullIfEmpty(initialDuration),
            FullDescription = NullIfEmpty(fullDescription),
            Documents = documents,
        };
    }

    private static async Task<string?> InnerTextAsync(IPage page, string selector)
    {
        var el = await page.QuerySelectorAsync(selector).ConfigureAwait(false);
        if (el is null) return null;
        var text = await el.InnerTextAsync().ConfigureAwait(false);
        return text?.Trim();
    }

    private static async Task<string?> TextareaValueAsync(IPage page, string selector)
    {
        var el = await page.QuerySelectorAsync(selector).ConfigureAwait(false);
        if (el is null) return null;
        var val = await el.EvaluateAsync<string?>("el => el.value ?? ''").ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(val) ? null : val.Trim();
    }

    /// <summary>
    /// BC Bid renders long-form fields (Background, Scope, etc.) via CKEditor: the
    /// underlying &lt;textarea&gt; is style="display:none" and the editable text
    /// lives in a sibling div.cke_textarea_inline. We prefer the editor div's
    /// innerText, falling back to textarea.value when the editor isn't present.
    /// </summary>
    private static async Task<string?> CkEditorTextAsync(IPage page, string textareaSelector)
    {
        var ta = await page.QuerySelectorAsync(textareaSelector).ConfigureAwait(false);
        if (ta is null) return null;

        // 1. Look for an adjacent CKEditor content div under the same field wrapper.
        var editorText = await ta.EvaluateAsync<string?>(@"el => {
            const wrapper = el.closest('[data-iv-control]');
            if (!wrapper) return null;
            const ed = wrapper.querySelector('div.cke_textarea_inline');
            if (!ed) return null;
            const t = (ed.innerText || ed.textContent || '').trim();
            return t || null;
        }").ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(editorText)) return editorText.Trim();

        // 2. Fall back to the textarea's own value.
        var fallback = await ta.EvaluateAsync<string?>("el => el.value ?? ''").ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(fallback) ? null : fallback.Trim();
    }

    private static async Task<string?> InputValueAsync(IPage page, string selector)
    {
        var el = await page.QuerySelectorAsync(selector).ConfigureAwait(false);
        if (el is null) return null;
        var val = await el.GetAttributeAsync("value").ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(val) ? null : val.Trim();
    }

    private static string? ComposeFullDescription(string? background, string? scope, string? ministryResp)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(background)) parts.Add("Background:\n" + background.Trim());
        if (!string.IsNullOrWhiteSpace(scope)) parts.Add("Scope:\n" + scope.Trim());
        if (!string.IsNullOrWhiteSpace(ministryResp))
        {
            parts.Add("Ministry Responsibility:\n" + ministryResp.Trim());
        }

        return parts.Count == 0 ? null : string.Join("\n\n--\n\n", parts);
    }

    private static async Task<IReadOnlyList<DocumentLink>> ExtractDocumentsAsync(IPage page)
    {
        var docs = new List<DocumentLink>();
        var rows = await page.QuerySelectorAllAsync(
            "#body_x_tabc_rfp_ext_prxrfp_ext_x_prxDoc_x_grid_phcgrid tbody tr").ConfigureAwait(false);

        foreach (var row in rows)
        {
            // Skip header/empty rows that don't carry data-id.
            var rowId = await row.GetAttributeAsync("id").ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(rowId) || !rowId.Contains("_grd_tr_", StringComparison.Ordinal))
            {
                continue;
            }

            var anchor = await row.QuerySelectorAsync("a[href]").ConfigureAwait(false);
            if (anchor is null) continue;
            var href = await anchor.GetAttributeAsync("href").ConfigureAwait(false);
            var name = await anchor.InnerTextAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(name)) continue;

            docs.Add(new DocumentLink(name.Trim(), href.Trim()));
        }

        return docs;
    }

    private static string? NullIfEmpty(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s;
}

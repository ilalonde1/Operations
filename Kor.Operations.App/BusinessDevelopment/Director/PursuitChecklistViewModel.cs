#nullable enable
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core;
using Kor.Operations.Rendering.Proposal;
using Kor.Opportunities.Data.Crm;
using Serilog;

namespace Kor.Operations.App.BusinessDevelopment.Director;

public sealed class ChecklistAttachmentRow(PursuitFile file) : ObservableObject
{
    public PursuitFile File { get; } = file;
    public ProposalDocxMetadata? Metadata { get; private set; }
    public string Message { get; private set; } = "";
    public void SetMetadata(ProposalDocxMetadata? metadata, string message = "")
    {
        Metadata = metadata; Message = message;
        OnPropertyChanged(nameof(Metadata)); OnPropertyChanged(nameof(Message));
    }
}

public sealed class PursuitChecklistViewModel(IPursuitRequirementStore requirements, IPursuitFileStore files,
    long engagementId, string rfpNumber, string actor) : ObservableObject
{
    public string Title => $"Checklist — {rfpNumber}";
    public ObservableCollection<PursuitRequirement> Items { get; } = new();
    public ObservableCollection<ChecklistAttachmentRow> Attachments { get; } = new();
    public System.Collections.Generic.IReadOnlyList<string> Kinds => PursuitRequirementValidation.Kinds;
    public System.Collections.Generic.IReadOnlyList<string> Statuses => PursuitRequirementValidation.Statuses;
    public string Requirement { get; set; } = "";
    public string SourceRef { get; set; } = "";
    public string CheckKind { get; set; } = "Manual";
    public string CheckParameter { get; set; } = "";
    public string Status { get; set; } = "Open";
    public string Note { get; set; } = "";
    public ChecklistAttachmentRow? SelectedAttachment { get; set; }
    private PursuitRequirement? _selectedItem;
    public PursuitRequirement? SelectedItem
    {
        get => _selectedItem;
        set
        {
            _selectedItem = value; Status = value?.Status ?? "Open"; Note = value?.StatusNote ?? "";
            OnPropertyChanged(); OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(Note));
        }
    }
    private string _message = "";
    public string Message { get => _message; set { _message = value; OnPropertyChanged(); } }
    private bool _busy;
    public bool IsReady => !_busy;

    public async Task PerformAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true; OnPropertyChanged(nameof(IsReady));
        try { await action().ConfigureAwait(true); }
        catch (Exception ex)
        {
            Log.Warning(ex, "Pursuit checklist action failed.");
            Message = ex.Message;
        }
        finally { _busy = false; OnPropertyChanged(nameof(IsReady)); }
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        await ReloadItemsAsync(ct).ConfigureAwait(true);
        Attachments.Clear();
        foreach (var file in await files.ListByEngagementAsync(engagementId, ct).ConfigureAwait(true))
        {
            var row = new ChecklistAttachmentRow(file);
            Attachments.Add(row);
            if (!file.HasContent) { row.SetMetadata(null, "Legacy attachment: attach a database copy to check it."); continue; }
            if (!Path.GetExtension(file.FileName).Equals(".docx", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var draft = await ReadAsync(row, ct).ConfigureAwait(true);
                row.SetMetadata(draft.Metadata);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Warning(ex, "Reading draft metadata failed for attachment {Id}.", file.Id);
                row.SetMetadata(null, "Could not read Word metadata: " + ex.Message);
            }
        }
        SelectedAttachment = Attachments.FirstOrDefault(); OnPropertyChanged(nameof(SelectedAttachment));
    }

    private async Task ReloadItemsAsync(CancellationToken ct)
    {
        var selected = SelectedItem?.Id;
        var rows = await requirements.ListAsync(engagementId, ct).ConfigureAwait(true);
        Items.Clear(); foreach (var row in rows) Items.Add(row);
        SelectedItem = Items.FirstOrDefault(i => i.Id == selected) ?? Items.FirstOrDefault();
    }

    public async Task AddAsync(CancellationToken ct = default)
    {
        var input = new[] { new PursuitRequirementInput(Requirement, SourceRef, CheckKind,
            string.IsNullOrWhiteSpace(CheckParameter) ? null : CheckParameter) };
        PursuitRequirementValidation.Validate(input);
        await requirements.AddAsync(engagementId, input, actor, null, ct).ConfigureAwait(true);
        Message = "Item added.";
        await ReloadItemsAsync(ct).ConfigureAwait(true);
    }

    public async Task AddGenericAsync(CancellationToken ct = default)
    {
        await requirements.AddAsync(engagementId, GenericPursuitChecklist.Items, actor, GenericPursuitChecklist.Note, ct).ConfigureAwait(true);
        Message = "Generic checklist added. Verify each item against this RFP.";
        await ReloadItemsAsync(ct).ConfigureAwait(true);
    }

    public async Task SetStatusAsync(CancellationToken ct = default)
    {
        if (SelectedItem is not { } item) throw new InvalidOperationException("Select an item.");
        PursuitRequirementValidation.Required(Note, 500, "A status note");
        await requirements.UpdateStatusAsync(engagementId, item.Id, Status, Note, actor, false, ct).ConfigureAwait(true);
        Message = "Status saved.";
        await ReloadItemsAsync(ct).ConfigureAwait(true);
    }

    public async Task CheckDraftAsync(CancellationToken ct = default)
    {
        if (SelectedAttachment is not { } row) throw new InvalidOperationException("Select a bid attachment.");
        var draft = await ReadAsync(row, ct).ConfigureAwait(true);
        row.SetMetadata(draft.Metadata);
        // Read current requirements, not an old UI snapshot (an import may have replaced them).
        var current = await requirements.ListAsync(engagementId, ct).ConfigureAwait(true);
        var checkedCount = 0;
        try
        {
            foreach (var item in current)
            {
                if (PursuitDraftChecker.Check(item, draft, rfpNumber) is not { } result) continue;
                await requirements.UpdateStatusAsync(engagementId, item.Id, result.Status, result.Note, actor, true, ct).ConfigureAwait(true);
                checkedCount++;
            }
            Message = $"Checked {checkedCount} automatic items against {row.File.FileName}. Manual items remain for your review.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"Saved {checkedCount} automatic results before the check stopped. Reload before retrying. {ex.Message}", ex);
        }
        finally { await ReloadItemsAsync(ct).ConfigureAwait(true); }
    }

    private async Task<DraftDocument> ReadAsync(ChecklistAttachmentRow row, CancellationToken ct)
    {
        await using var source = await files.OpenContentAsync(row.File.Id, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Attachment bytes are unavailable. Attach a database copy first.");
        return await DraftDocument.ReadAsync(row.File.FileName, source, ct).ConfigureAwait(false);
    }
}

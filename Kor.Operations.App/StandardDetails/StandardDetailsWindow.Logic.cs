#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.SqlClient;
using Microsoft.Win32;
using Serilog;

namespace Kor.Operations.StandardDetails;

public partial class StandardDetailsWindow
{
    private async Task EnsureGroupSchemaStateAsync()
    {
        if (_repo == null)
            return;

        var result = await _repo.EnsureGroupSchemaAsync();
        _groupSchemaAvailable = result.GroupSchemaAvailable;
        if (_groupSchemaAvailable)
            return;

        _selectedGroupId = null;
        if (result.PermissionDenied)
        {
            SetActivityMessage("Group setup skipped due to SQL permissions. Records/revisions remain available.", BannerTone.Warning);
            Log.Warning("Standard Details: group schema bootstrap skipped (SQL permission).");
        }
        else if (result.SchemaMismatch)
        {
            SetActivityMessage("Group features unavailable due to database schema mismatch.", BannerTone.Warning);
            Log.Warning("Standard Details: group features unavailable (database schema mismatch).");
        }
        else
        {
            SetActivityMessage("Grouping is disabled until DB schema is installed by an administrator.", BannerTone.Warning);
            Log.Warning("Standard Details: group schema not present; runtime creation disabled.");
        }
    }

    private async Task LoadGroupsUiAsync()
    {
        var (expandedGroupIds, allRootExpanded) = SnapshotGroupTreeState();
        if (!_groupSchemaAvailable)
        {
            GroupsTree.ItemsSource = new[] { new GroupNode { GroupId = null, ParentGroupId = null, Name = "All Records" } };
            _selectedGroupId = null;
            _groupCount = 0;
            UpdateHeroMetrics();
            UpdateActionStates();
            return;
        }

        if (_repo == null)
            return;

        IReadOnlyList<StandardDetailsGroupRow> items;
        try
        {
            items = await _repo.LoadGroupsAsync();
        }
        catch (SqlException ex) when (ex.Number is 208 or 207)
        {
            _groupSchemaAvailable = false;
            _selectedGroupId = null;
            SetActivityMessage("Grouping disabled because required DB objects are missing.", BannerTone.Warning);
            Log.Warning(ex, "Standard Details: group schema missing while loading groups.");
            await LoadGroupsUiAsync();
            return;
        }

        var map = items.ToDictionary(x => x.GroupId, x => new GroupNode { GroupId = x.GroupId, ParentGroupId = x.ParentGroupId, Name = x.Name });
        var roots = new ObservableCollection<GroupNode>();
        foreach (var g in items)
        {
            if (g.ParentGroupId.HasValue && map.TryGetValue(g.ParentGroupId.Value, out var parent))
            {
                parent.Children.Add(map[g.GroupId]);
            }
            else
            {
                roots.Add(map[g.GroupId]);
            }
        }

        var allRoot = new GroupNode { GroupId = null, ParentGroupId = null, Name = "All Groups" };
        foreach (var root in roots.OrderBy(x => x.Name))
        {
            allRoot.Children.Add(root);
        }

        ApplyGroupExpansionState(allRoot, expandedGroupIds);
        allRoot.IsExpanded = allRootExpanded;
        if (_selectedGroupId.HasValue && FindGroupNode(allRoot, _selectedGroupId.Value) is { } selectedNode)
        {
            selectedNode.IsSelected = true;
            EnsureGroupAncestorsExpanded(allRoot, _selectedGroupId.Value);
        }

        GroupsTree.ItemsSource = new[] { allRoot };
        if (_selectedGroupId.HasValue && !items.Any(x => x.GroupId == _selectedGroupId.Value))
        {
            _selectedGroupId = null;
        }

        _groupCount = items.Count;
        UpdateHeroMetrics();
        UpdateActionStates();
    }

    private (HashSet<long> ExpandedGroupIds, bool AllRootExpanded) SnapshotGroupTreeState()
    {
        var expanded = new HashSet<long>();
        var allRootExpanded = true;
        if (GroupsTree.ItemsSource is not IEnumerable<GroupNode> roots)
        {
            return (expanded, allRootExpanded);
        }

        foreach (var root in roots)
        {
            if (root.GroupId is null)
                allRootExpanded = root.IsExpanded;
            CollectExpandedGroupIds(root, expanded);
        }

        return (expanded, allRootExpanded);
    }

    private static void CollectExpandedGroupIds(GroupNode node, ISet<long> expanded)
    {
        if (node.GroupId.HasValue && node.IsExpanded)
        {
            expanded.Add(node.GroupId.Value);
        }
        foreach (var child in node.Children)
        {
            CollectExpandedGroupIds(child, expanded);
        }
    }

    private static void ApplyGroupExpansionState(GroupNode node, ISet<long> expanded)
    {
        if (node.GroupId.HasValue)
        {
            node.IsExpanded = expanded.Contains(node.GroupId.Value);
        }
        foreach (var child in node.Children)
        {
            ApplyGroupExpansionState(child, expanded);
        }
    }

    private static GroupNode? FindGroupNode(GroupNode node, long groupId)
    {
        if (node.GroupId == groupId)
        {
            return node;
        }
        foreach (var child in node.Children)
        {
            if (FindGroupNode(child, groupId) is { } match)
            {
                return match;
            }
        }
        return null;
    }

    private static bool EnsureGroupAncestorsExpanded(GroupNode node, long groupId)
    {
        foreach (var child in node.Children)
        {
            if (child.GroupId == groupId || EnsureGroupAncestorsExpanded(child, groupId))
            {
                node.IsExpanded = true;
                return true;
            }
        }
        return false;
    }

    private async Task LoadDocumentsUiAsync()
    {
        if (_repo == null)
        {
            MessageBox.Show(this, "Missing connection string 'KorTransmittalsDb' in App.config", "Standard Details — Configuration", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var q = SearchBox.Text?.Trim() ?? string.Empty;
        var effectiveGroupId = _filterRecordsBySelectedGroup ? _selectedGroupId : null;

        IReadOnlyList<StandardDetailsDocumentRow> data;
        try
        {
            data = await _repo.LoadDocumentsAsync(_groupSchemaAvailable, q, effectiveGroupId);
        }
        catch (SqlException ex) when (_groupSchemaAvailable && ex.Number is 208 or 207)
        {
            _groupSchemaAvailable = false;
            _selectedGroupId = null;
            SetActivityMessage("Grouping disabled because required DB objects are missing.", BannerTone.Warning);
            Log.Warning(ex, "Standard Details: group schema missing while loading documents.");
            await LoadGroupsUiAsync();
            await LoadDocumentsUiAsync();
            return;
        }

        _documentSnapshot = data.Select(r => new DocumentRow
        {
            DocumentId = r.DocumentId,
            Title = r.Title,
            DetailNumber = r.DetailNumber ?? string.Empty,
            GroupName = r.GroupName,
            CurrentOfficialText = r.CurrentOfficialText,
            LatestStatusText = ToStatusText(r.LatestStatus)
        }).ToList();
        _versionSnapshot = new List<VersionRow>();
        UpdateHeroMetrics();
        DocumentsGrid.ItemsSource = _documentSnapshot;
        VersionsGrid.ItemsSource = null;
        UpdateSelectionSummary();
    }

    private async Task LoadVersionsUiAsync(long documentId)
    {
        if (_repo == null)
            return;

        _versionSnapshot = (await _repo.LoadVersionsAsync(documentId)).Select(r => new VersionRow
        {
            DocumentVersionId = r.DocumentVersionId,
            DocumentId = r.DocumentId,
            DocumentVariantId = r.DocumentVariantId,
            VariantKey = r.VariantKey,
            VersionNumber = r.VersionNumber,
            VersionLabel = $"v{r.VersionNumber}",
            Status = r.Status,
            StatusText = ToStatusText(r.Status),
            IsCurrentOfficial = r.IsCurrentOfficial,
            CreatedUtc = r.CreatedUtc,
            CreatedUtcDisplay = r.CreatedUtc.ToString("u"),
            OriginalFileName = r.OriginalFileName,
            FileSizeKb = Math.Round(r.ContentLengthBytes / 1024.0, 1),
            StoragePath = r.StoragePath,
            RowVersion = r.RowVersion
        }).ToList();
        UpdateHeroMetrics();
        VersionsGrid.ItemsSource = _versionSnapshot;
        ApplyVersionSummaryToSelectedDocument(_versionSnapshot);
        UpdateActionStates();
    }

    private void ApplyVersionSummaryToSelectedDocument(IReadOnlyList<VersionRow> versions)
    {
        if (DocumentsGrid.SelectedItem is not DocumentRow doc)
            return;

        doc.LatestStatusText = versions.FirstOrDefault()?.StatusText ?? "None";
        doc.CurrentOfficialText = versions.Select(x => x.DocumentVariantId).Distinct().Count() > 1
            ? "(per variant)"
            : versions.FirstOrDefault(x => x.IsCurrentOfficial)?.VersionLabel ?? "None";
        DocumentsGrid.Items.Refresh();
        UpdateHeroMetrics();
    }

    private static string ToStatusText(byte? status)
        => status switch
        {
            StatusDraft => "Draft",
            StatusSubmitted => "Submitted",
            StatusApproved => "Approved",
            StatusRejected => "Rejected",
            StatusPublished => "Published",
            5 => "Archived",
            _ => "None"
        };

    private async void Search_Click(object sender, RoutedEventArgs e) => await LoadDocumentsUiAsync();
    private async void Refresh_Click(object sender, RoutedEventArgs e) { await LoadGroupsUiAsync(); await LoadDocumentsUiAsync(); }

    private async void GroupsTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _selectedGroupId = (GroupsTree.SelectedItem as GroupNode)?.GroupId;
        if (_filterRecordsBySelectedGroup) { await LoadDocumentsUiAsync(); return; }
        if (DocumentsGrid.SelectedItem is DocumentRow selectedDoc && GroupsTree.SelectedItem is GroupNode selectedGroup)
            SetActivityMessage(selectedGroup.GroupId.HasValue ? $"Target group set to '{selectedGroup.Name}' for record '{selectedDoc.Title}'. Click Assign Record to apply." : "Target set to 'All Records'. Click Assign Record to move record to Ungrouped.", BannerTone.Info);
        else
            SetActivityMessage("Target group selected. Select a record and click Move Selected Record.", BannerTone.Info);
        UpdateActionStates();
    }

    private async void FilterByGroupCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        _filterRecordsBySelectedGroup = FilterByGroupCheckBox.IsChecked == true;
        await LoadDocumentsUiAsync();
        SetActivityMessage(_filterRecordsBySelectedGroup ? "Group filter is ON. Records list now follows tree selection." : "Group filter is OFF. Tree selection sets assignment target without filtering records.", BannerTone.Info);
    }

    private async void DocumentsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DocumentsGrid.SelectedItem is DocumentRow doc)
        {
            await LoadVersionsUiAsync(doc.DocumentId);
        }
        else
        {
            VersionsGrid.ItemsSource = null;
            UpdateActionStates();
        }

        UpdateSelectionSummary();
    }

    private void VersionsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelectionSummary();

    private void UpdateSelectionSummary()
    {
        // Selection detail now lives in the grids themselves; just refresh action states.
        UpdateActionStates();
    }

    private async void AddGroup_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureGroupFeatureAvailable() || !EnsureCanManageGroupAction("manage groups")) return;
        var dlg = new GroupEditWindow("Add Group", "Create a new top-level group.") { Owner = this };
        if (dlg.ShowDialog() != true || _repo == null) return;
        await _repo.AddGroupAsync(dlg.GroupName, _actorUserId); await LoadGroupsUiAsync();
    }

    private async void AddSubgroup_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureGroupFeatureAvailable() || !EnsureCanManageGroupAction("manage groups")) return;
        if (GroupsTree.SelectedItem is not GroupNode selected || selected.GroupId is null) { MessageBox.Show(this, "Select a parent group first.", "Standard Details — Add Subgroup", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var dlg = new GroupEditWindow("Add Subgroup", $"Create a subgroup under '{selected.Name}'.") { Owner = this };
        if (dlg.ShowDialog() != true || _repo == null) return;
        await _repo.AddSubgroupAsync(selected.GroupId.Value, dlg.GroupName, _actorUserId); await LoadGroupsUiAsync();
    }

    private async void RenameGroup_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureGroupFeatureAvailable() || !EnsureCanManageGroupAction("rename groups")) return;
        if (GroupsTree.SelectedItem is not GroupNode selected || selected.GroupId is null) { MessageBox.Show(this, "Select a group to rename.", "Standard Details — Rename Group", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var dlg = new GroupEditWindow("Rename Group", "Update group name.", selected.Name) { Owner = this };
        if (dlg.ShowDialog() != true || _repo == null) return;
        await _repo.RenameGroupAsync(selected.GroupId.Value, dlg.GroupName, _actorUserId); await LoadGroupsUiAsync(); await LoadDocumentsUiAsync();
    }

    private async void RemoveGroup_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureGroupFeatureAvailable() || !EnsureCanManageGroupAction("remove groups")) return;
        if (GroupsTree.SelectedItem is not GroupNode selected || selected.GroupId is null) { MessageBox.Show(this, "Select a group to remove.", "Standard Details — Remove Group", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (_repo == null) return;
        if (await _repo.GetActiveChildGroupCountAsync(selected.GroupId.Value) > 0) { MessageBox.Show(this, "This group has subgroups. Remove or move subgroups first.", "Standard Details — Remove Group", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (await _repo.GetDocumentCountForGroupAsync(selected.GroupId.Value) > 0) { MessageBox.Show(this, "This group has document records. Reassign or ungroup records before removing.", "Standard Details — Remove Group", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (MessageBox.Show(this, $"Remove group '{selected.Name}'?", "Standard Details — Confirm Remove", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await _repo.RemoveGroupAsync(selected.GroupId.Value); _selectedGroupId = null; await LoadGroupsUiAsync(); await LoadDocumentsUiAsync();
    }

    private async void AssignRecordToGroup_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureGroupFeatureAvailable() || !EnsureCanContribute("assign records to groups")) return;
        if (DocumentsGrid.SelectedItem is not DocumentRow doc) { SetActivityMessage("Choose a document record first, then assign it to a group.", BannerTone.Info); MessageBox.Show(this, "Select a document record first.", "Standard Details — Assign Record", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (GroupsTree.SelectedItem is not GroupNode selectedGroup) { MessageBox.Show(this, "Select a target group first. Choose 'All Records' to ungroup the record.", "Standard Details — Assign Record", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (selectedGroup.GroupId is null && !string.Equals(selectedGroup.Name, "All Records", StringComparison.OrdinalIgnoreCase)) { MessageBox.Show(this, "Select a specific group. To remove a group assignment, select 'All Records'.", "Standard Details — Assign Record", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (_repo == null) return;
        await _repo.AssignRecordToGroupAsync(doc.DocumentId, selectedGroup.GroupId, _actorUserId); await LoadDocumentsUiAsync();
        SetActivityMessage(selectedGroup.GroupId.HasValue ? $"Record '{doc.Title}' assigned to group '{selectedGroup.Name}'." : $"Record '{doc.Title}' moved to Ungrouped.", BannerTone.Success);
    }

    private async void CreateDocument_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureCanContribute("create records")) return;
        var dlg = new CreateStandardDocumentWindow { Owner = this };
        if (dlg.ShowDialog() != true) return;
        try { if (_repo == null) return; await _repo.CreateDocumentAsync(dlg.DocumentTitle, dlg.DocumentDescription, _groupSchemaAvailable, _selectedGroupId, _actorUserId); await LoadDocumentsUiAsync(); SetActivityMessage($"Created record '{dlg.DocumentTitle}'.", BannerTone.Success); }
        catch (Exception ex) { SetActivityMessage("Could not create the record. Please review the error and try again.", BannerTone.Error); MessageBox.Show(this, ex.Message, "Standard Details — Create Record Failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void UploadVersion_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureCanContribute("upload versions")) return;
        if (DocumentsGrid.SelectedItem is not DocumentRow doc) { SetActivityMessage("Choose a document record first, then upload a revision file.", BannerTone.Info); MessageBox.Show(this, "Select a document record first.", "Standard Details — Add Revision", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var picker = new OpenFileDialog { Title = "Select version file", Filter = "Allowed files (*.pdf;*.dwg;*.docx)|*.pdf;*.dwg;*.docx" };
        if (picker.ShowDialog(this) != true) return;
        var ext = System.IO.Path.GetExtension(picker.FileName).ToLowerInvariant();
        if (ext != ".pdf" && ext != ".dwg" && ext != ".docx") { SetActivityMessage("Upload blocked: only PDF, DWG, and DOCX files are supported.", BannerTone.Warning); MessageBox.Show(this, "Only PDF, DWG, DOCX are allowed.", "Standard Details — Add Revision", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        try { if (_repo == null || _fileStore == null) return; var nextVersion = await _repo.UploadVersionAsync(doc.DocumentId, doc.Title, picker.FileName, ext, _fileStore, _actorUserId); await LoadVersionsUiAsync(doc.DocumentId); SetActivityMessage($"Uploaded revision v{nextVersion} for '{doc.Title}'.", BannerTone.Success); }
        catch (Exception ex) { SetActivityMessage("Upload failed. The file was not added.", BannerTone.Error); MessageBox.Show(this, ex.Message, "Standard Details — Add Revision Failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void LinkDetail_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureCanContribute("link details")) return;
        if (DocumentsGrid.SelectedItem is not DocumentRow doc) { SetActivityMessage("Choose a document record first, then link it to a standard detail.", BannerTone.Info); MessageBox.Show(this, "Select a document record first.", "Standard Details — Link Detail", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (_korStandardsRepo == null) { SetActivityMessage("KorStandards catalog is not configured.", BannerTone.Info); MessageBox.Show(this, "KorStandards catalog is not configured.", "Standard Details — Link Detail", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var dlg = new LinkDetailWindow(_korStandardsRepo) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        try
        {
            if (_repo == null) return;
            var detailNumber = dlg.ClearRequested ? null : dlg.SelectedDetailNumber;
            await _repo.SetDocumentDetailNumberAsync(doc.DocumentId, detailNumber, _actorUserId);
            await LoadDocumentsUiAsync();
            SetActivityMessage(dlg.ClearRequested ? $"Removed detail link from '{doc.Title}'." : $"Linked '{doc.Title}' to {detailNumber}.", BannerTone.Success);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            SetActivityMessage("That detail is already linked to another record.", BannerTone.Warning);
            MessageBox.Show(this, "That detail (KOR-D-#####) is already linked to another record.", "Standard Details — Link Detail", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            SetActivityMessage("Could not update the detail link. Please review the error and try again.", BannerTone.Error);
            MessageBox.Show(this, ex.Message, "Standard Details — Link Detail Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PromotionQueue_Click(object sender, RoutedEventArgs e)
    {
        if (_repo == null)
        {
            MessageBox.Show(this, "Missing connection string 'KorTransmittalsDb' in App.config", "Standard Details — Promotion Queue", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var dlg = new PromotionQueueWindow(_repo, ProcessPendingPromotionsAsync) { Owner = this };
        dlg.ShowDialog();
    }

    private void Registers_Click(object sender, RoutedEventArgs e)
    {
        if (_repo == null || _korStandardsRepo == null)
        {
            MessageBox.Show(this, "KorStandards catalog is not configured; the registers are unavailable.", "Standard Details — Registers", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new RegistersWindow(_korStandardsRepo, _repo) { Owner = this };
        dlg.ShowDialog();
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (VersionsGrid.SelectedItem is not VersionRow version) { SetActivityMessage("Select a revision to open its file.", BannerTone.Info); MessageBox.Show(this, "Select a version first.", "Standard Details — Open File", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (_fileStore == null) return;
        // A published, detail-linked revision opens stamped TYPICAL + its KOR-D number.
        var linkedDetailNumber = (DocumentsGrid.SelectedItem as DocumentRow)?.DetailNumber;
        var result = _fileStore.OpenVersionFile(version.StoragePath, version.Status, version.StatusText, linkedDetailNumber);
        if (result.FileMissing) { SetActivityMessage("File could not be opened because it is missing from storage.", BannerTone.Warning); MessageBox.Show(this, "File not found in storage.", "Standard Details — Open File", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        SetActivityMessage(string.IsNullOrWhiteSpace(result.Note) ? $"Opened file '{version.OriginalFileName}'." : result.Note, string.IsNullOrWhiteSpace(result.Note) ? BannerTone.Success : BannerTone.Warning);
    }

    private async void Submit_Click(object sender, RoutedEventArgs e) { if (!EnsureCanContribute("submit versions")) return; await SubmitSelectedVersionAsync(StatusDraft, StatusSubmitted, true); }
    private async void Approve_Click(object sender, RoutedEventArgs e) { if (!EnsureCanApproveReject("approve versions")) return; await DecideSelectedVersionAsync(StatusApproved, 1); }
    private async void Reject_Click(object sender, RoutedEventArgs e) { if (!EnsureCanApproveReject("reject versions")) return; await DecideSelectedVersionAsync(StatusRejected, 2); }
    private async void Publish_Click(object sender, RoutedEventArgs e) { if (!EnsureCanPublishAction()) return; await PublishSelectedVersionAsync(); }

    private async void DeleteRecord_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureCanContribute("delete document records")) return;
        if (DocumentsGrid.SelectedItem is not DocumentRow doc) { SetActivityMessage("Select a document record first.", BannerTone.Info); MessageBox.Show(this, "Select a document record first.", "Standard Details — Delete Record", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (MessageBox.Show(this, $"Delete record '{doc.Title}' and all associated revisions?\n\nThis cannot be undone.", "Standard Details — Delete Record", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (MessageBox.Show(this, "Final confirmation: this will permanently remove record metadata, revision history, and approval/publication history.", "Standard Details — Confirm Permanent Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        StandardDetailsDeleteResult result;
        try { if (_repo == null) return; result = await _repo.DeleteRecordAsync(doc.DocumentId, doc.Title, _actorUserId); }
        catch (Exception ex) { SetActivityMessage("Delete failed. No changes were committed.", BannerTone.Error); MessageBox.Show(this, ex.Message, "Standard Details — Delete Record Failed", MessageBoxButton.OK, MessageBoxImage.Error); return; }
        if (result.EntityMissing) { SetActivityMessage("Selected record no longer exists. Refreshing list.", BannerTone.Warning); MessageBox.Show(this, "Selected record no longer exists.", "Standard Details — Delete Record", MessageBoxButton.OK, MessageBoxImage.Warning); await LoadDocumentsUiAsync(); return; }
        _fileStore?.DeleteFiles(result.DeletedStoragePaths); await LoadDocumentsUiAsync(); VersionsGrid.ItemsSource = null; SetActivityMessage($"Record '{doc.Title}' was deleted.", BannerTone.Success);
    }

    private async Task SubmitSelectedVersionAsync(byte expected, byte next, bool writeAudit)
    {
        if (VersionsGrid.SelectedItem is not VersionRow v) { SetActivityMessage("Select a revision first.", BannerTone.Info); MessageBox.Show(this, "Select a version first.", "Standard Details — Status", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (_repo == null) return;
        var result = await _repo.UpdateStatusAsync(v.DocumentVersionId, v.DocumentId, v.RowVersion, _actorUserId, expected, next, writeAudit);
        if (result.RowsAffected == 0) { await HandleStateChangeFailureAsync(v.DocumentId, result, expected, "Status", "Selected version is not in the expected state for this action.", "This action is not available for the selected revision status."); return; }
        await LoadVersionsUiAsync(v.DocumentId); SetActivityMessage(next == StatusSubmitted ? "Revision submitted for approval." : $"Revision moved to {ToStatusText(next)}.", BannerTone.Success);
    }

    private async Task DecideSelectedVersionAsync(byte targetStatus, int decision)
    {
        if (VersionsGrid.SelectedItem is not VersionRow v) { SetActivityMessage("Select a revision first.", BannerTone.Info); MessageBox.Show(this, "Select a version first.", "Standard Details — Approval", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (_repo == null) return;
        StandardDetailsStateChangeResult result;
        try
        {
            result = await _repo.DecideAsync(v.DocumentVersionId, v.DocumentId, v.RowVersion, _actorUserId, Environment.UserName, targetStatus, decision);
            if (result.RowsAffected == 0) { await HandleStateChangeFailureAsync(v.DocumentId, result, StatusSubmitted, "Approval", "Approve/Reject requires Submitted status.", "Approve/Reject is only available when status is Submitted."); return; }
        }
        catch (SqlException ex)
        {
            SetActivityMessage("Approval failed. No changes were committed.", BannerTone.Error);
            Log.Warning(ex, "Standard Details: approval decision failed for document version {DocumentVersionId}.", v.DocumentVersionId);
            MessageBox.Show(this, "Approval failed. No changes were committed.", "Standard Details - Approval", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        catch (Exception ex)
        {
            SetActivityMessage("Approval failed. No changes were committed.", BannerTone.Error);
            Log.Warning(ex, "Standard Details: approval decision failed for document version {DocumentVersionId}.", v.DocumentVersionId);
            MessageBox.Show(this, "Approval failed. No changes were committed.", "Standard Details - Approval", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        await LoadVersionsUiAsync(v.DocumentId);
        if (decision != 1)
        {
            SetActivityMessage("Revision rejected.", BannerTone.Success);
            return;
        }

        try
        {
            var promotionSummary = await ProcessPendingPromotionsAsync();
            var tone = promotionSummary.Contains("failed", StringComparison.OrdinalIgnoreCase) || promotionSummary.Contains("not configured", StringComparison.OrdinalIgnoreCase)
                ? BannerTone.Warning
                : BannerTone.Success;
            SetActivityMessage($"Revision approved. {promotionSummary}", tone);
        }
        catch (Exception ex)
        {
            SetActivityMessage("Revision approved, but promotion processing failed. Review the Promotion Queue.", BannerTone.Warning);
            Log.Warning(ex, "Standard Details: promotion processing failed after approval.");
        }
    }

    private async Task<string> ProcessPendingPromotionsAsync()
    {
        if (_repo == null)
        {
            return "Promotion queue is unavailable.";
        }

        if (_promoterRepo == null)
        {
            return "Promotion not configured; pending requests remain in queue.";
        }

        var pending = await _repo.LoadPendingOutboxAsync();
        var processed = 0;
        var failed = 0;

        foreach (var row in pending)
        {
            try
            {
                var requestedBy = string.IsNullOrWhiteSpace(row.RequestedByUserName) ? "operations" : row.RequestedByUserName!;
                var basis = $"Approved in Operations Standard Details by {requestedBy}";
                var result = await _promoterRepo.PromoteAsync(row.DetailNumber, row.TargetConfidence, basis, requestedBy);
                if (result.ok)
                {
                    await _repo.MarkOutboxDoneAsync(row.PromotionOutboxId, result.message);
                    processed++;
                }
                else
                {
                    await _repo.MarkOutboxFailedAsync(row.PromotionOutboxId, result.message);
                    failed++;
                }
            }
            catch (Exception ex)
            {
                failed++;
                Log.Warning(ex, "Standard Details: promotion outbox row {PromotionOutboxId} failed.", row.PromotionOutboxId);
                try
                {
                    await _repo.MarkOutboxFailedAsync(row.PromotionOutboxId, ex.Message);
                }
                catch (Exception markEx)
                {
                    Log.Warning(markEx, "Standard Details: failed to mark promotion outbox row {PromotionOutboxId} failed.", row.PromotionOutboxId);
                }
            }
        }

        return $"Promotion processing: processed {processed}, failed {failed}.";
    }

    private async Task PublishSelectedVersionAsync()
    {
        if (VersionsGrid.SelectedItem is not VersionRow v) { SetActivityMessage("Select a revision first.", BannerTone.Info); MessageBox.Show(this, "Select a version first.", "Standard Details — Publish", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (_repo == null) return;
        try
        {
            var result = await _repo.PublishAsync(v.DocumentVersionId, v.DocumentId, v.RowVersion, v.IsCurrentOfficial, _actorUserId);
            if (result.RowsAffected == 0) { await HandleStateChangeFailureAsync(v.DocumentId, result, StatusApproved, "Publish", "Publish requires Approved status.", "Publish is only available when status is Approved."); return; }
            await LoadVersionsUiAsync(v.DocumentId); SetActivityMessage("Revision published and set as the official version.", BannerTone.Success);
        }
        catch (SqlException ex)
        {
            SetActivityMessage("Publish failed. Try refreshing and publishing again.", BannerTone.Error);
            MessageBox.Show(this, ex.Message, "Standard Details — Publish Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task HandleStateChangeFailureAsync(long documentId, StandardDetailsStateChangeResult result, byte expectedStatus, string caption, string wrongStateMessage, string bannerMessage)
    {
        if (result.EntityMissing)
        {
            SetActivityMessage("Selected revision no longer exists. Refreshing list.", BannerTone.Warning);
            MessageBox.Show(this, "Selected revision no longer exists.", $"Standard Details — {caption}", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else if (result.CurrentStatus != expectedStatus)
        {
            SetActivityMessage(bannerMessage, BannerTone.Warning);
            MessageBox.Show(this, wrongStateMessage, $"Standard Details — {caption}", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            SetActivityMessage("Revision changed by another user. Refresh and retry.", BannerTone.Warning);
            MessageBox.Show(this, "Revision was updated by another user. Reload and try again.", "Standard Details — Concurrency Conflict", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        await LoadVersionsUiAsync(documentId);
    }

    private sealed class DocumentRow
    {
        public long DocumentId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string DetailNumber { get; set; } = string.Empty;
        public string GroupName { get; set; } = "Ungrouped";
        public string CurrentOfficialText { get; set; } = "None";
        public string LatestStatusText { get; set; } = "None";
    }

    private sealed class VersionRow
    {
        public long DocumentVersionId { get; init; }
        public long DocumentId { get; init; }
        public long DocumentVariantId { get; init; }
        public string VariantKey { get; init; } = string.Empty;
        public int VersionNumber { get; init; }
        public string VersionLabel { get; init; } = string.Empty;
        public byte Status { get; init; }
        public string StatusText { get; init; } = string.Empty;
        public bool IsCurrentOfficial { get; init; }
        public DateTime CreatedUtc { get; init; }
        public string CreatedUtcDisplay { get; init; } = string.Empty;
        public string OriginalFileName { get; init; } = string.Empty;
        public double FileSizeKb { get; init; }
        public string StoragePath { get; init; } = string.Empty;
        public byte[] RowVersion { get; init; } = Array.Empty<byte>();
    }

    private sealed class GroupNode
    {
        public long? GroupId { get; init; }
        public long? ParentGroupId { get; init; }
        public string Name { get; init; } = string.Empty;
        public ObservableCollection<GroupNode> Children { get; } = new();
        public bool IsExpanded { get; set; }
        public bool IsSelected { get; set; }
    }
}

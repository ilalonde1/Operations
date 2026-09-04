#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
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
        var q = SearchBox.Text?.Trim() ?? string.Empty;

        // The main list IS the standard details — the governed content the engineer approves — read
        // live from KorStandards. (Document records were an empty file-wrapper; the details are the point.)
        if (_korStandardsRepo != null)
        {
            try
            {
                if (_partsMode)
                {
                    // Parts = the Quick Insert catalog: governed, placeable, on the SAME confidence ladder
                    // as details. Palettes (Fasteners/Reinforcing/Symbols/...) are not the structural
                    // disciplines, so the discipline chips don't filter parts — only search does.
                    var parts = await _korStandardsRepo.LoadQuickInsertPartsAsync(q);
                    _documentSnapshot = parts.Select(p => new DocumentRow
                    {
                        DocumentId = 0,
                        IsDetail = false,
                        IsPart = true,
                        FamilyName = p.FamilyName,
                        TypeName = p.TypeName,
                        DetailNumber = string.IsNullOrWhiteSpace(p.Label) ? p.FamilyName : p.Label,
                        Title = string.IsNullOrWhiteSpace(p.TypeName) ? p.FamilyName : p.TypeName,
                        GroupName = p.Palette,
                        StatusLabel = PartStatusLabel(p),
                        RightSubtitle = $"{p.FamilyName}  ·  {p.Palette}  ·  {StatusWord(PartStatusLabel(p))}"
                    }).ToList();
                }
                else
                {
                    var details = await _korStandardsRepo.LoadPaletteDetailsAsync(q, _selectedDiscipline, _selectedKind, isSheet: _sheetsMode, orderByViewGroup: _sheetsMode);
                    _documentSnapshot = details.Select(d => new DocumentRow
                    {
                        DocumentId = 0,
                        IsDetail = true,
                        IsSheet = d.IsSheet,
                        Title = d.Title,
                        DetailNumber = d.DetailNumber,
                        Kind = d.Kind,
                        ViewGroup = d.ViewGroup,
                        GroupName = _sheetsMode ? SheetCollectionDisplay(d.ViewGroup) : string.IsNullOrWhiteSpace(d.Discipline) ? "Ungrouped" : d.Discipline,
                        CurrentOfficialText = d.IsPlaceable ? "Yes" : (d.VariantsDiverge ? "Diverges" : "No"),
                        LatestStatusText = string.IsNullOrWhiteSpace(d.Confidence) ? "unverified" : d.Confidence,
                        StatusLabel = DetailStatusLabel(d),
                        RightSubtitle = _sheetsMode
                            ? $"{d.DetailNumber}  ·  {SheetCollectionDisplay(d.ViewGroup)}  ·  {DetailTypeDisplay(d.Kind, d.IsSheet)}  ·  {StatusWord(DetailStatusLabel(d))}"
                            : $"{d.DetailNumber}  ·  {(string.IsNullOrWhiteSpace(d.Discipline) ? "Ungrouped" : d.Discipline)}  ·  {DetailTypeDisplay(d.Kind, d.IsSheet)}  ·  {StatusWord(DetailStatusLabel(d))}"
                    }).ToList();
                }
                _versionSnapshot = new List<VersionRow>();
                UpdateHeroMetrics();
                ApplyDocumentsItemsSource();
                VersionsGrid.ItemsSource = null;
                UpdateDetailPane(null);
                UpdateSelectionSummary();
                return;
            }
            catch (Exception ex)
            {
                SetActivityMessage(_partsMode ? "Could not load parts from KorStandards." : "Could not load standard details from KorStandards.", BannerTone.Warning);
                Log.Warning(ex, "Standard Details: loading {Mode} from KorStandards failed.", _partsMode ? "parts" : "details");
            }
        }

        if (_repo == null)
        {
            MessageBox.Show(this, "Missing connection string 'KorTransmittalsDb' in App.config", "Standard Details — Configuration", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

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
        ApplyDocumentsItemsSource();
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

    private void ToolsButton_Click(object sender, RoutedEventArgs e)
    {
        if (ToolsButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = ToolsButton;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await LoadDocumentsUiAsync();
    }

    private async void DisciplineChip_Checked(object sender, RoutedEventArgs e)
    {
        if (!_uiReady || sender is not RadioButton rb) return;
        var label = rb.Content as string;
        _selectedDiscipline = string.Equals(label, "All", StringComparison.OrdinalIgnoreCase) ? null : label;
        await LoadDocumentsUiAsync();
    }

    private async void KindFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady)
        {
            return;
        }

        _selectedKind = SelectedKindValue(KindFilterCombo);
        await LoadDocumentsUiAsync();
    }

    private async void CatalogTab_Checked(object sender, RoutedEventArgs e)
    {
        var parts = ReferenceEquals(sender, PartsTab);
        var sheets = ReferenceEquals(sender, SheetsTab);
        if (!_uiReady || (parts == _partsMode && sheets == _sheetsMode)) return;
        _partsMode = parts;
        _sheetsMode = sheets;
        ListIdColumn.Header = _partsMode ? "PART" : _sheetsMode ? "SHEET" : "DETAIL #";
        ListGroupColumn.Header = _partsMode ? "PALETTE" : _sheetsMode ? "COLLECTION" : "DISCIPLINE";
        KindFilterCombo.IsEnabled = !_partsMode;
        ApplyCatalogModeLayout(null);
        await LoadDocumentsUiAsync();
    }

    private void PreviewImage_Click(object sender, MouseButtonEventArgs e)
    {
        if (PreviewImage.Source is null) return;
        var zoom = new DetailZoomWindow(PreviewImage.Source, DetailTitleText.Text) { Owner = this };
        zoom.ShowDialog();
    }

    // Tools ▸ Sync Part Images: reads every Quick Insert thumbnail (fastener/bolt .png + QuickPick .bmp)
    // and upserts it into the DB art store the app shows. Production Quick Insert reads the files, not the
    // store, so this never touches production — it just refreshes what reviewers see.
    private async void SyncPartImages_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureCanPublishAction()) return;
        if (_korStandardsRepo == null || _promoterRepo == null)
        {
            MessageBox.Show(this, "KorStandards is not fully configured (reader + promoter required).", "Standard Details — Sync Part Images", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (MessageBox.Show(this, "Re-sync all Quick Insert part thumbnails into the image store?" + Environment.NewLine + Environment.NewLine + "Reads the current thumbnails and refreshes the DB store the app shows. Production Quick Insert is not affected.", "Standard Details — Sync Part Images", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;

        SetActivityMessage("Syncing part images...", BannerTone.Info);
        ToolsButton.IsEnabled = false;
        try
        {
            var (total, done, missing, failed) = await Task.Run(SyncPartImagesCoreAsync);
            SetActivityMessage($"Part images synced: {done} updated, {missing} missing, {failed} failed (of {total}).", failed > 0 ? BannerTone.Warning : BannerTone.Success);
            if (_partsMode) await LoadDocumentsUiAsync();
        }
        catch (Exception ex)
        {
            SetActivityMessage("Part image sync failed.", BannerTone.Error);
            Log.Warning(ex, "Standard Details: part image sync failed.");
            MessageBox.Show(this, ex.Message, "Standard Details — Sync Part Images Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ToolsButton.IsEnabled = true;
        }
    }

    private async Task<(int total, int done, int missing, int failed)> SyncPartImagesCoreAsync()
    {
        var root = string.IsNullOrWhiteSpace(_partImageRoot) ? @"\\Kor-fs01\Drafting\2026\QuickPick\BMP" : _partImageRoot;
        var refs = await _korStandardsRepo!.LoadComponentImageRefsAsync();
        int done = 0, missing = 0, failed = 0;
        foreach (var cr in refs)
        {
            try
            {
                var path = System.IO.Path.IsPathRooted(cr.ImageFile) ? cr.ImageFile : System.IO.Path.Combine(root, cr.ImageFile);
                if (!System.IO.File.Exists(path)) { missing++; continue; }
                var (png, w, h) = LoadImageAsPng(path);
                if (png.Length == 0) { failed++; continue; }
                var (ok, _) = await _promoterRepo!.SetRenderedImageAsync("component", $"{cr.FamilyName}|{cr.TypeName}", png, w, h, "sync-thumb");
                if (ok) done++; else failed++;
            }
            catch (Exception ex)
            {
                failed++;
                Log.Warning(ex, "Standard Details: sync image failed for {Family}/{Type}.", cr.FamilyName, cr.TypeName);
            }
        }
        return (refs.Count, done, missing, failed);
    }

    // Loads any WPF-decodable image (BMP/PNG/…) and re-encodes it as PNG bytes for the store.
    private static (byte[] png, int w, int h) LoadImageAsPng(string path)
    {
        var bytes = System.IO.File.ReadAllBytes(path);
        using var ms = new System.IO.MemoryStream(bytes);
        var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(ms, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(frame));
        using var outMs = new System.IO.MemoryStream();
        encoder.Save(outMs);
        return (outMs.ToArray(), frame.PixelWidth, frame.PixelHeight);
    }

    // Parts ride the same confidence ladder as details, so the same status vocabulary applies.
    private static string PartStatusLabel(QuickInsertPartRow p)
    {
        if (string.Equals(p.Confidence, "rejected", StringComparison.OrdinalIgnoreCase)) return "Rejected";
        if (p.IsPlaceable) return "Approved";
        if (string.Equals(p.Confidence, "content-verified", StringComparison.OrdinalIgnoreCase)
            || string.Equals(p.Confidence, "human-confirmed", StringComparison.OrdinalIgnoreCase)) return "Held";
        return "Pending";
    }

    private static string StatusWord(string label) => label switch
    {
        "Approved" => "Approved",
        "Pending" => "Pending",
        "Held" => "On hold",
        "Rejected" => "Rejected",
        _ => label
    };

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
        var selected = DocumentsGrid.SelectedItem as DocumentRow;
        // Only the legacy document-record rows (real DocumentId) have version history; details and parts
        // do not, so don't fire a spurious version query for them.
        if (selected is { IsDetail: false, DocumentId: > 0 } record)
        {
            await LoadVersionsUiAsync(record.DocumentId);
        }
        else
        {
            _versionSnapshot = new List<VersionRow>();
            VersionsGrid.ItemsSource = null;
            UpdateActionStates();
        }

        UpdateDetailPane(selected);
        await LoadPreviewAsync(selected);
        UpdateSelectionSummary();
    }

    private void VersionsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelectionSummary();

    private void UpdateSelectionSummary()
    {
        // Selection detail now lives in the grids themselves; just refresh action states.
        UpdateActionStates();
    }

    private void ApplyDocumentsItemsSource()
    {
        if (_sheetsMode && !_partsMode)
        {
            var view = CollectionViewSource.GetDefaultView(_documentSnapshot);
            view.GroupDescriptions.Clear();
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DocumentRow.GroupName)));
            DocumentsGrid.ItemsSource = view;
            return;
        }

        DocumentsGrid.ItemsSource = _documentSnapshot;
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

    private async void PublishToMaster_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureCanPublishAction())
        {
            return;
        }

        await PublishToMasterAsync();
    }

    private async void ComposeSheet_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureCanPublishAction())
        {
            return;
        }

        if (_repo == null || _korStandardsRepo == null)
        {
            SetActivityMessage("Standard Details sheet composer is not configured.", BannerTone.Warning);
            MessageBox.Show(this, "KorStandards and Standard Details databases must both be configured.", "Standard Details - Sheet Composer", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_masterPublishOptions is not { IsConfigured: true } options)
        {
            SetActivityMessage("Sheet composer settings are incomplete.", BannerTone.Warning);
            MessageBox.Show(this, "App.config must define StandardDetails.AuthoringPath, StandardDetails.MasterPath, and StandardDetails.BridgeRoot.", "Standard Details - Sheet Composer", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var bridge = new DrafterBridgeClient(options.BridgeRoot);
        var composer = new StandardDetailsSheetComposer(bridge, options);
        var dlg = new SheetComposerWindow(_korStandardsRepo, _repo, composer, _groupSchemaAvailable, _selectedGroupId, _actorUserId, _selectedDiscipline, _selectedKind) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            SetActivityMessage("Composed Standard Details sheet and created governance record.", BannerTone.Success);
            await LoadDocumentsUiAsync();
        }
    }

    private async Task PublishToMasterAsync()
    {
        if (_korStandardsRepo == null)
        {
            SetActivityMessage("KorStandards catalog is not configured.", BannerTone.Warning);
            MessageBox.Show(this, "KorStandards catalog is not configured.", "Standard Details - Publish to Master", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_masterPublishOptions is not { IsConfigured: true } options)
        {
            SetActivityMessage("Publish-to-master settings are incomplete.", BannerTone.Warning);
            MessageBox.Show(this, "App.config must define StandardDetails.AuthoringPath, StandardDetails.MasterPath, and StandardDetails.BridgeRoot.", "Standard Details - Publish to Master", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"Rebuild MASTER from approved AUTHORING details?\n\nAUTHORING:\n{options.AuthoringPath}\n\nMASTER:\n{options.MasterPath}\n\nThe live MASTER file is replaced only after verification succeeds.",
            "Standard Details - Publish to Master",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        PublishToMasterButton.IsEnabled = false;
        SetActivityMessage("Publishing approved details to MASTER...", BannerTone.Info);

        try
        {
            var bridge = new DrafterBridgeClient(options.BridgeRoot);
            var publisher = new MasterPublisher(bridge, _korStandardsRepo, options);
            var result = await publisher.PublishAsync(TimeSpan.FromMinutes(15));
            var summary = BuildMasterPublishSummary(result);

            SetActivityMessage($"Published MASTER: removed {result.RemovedViews.Count} view(s); verified {result.MasterDetailCount} detail view(s).", BannerTone.Success);
            MessageBox.Show(this, summary, "Standard Details - Publish to Master", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            SetActivityMessage("Publish to MASTER failed. Review the error before retrying.", BannerTone.Error);
            Log.Warning(ex, "Standard Details: publish to MASTER failed.");
            MessageBox.Show(this, ex.Message, "Standard Details - Publish to Master Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            UpdateActionStates();
        }
    }

    private static string BuildMasterPublishSummary(MasterPublishResult result)
    {
        var text = new StringBuilder();
        text.AppendLine("MASTER publish completed.");
        text.AppendLine();
        text.AppendLine($"Approved details: {result.ApprovedCount}");
        text.AppendLine($"AUTHORING KOR-D views: {result.AuthoringDetailCount}");
        text.AppendLine($"Removed non-approved KOR-D views: {result.RemovedViews.Count}");
        text.AppendLine($"MASTER KOR-D views after verification: {result.MasterDetailCount}");
        text.AppendLine($"Verified: {result.Verified}");

        if (result.RemovedViews.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Removed:");
            foreach (var view in result.RemovedViews.Take(20))
            {
                text.AppendLine($"- {view.DetailNumber} ({view.ViewId}) {view.ViewName}");
            }

            if (result.RemovedViews.Count > 20)
            {
                text.AppendLine($"- ... {result.RemovedViews.Count - 20} more");
            }
        }

        if (result.ApprovedMissingFromAuthoring.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Approved but not found in AUTHORING:");
            foreach (var detailNumber in result.ApprovedMissingFromAuthoring.Take(20))
            {
                text.AppendLine($"- {detailNumber}");
            }

            if (result.ApprovedMissingFromAuthoring.Count > 20)
            {
                text.AppendLine($"- ... {result.ApprovedMissingFromAuthoring.Count - 20} more");
            }
        }

        return text.ToString();
    }

    private void Registers_Click(object sender, RoutedEventArgs e)
    {
        if (_repo == null || _korStandardsRepo == null)
        {
            MessageBox.Show(this, "KorStandards catalog is not configured; the registers are unavailable.", "Standard Details — Registers", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new RegistersWindow(_korStandardsRepo, _repo, _promoterRepo, _policy?.CanApproveOrReject() == true, _userIdentity) { Owner = this };
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
    private async void Approve_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureCanApproveReject("approve")) return;
        if (DocumentsGrid.SelectedItem is DocumentRow { IsDetail: true } detail) { await DecideSelectedDetailAsync(detail, "human-confirmed", "Approve"); return; }
        if (DocumentsGrid.SelectedItem is DocumentRow { IsPart: true } part) { await DecideSelectedComponentAsync(part, "human-confirmed", "Approve"); return; }
        await DecideSelectedVersionAsync(StatusApproved, 1);
    }

    private async void Reject_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureCanApproveReject("reject")) return;
        if (DocumentsGrid.SelectedItem is DocumentRow { IsDetail: true } detail) { await DecideSelectedDetailAsync(detail, "rejected", "Reject"); return; }
        if (DocumentsGrid.SelectedItem is DocumentRow { IsPart: true } part) { await DecideSelectedComponentAsync(part, "rejected", "Reject"); return; }
        await DecideSelectedVersionAsync(StatusRejected, 2);
    }

    private async void DetailTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || _syncingTypeUi)
        {
            return;
        }

        if (DocumentsGrid.SelectedItem is not DocumentRow { IsDetail: true } detail)
        {
            return;
        }

        DetailTypeSavedText.Visibility = Visibility.Collapsed;

        if (!EnsureCanApproveReject("classify details"))
        {
            SetDetailTypeControl(detail, true);
            return;
        }

        if (_promoterRepo == null)
        {
            SetActivityMessage("KorStandards promoter is not configured.", BannerTone.Warning);
            MessageBox.Show(this, "KorStandards promoter is not configured (App.config: KorStandardsPromoterDb).", "Standard Details - Detail Type", MessageBoxButton.OK, MessageBoxImage.Warning);
            SetDetailTypeControl(detail, true);
            return;
        }

        var detailType = SelectedDetailTypeValue(DetailTypeCombo);
        var typeFields = DetailTypeFields(detailType);
        if (string.Equals(detail.Kind, typeFields.Kind, StringComparison.OrdinalIgnoreCase) && detail.IsSheet == typeFields.IsSheet)
        {
            return;
        }

        try
        {
            var (ok, message) = await _promoterRepo.SetDetailTypeAsync(detail.DetailNumber, detailType);
            if (!ok)
            {
                SetActivityMessage(message, BannerTone.Error);
                MessageBox.Show(this, message, "Standard Details - Detail Type", MessageBoxButton.OK, MessageBoxImage.Error);
                SetDetailTypeControl(detail, true);
                return;
            }

            SetActivityMessage(message, BannerTone.Success);
            var detailNumber = detail.DetailNumber;
            if (typeFields.IsSheet != _sheetsMode || _partsMode)
            {
                _partsMode = false;
                _sheetsMode = typeFields.IsSheet;
                DetailsTab.IsChecked = !typeFields.IsSheet;
                SheetsTab.IsChecked = typeFields.IsSheet;
                ListIdColumn.Header = _sheetsMode ? "SHEET" : "DETAIL #";
                ListGroupColumn.Header = _sheetsMode ? "COLLECTION" : "DISCIPLINE";
                KindFilterCombo.IsEnabled = true;
                ApplyCatalogModeLayout(null);
            }

            await LoadDocumentsUiAsync();
            var refreshed = _documentSnapshot.FirstOrDefault(x => x.IsDetail && string.Equals(x.DetailNumber, detailNumber, StringComparison.OrdinalIgnoreCase));
            if (refreshed is not null)
            {
                DocumentsGrid.SelectedItem = refreshed;
                DocumentsGrid.ScrollIntoView(refreshed);
            }

            DetailTypeSavedText.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            SetActivityMessage("Detail type update failed. No changes were committed.", BannerTone.Error);
            Log.Warning(ex, "Standard Details: detail type update failed for {DetailNumber}.", detail.DetailNumber);
            MessageBox.Show(this, ex.Message, "Standard Details - Detail Type", MessageBoxButton.OK, MessageBoxImage.Error);
            SetDetailTypeControl(detail, true);
        }
    }

    private async void OpenSheetPdf_Click(object sender, RoutedEventArgs e)
    {
        if (_openingCatalogPdf)
        {
            return;
        }

        if (DocumentsGrid.SelectedItem is not DocumentRow { IsDetail: true } detail)
        {
            MessageBox.Show(this, "Select a detail or sheet first.", "Standard Details - Open PDF", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_korStandardsRepo is null)
        {
            SetActivityMessage("KorStandards catalog is not configured.", BannerTone.Warning);
            MessageBox.Show(this, "KorStandards catalog is not configured.", "Standard Details - Open PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var options = _masterPublishOptions ?? new StandardDetailsMasterPublishOptions("", "", "");
        var bridgeRoot = string.IsNullOrWhiteSpace(options.BridgeRoot) ? "." : options.BridgeRoot;
        _openingCatalogPdf = true;
        OpenSheetPdfButton.IsEnabled = false;
        OpenSheetPdfButtonText.Text = "Generating…";
        try
        {
            SetActivityMessage($"Generating PDF for {detail.DetailNumber}...", BannerTone.Info);
            var composer = new StandardDetailsSheetComposer(new DrafterBridgeClient(bridgeRoot), options);
            await composer.OpenDetailPdfAsync(detail.DetailNumber, _korStandardsRepo, TimeSpan.FromMinutes(5));
            SetActivityMessage($"Opened PDF for {detail.DetailNumber}.", BannerTone.Success);
        }
        catch (Exception ex)
        {
            SetActivityMessage("Catalog PDF could not be opened.", BannerTone.Error);
            Log.Warning(ex, "Standard Details: catalog PDF open failed for {DetailNumber}.", detail.DetailNumber);
            ShowScrollableMessage("Standard Details - Open PDF Failed", ex.Message, MessageBoxImage.Error);
        }
        finally
        {
            _openingCatalogPdf = false;
            OpenSheetPdfButtonText.Text = "Open PDF";
            UpdateActionStates();
            SetActivityMessage("Ready.", BannerTone.Info);
        }
    }

    private void ShowScrollableMessage(string title, string message, MessageBoxImage icon)
    {
        _ = icon;
        var dialog = new Window
        {
            Owner = this,
            Title = title,
            Width = 760,
            Height = 360,
            MinWidth = 560,
            MinHeight = 260,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize
        };

        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var text = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(message) ? "The PDF could not be opened." : message,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(8)
        };
        Grid.SetRow(text, 0);
        grid.Children.Add(text);

        var okButton = new Button
        {
            Content = "OK",
            Width = 90,
            Height = 30,
            IsDefault = true,
            IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        okButton.Click += (_, _) => dialog.Close();
        Grid.SetRow(okButton, 1);
        grid.Children.Add(okButton);

        dialog.Content = grid;
        dialog.ShowDialog();
    }

    private async Task DecideSelectedDetailAsync(DocumentRow detail, string toConfidence, string verb)
    {
        if (_promoterRepo == null) { MessageBox.Show(this, "KorStandards promoter is not configured (App.config: KorStandardsPromoterDb).", "Standard Details — Approval", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (string.IsNullOrWhiteSpace(detail.DetailNumber)) return;
        if (MessageBox.Show(this, $"{verb} {detail.DetailNumber} — {detail.Title}?" + Environment.NewLine + Environment.NewLine + $"Sets its confidence to '{toConfidence}' in KorStandards, journaled to DetailHistory.", $"Standard Details — {verb}", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        try
        {
            var basis = $"Set to {toConfidence} in Standard Details by {_userIdentity}";
            var (ok, message) = await _promoterRepo.PromoteAsync(detail.DetailNumber, toConfidence, basis, _userIdentity);
            if (!ok)
            {
                SetActivityMessage(message, BannerTone.Error);
                MessageBox.Show(this, message, $"Standard Details — {verb}", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            SetActivityMessage(message, BannerTone.Success);
            await LoadDocumentsUiAsync();
        }
        catch (Exception ex)
        {
            SetActivityMessage("Approval failed. No changes were committed.", BannerTone.Error);
            Log.Warning(ex, "Standard Details: detail promotion failed for {DetailNumber}.", detail.DetailNumber);
            MessageBox.Show(this, ex.Message, $"Standard Details — {verb}", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Parts approve/reject through the SAME confidence ladder as details — detail.PromoteComponent,
    // keyed on family + type, journaled to ComponentHistory.
    private async Task DecideSelectedComponentAsync(DocumentRow part, string toConfidence, string verb)
    {
        if (_promoterRepo == null) { MessageBox.Show(this, "KorStandards promoter is not configured (App.config: KorStandardsPromoterDb).", "Standard Details — Approval", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (string.IsNullOrWhiteSpace(part.FamilyName)) return;
        var label = string.IsNullOrWhiteSpace(part.TypeName) ? part.FamilyName : $"{part.FamilyName} / {part.TypeName}";
        if (MessageBox.Show(this, $"{verb} {label}?" + Environment.NewLine + Environment.NewLine + $"Sets its confidence to '{toConfidence}' in KorStandards, journaled to ComponentHistory.", $"Standard Details — {verb}", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        try
        {
            var basis = $"Set to {toConfidence} in Standard Details by {_userIdentity}";
            var (ok, message) = await _promoterRepo.PromoteComponentAsync(part.FamilyName, part.TypeName, toConfidence, basis, _userIdentity);
            if (!ok)
            {
                SetActivityMessage(message, BannerTone.Error);
                MessageBox.Show(this, message, $"Standard Details — {verb}", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            SetActivityMessage(message, BannerTone.Success);
            await LoadDocumentsUiAsync();
        }
        catch (Exception ex)
        {
            SetActivityMessage("Approval failed. No changes were committed.", BannerTone.Error);
            Log.Warning(ex, "Standard Details: component promotion failed for {Family}/{Type}.", part.FamilyName, part.TypeName);
            MessageBox.Show(this, ex.Message, $"Standard Details — {verb}", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

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

    private static string DetailStatusLabel(PaletteDetailRow d)
    {
        if (string.Equals(d.Confidence, "rejected", StringComparison.OrdinalIgnoreCase)) return "Rejected";
        if (d.IsPlaceable) return "Approved";
        if (string.Equals(d.Confidence, "content-verified", StringComparison.OrdinalIgnoreCase)
            || string.Equals(d.Confidence, "human-confirmed", StringComparison.OrdinalIgnoreCase)) return "Held";
        return "Pending";
    }

    private static string DetailTypeDisplay(string? kind, bool isSheet)
        => DetailTypeFields(DetailTypeValue(kind, isSheet)).Display;

    private static string SheetCollectionDisplay(string? viewGroup)
        => string.IsNullOrWhiteSpace(viewGroup) ? "Uncollected" : viewGroup;

    private static string? SelectedKindValue(ComboBox combo)
    {
        if (combo.SelectedItem is not ComboBoxItem item || item.Tag is not string value)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string SelectedDetailTypeValue(ComboBox combo)
    {
        if (combo.SelectedItem is not ComboBoxItem item || item.Tag is not string value)
        {
            return "typical";
        }

        return string.IsNullOrWhiteSpace(value) ? "typical" : value;
    }

    private static string DetailTypeValue(DocumentRow detail)
        => DetailTypeValue(detail.Kind, detail.IsSheet);

    private static string DetailTypeValue(string? kind, bool isSheet)
    {
        if (isSheet)
        {
            return "note-schedule";
        }

        return string.Equals(kind, "custom", StringComparison.OrdinalIgnoreCase) ? "custom" : "typical";
    }

    private static (string Kind, bool IsSheet, string Display) DetailTypeFields(string type)
        => type switch
        {
            "custom" => ("custom", false, "Custom detail"),
            "note-schedule" => ("general-note", true, "Note / schedule"),
            _ => ("typical", false, "Typical detail")
        };

    private void SetDetailTypeControl(DocumentRow? detail, bool visible)
        => SetDetailTypeControl(detail is null ? null : DetailTypeValue(detail), visible);

    private void SetDetailTypeControl(string? detailType, bool visible)
    {
        _syncingTypeUi = true;
        try
        {
            DetailTypePanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            DetailTypeSavedText.Visibility = Visibility.Collapsed;
            foreach (var item in DetailTypeCombo.Items.OfType<ComboBoxItem>())
            {
                var value = item.Tag as string ?? string.Empty;
                if (string.Equals(value, detailType ?? "typical", StringComparison.OrdinalIgnoreCase))
                {
                    DetailTypeCombo.SelectedItem = item;
                    break;
                }
            }
            OpenSheetPdfButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _syncingTypeUi = false;
        }

        DetailTypeCombo.IsEnabled = visible && _promoterRepo != null && _policy?.CanApproveOrReject() == true;
        OpenSheetPdfButton.IsEnabled = visible && _korStandardsRepo != null && !_openingCatalogPdf;
    }

    private sealed class DocumentRow
    {
        public long DocumentId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string DetailNumber { get; set; } = string.Empty;
        public string GroupName { get; set; } = "Ungrouped";
        public string CurrentOfficialText { get; set; } = "None";
        public string LatestStatusText { get; set; } = "None";
        public string StatusLabel { get; set; } = "";
        public string RightSubtitle { get; set; } = "";
        public string Kind { get; set; } = string.Empty;
        public bool IsSheet { get; set; }
        public string ViewGroup { get; set; } = string.Empty;
        public bool IsDetail { get; set; }
        public bool IsPart { get; set; }
        public string FamilyName { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
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

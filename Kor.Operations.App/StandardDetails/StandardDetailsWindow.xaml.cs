using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Data;
using Kor.Operations.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Win32;

namespace Kor.Operations.StandardDetails;

public partial class StandardDetailsWindow : Window
{
    private const byte StatusDraft = 0;
    private const byte StatusSubmitted = 1;
    private const byte StatusApproved = 2;
    private const byte StatusRejected = 3;
    private const byte StatusPublished = 4;
    private const int DocumentTitleMax = 300;
    private const int DocumentDescriptionMax = 2000;
    private const int FileStoragePathMax = 1024;
    private const int FileNameMax = 260;
    private const int FileExtensionMax = 10;
    private const int ContentTypeMax = 127;
    private const int Sha256HexLength = 64;
    private const int RowVersionLength = 8;

    private readonly Guid _actorUserId = CreateStableUserGuid(Environment.UserName);
    private long? _selectedGroupId;
    private string _currentUserIdentity = string.Empty;
    private int _groupCount;
    private List<DocumentRow> _documentSnapshot = new();
    private List<VersionRow> _versionSnapshot = new();
    private bool _groupSchemaAvailable = true;
    private bool _filterRecordsBySelectedGroup;
    private enum BannerTone { Info, Success, Warning, Error }

    public StandardDetailsWindow()
    {
        InitializeComponent();
        Loaded += StandardDetailsWindow_Loaded;
    }

    private async void StandardDetailsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await HeaderLoader.ApplyAsync(HeaderBar);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"HeaderLoader failed: {ex.Message}");
        }

        _currentUserIdentity = ResolveCurrentUserIdentity();
        _filterRecordsBySelectedGroup = FilterByGroupCheckBox.IsChecked == true;
        await EnsureGroupSchemaAsync();
        await LoadGroupsAsync();
        await LoadDocumentsAsync();
        UpdateActionStates();
        SetActivityMessage("Ready.", BannerTone.Info);
    }

    private static Guid CreateStableUserGuid(string input)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input ?? "unknown"));
        var b = new byte[16];
        Array.Copy(hash, b, 16);
        return new Guid(b);
    }

    private static string? GetConnectionString()
        => ConfigurationManager.ConnectionStrings["KorTransmittalsDb"]?.ConnectionString;

    private static string GetStorageRoot()
    {
        var configured = ConfigurationManager.AppSettings["StandardDetails.FileStorageRootPath"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return @"\\Kor-fs01\Drafting";
        }

        var value = configured.Trim().Trim('"');

        // App.config values are not C# string literals; accept accidentally escaped UNC values.
        // Example input: \\\\Kor-fs01\\Drafting -> \\Kor-fs01\Drafting
        if (value.StartsWith(@"\\\\", StringComparison.Ordinal))
        {
            value = @"\\" + value[4..].Replace(@"\\", @"\");
        }

        return value;
    }

    private string ResolveCurrentUserIdentity()
    {
        if (!string.IsNullOrWhiteSpace(HeaderBar?.UserEmail))
        {
            return HeaderBar.UserEmail.Trim();
        }

        var overrideUpn = ConfigurationManager.AppSettings["UserUpnOverride"];
        if (!string.IsNullOrWhiteSpace(overrideUpn))
        {
            return overrideUpn.Trim();
        }

        var user = Environment.UserName;
        if (user.Contains('\\'))
        {
            user = user[(user.LastIndexOf('\\') + 1)..];
        }

        return string.IsNullOrWhiteSpace(user) ? "unknown@korstructural.com" : $"{user}@korstructural.com";
    }

    private bool IsInRoleGroup(string roleName)
        => SecurityGroupAccess.IsUserInGroup(roleName, _currentUserIdentity);

    private bool CanManageGroups() => IsInRoleGroup("StandardDetailsAdmins");

    private bool CanContribute()
        => IsInRoleGroup("StandardDetailsContributors")
           || IsInRoleGroup("StandardDetailsApprovers")
           || IsInRoleGroup("StandardDetailsPublishers")
           || IsInRoleGroup("StandardDetailsAdmins");

    private bool CanApproveOrReject()
        => IsInRoleGroup("StandardDetailsApprovers")
           || IsInRoleGroup("StandardDetailsPublishers")
           || IsInRoleGroup("StandardDetailsAdmins");

    private bool CanPublish()
        => IsInRoleGroup("StandardDetailsPublishers")
           || IsInRoleGroup("StandardDetailsAdmins");

    private bool EnsureCanContribute(string action)
    {
        if (CanContribute()) return true;
        SetActivityMessage($"You do not have permission to {action}.", BannerTone.Warning);
        MessageBox.Show(this, $"You do not have permission to {action}.", "Permission", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private bool EnsureCanApproveReject(string action)
    {
        if (CanApproveOrReject()) return true;
        SetActivityMessage($"You do not have permission to {action}.", BannerTone.Warning);
        MessageBox.Show(this, $"You do not have permission to {action}.", "Permission", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private bool EnsureCanPublishAction()
    {
        if (CanPublish()) return true;
        SetActivityMessage("You do not have permission to publish versions.", BannerTone.Warning);
        MessageBox.Show(this, "You do not have permission to publish versions.", "Permission", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private bool EnsureCanManageGroupAction(string action)
    {
        if (CanManageGroups()) return true;
        SetActivityMessage($"You do not have permission to {action}.", BannerTone.Warning);
        MessageBox.Show(this, $"You do not have permission to {action}.", "Permission", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private bool EnsureGroupFeatureAvailable()
    {
        if (_groupSchemaAvailable) return true;
        SetActivityMessage("Group features are unavailable for this database login.", BannerTone.Warning);
        MessageBox.Show(this,
            "Group features are unavailable with the current database permissions. Core record and revision workflow is still available.",
            "Group Features Unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    private void SetActivityMessage(string message, BannerTone tone)
    {
        ActivityMessageText.Text = message;
        ActivityMessageText.Foreground = tone switch
        {
            BannerTone.Success => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF1E6B3A")),
            BannerTone.Warning => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF8A5A00")),
            BannerTone.Error => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF9D1C1C")),
            _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5E7185"))
        };
    }

    private void UpdateWorkflowHint(DocumentRow? selectedDoc, VersionRow? selectedVersion)
    {
        if (selectedDoc is null)
        {
            WorkflowHintText.Text = !_groupSchemaAvailable
                ? "Click New Record to start. Grouping is currently unavailable for this DB login."
                : (CanContribute()
                    ? "Click New Record to start. Select a group when you want to move records or filter the list."
                    : "Select a record to review versions and files.");
            return;
        }

        if (selectedVersion is null)
        {
            WorkflowHintText.Text = CanContribute()
                ? "Record selected. Click Add Revision to upload the next file."
                : "Record selected. Choose a revision to view status and file.";
            return;
        }

        WorkflowHintText.Text = selectedVersion.Status switch
        {
            StatusDraft => CanContribute()
                ? "Draft revision selected. Next step: Submit for approval."
                : "Draft revision selected. Waiting for contributor submission.",
            StatusSubmitted => CanApproveOrReject()
                ? "Submitted revision selected. Approve or Reject to continue."
                : "Submitted revision selected. Waiting for approver decision.",
            StatusApproved => CanPublish()
                ? "Approved revision selected. Publish to make it the official version."
                : "Approved revision selected. Waiting for publisher.",
            StatusRejected => CanContribute()
                ? "Rejected revision selected. Upload a corrected revision."
                : "Rejected revision selected. Waiting for contributor rework.",
            StatusPublished => "Published revision selected. This is currently official.",
            _ => "Revision selected. Review details and take the next appropriate action."
        };
    }

    private void UpdateHeroMetrics()
    {
        var recordCount = _documentSnapshot.Count;
        var officialCount = _documentSnapshot.Count(x => !string.Equals(x.CurrentOfficialText, "None", StringComparison.OrdinalIgnoreCase));
        var submittedCount = _documentSnapshot.Count(x => string.Equals(x.LatestStatusText, "Submitted", StringComparison.OrdinalIgnoreCase));
        var publishedCount = _documentSnapshot.Count(x => string.Equals(x.LatestStatusText, "Published", StringComparison.OrdinalIgnoreCase));
        var selectedRevisionCount = _versionSnapshot.Count;

        OfficialMetricText.Text = $"{officialCount} of {recordCount} records have an official revision";
        WorkflowMetricText.Text = $"{submittedCount} awaiting review • {publishedCount} published • {selectedRevisionCount} revisions in view";
        GroupMetricText.Text = _groupSchemaAvailable
            ? $"{_groupCount} active groups available for organization"
            : "Grouping unavailable for this database login";
    }

    private void UpdateActionStates()
    {
        var selectedGroup = GroupsTree.SelectedItem as GroupNode;
        var selectedDoc = DocumentsGrid.SelectedItem as DocumentRow;
        var selectedVersion = VersionsGrid.SelectedItem as VersionRow;

        var canManageGroups = CanManageGroups() && _groupSchemaAvailable;
        AddGroupButton.IsEnabled = canManageGroups;
        AddSubgroupButton.IsEnabled = canManageGroups && selectedGroup?.GroupId is not null;
        RenameGroupButton.IsEnabled = canManageGroups && selectedGroup?.GroupId is not null;
        RemoveGroupButton.IsEnabled = canManageGroups && selectedGroup?.GroupId is not null;

        var canContribute = CanContribute();
        CreateRecordButton.IsEnabled = canContribute;
        UploadVersionButton.IsEnabled = canContribute && selectedDoc is not null;
        var canAssignToSelectedGroup = selectedGroup is not null
            && (selectedGroup.GroupId is not null || string.Equals(selectedGroup.Name, "All Records", StringComparison.OrdinalIgnoreCase));
        AssignRecordButton.IsEnabled = canContribute && selectedDoc is not null && _groupSchemaAvailable && canAssignToSelectedGroup;
        UpdateGroupStepChips(selectedDoc is not null, canAssignToSelectedGroup, AssignRecordButton.IsEnabled);

        DeleteRecordButton.IsEnabled = canContribute && selectedDoc is not null;
        OpenFileButton.IsEnabled = selectedVersion is not null;
        SubmitButton.IsEnabled = canContribute && selectedVersion is not null && selectedVersion.Status == StatusDraft;
        ApproveButton.IsEnabled = CanApproveOrReject() && selectedVersion is not null && selectedVersion.Status == StatusSubmitted;
        RejectButton.IsEnabled = CanApproveOrReject() && selectedVersion is not null && selectedVersion.Status == StatusSubmitted;
        PublishButton.IsEnabled = CanPublish() && selectedVersion is not null && selectedVersion.Status == StatusApproved;

        UpdateWorkflowHint(selectedDoc, selectedVersion);
    }

    private void UpdateGroupStepChips(bool hasSelectedRecord, bool hasTargetGroup, bool canMoveRecord)
    {
        SetStepChipVisual(StepSelectRecordChip, StepSelectRecordText, hasSelectedRecord);
        SetStepChipVisual(StepSelectGroupChip, StepSelectGroupText, hasTargetGroup);
        SetStepChipVisual(StepMoveRecordChip, StepMoveRecordText, canMoveRecord);
    }

    private static void SetStepChipVisual(Border chip, TextBlock label, bool isActive)
    {
        if (isActive)
        {
            chip.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFDCEEF9"));
            chip.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF9FC6E3"));
            label.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF114F78"));
        }
        else
        {
            chip.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF1F3F6"));
            chip.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD9DEE5"));
            label.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF4F6275"));
        }
    }

    private static string ToSafeFolderSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Untitled";
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Trim().Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "Untitled" : cleaned;
    }

    private static void AddParam(SqlCommand cmd, string name, SqlDbType type, object? value)
    {
        var p = cmd.Parameters.Add(name, type);
        p.Value = value ?? DBNull.Value;
    }

    private static void AddNVarChar(SqlCommand cmd, string name, int size, string? value)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.NVarChar, size);
        p.Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
    }

    private static void AddNVarCharMax(SqlCommand cmd, string name, string? value)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.NVarChar, -1);
        p.Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
    }

    private static void AddRowVersionParam(SqlCommand cmd, string name, byte[]? value)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.Binary, RowVersionLength);
        p.Value = value is { Length: RowVersionLength } ? value : DBNull.Value;
    }

    private async Task EnsureGroupSchemaAsync()
    {
        var cs = GetConnectionString();
        if (string.IsNullOrWhiteSpace(cs)) return;

        const string detectSql = @"
SELECT
    CASE WHEN OBJECT_ID('dbo.DocumentGroups', 'U') IS NOT NULL THEN 1 ELSE 0 END AS HasGroups,
    CASE WHEN COL_LENGTH('dbo.Documents', 'DocumentGroupId') IS NOT NULL THEN 1 ELSE 0 END AS HasDocumentGroupId;";

        try
        {
            await using var cn = new SqlConnection(cs);
            await cn.OpenAsync();
            await using (var detect = new SqlCommand(detectSql, cn))
            await using (var reader = await detect.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    var hasGroups = reader.GetInt32(0) == 1;
                    var hasDocGroupId = reader.GetInt32(1) == 1;
                    _groupSchemaAvailable = hasGroups && hasDocGroupId;
                    if (!_groupSchemaAvailable)
                    {
                        _selectedGroupId = null;
                        SetActivityMessage("Grouping is disabled until DB schema is installed by an administrator.", BannerTone.Warning);
                        Debug.WriteLine("Group schema not present. Runtime schema creation is disabled.");
                    }
                    return;
                }
            }
        }
        catch (SqlException ex) when (ex.Number is 262 or 229)
        {
            _groupSchemaAvailable = false;
            _selectedGroupId = null;
            SetActivityMessage("Group setup skipped due to SQL permissions. Records/revisions remain available.", BannerTone.Warning);
            Debug.WriteLine($"Group schema bootstrap skipped due to SQL permission: {ex.Message}");
        }
        catch (Exception ex)
        {
            _groupSchemaAvailable = false;
            _selectedGroupId = null;
            SetActivityMessage("Group features unavailable due to database schema mismatch.", BannerTone.Warning);
            Debug.WriteLine($"Group schema bootstrap failed: {ex.Message}");
        }
    }

    private async Task LoadGroupsAsync()
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

        var cs = GetConnectionString();
        if (string.IsNullOrWhiteSpace(cs)) return;

        const string sql = @"
SELECT DocumentGroupId AS GroupId, ParentDocumentGroupId AS ParentGroupId, Name AS GroupName
FROM dbo.DocumentGroups
WHERE IsActive = 1
ORDER BY Name;";

        var items = new List<GroupNode>();

        try
        {
            await using var cn = new SqlConnection(cs);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql, cn);
            await using var r = await cmd.ExecuteReaderAsync();

            while (await r.ReadAsync())
            {
                items.Add(new GroupNode
                {
                    GroupId = r.GetInt64(0),
                    ParentGroupId = r.IsDBNull(1) ? null : r.GetInt64(1),
                    Name = r.IsDBNull(2) ? "(Unnamed)" : r.GetString(2)
                });
            }
        }
        catch (SqlException ex) when (ex.Number is 208 or 207)
        {
            _groupSchemaAvailable = false;
            _selectedGroupId = null;
            SetActivityMessage("Grouping disabled because required DB objects are missing.", BannerTone.Warning);
            Debug.WriteLine($"Group schema missing while loading groups: {ex.Message}");
            await LoadGroupsAsync();
            return;
        }

        var map = items.ToDictionary(x => x.GroupId!.Value);
        var roots = new ObservableCollection<GroupNode>();

        foreach (var g in items)
        {
            if (g.ParentGroupId.HasValue && map.TryGetValue(g.ParentGroupId.Value, out var parent))
            {
                parent.Children.Add(g);
            }
            else
            {
                roots.Add(g);
            }
        }

        var allRoot = new GroupNode { GroupId = null, ParentGroupId = null, Name = "All Groups" };
        foreach (var root in roots.OrderBy(x => x.Name))
        {
            allRoot.Children.Add(root);
        }

        ApplyGroupExpansionState(allRoot, expandedGroupIds);
        allRoot.IsExpanded = allRootExpanded;

        if (_selectedGroupId.HasValue)
        {
            var selectedNode = FindGroupNode(allRoot, _selectedGroupId.Value);
            if (selectedNode != null)
            {
                selectedNode.IsSelected = true;
                EnsureGroupAncestorsExpanded(allRoot, _selectedGroupId.Value);
            }
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
            return (expanded, allRootExpanded);

        foreach (var root in roots)
        {
            if (root.GroupId is null)
            {
                allRootExpanded = root.IsExpanded;
            }

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
            return node;

        foreach (var child in node.Children)
        {
            var match = FindGroupNode(child, groupId);
            if (match != null)
                return match;
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

    private async Task LoadDocumentsAsync()
    {
        var cs = GetConnectionString();
        if (string.IsNullOrWhiteSpace(cs))
        {
            MessageBox.Show(this, "Missing connection string 'KorTransmittalsDb' in App.config", "Configuration", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var q = SearchBox.Text?.Trim() ?? string.Empty;
        var effectiveGroupId = _filterRecordsBySelectedGroup ? _selectedGroupId : null;

        const string groupedSql = @"
WITH GroupFilter AS
(
    SELECT DocumentGroupId AS GroupId
    FROM dbo.DocumentGroups
    WHERE DocumentGroupId = @groupId
    UNION ALL
    SELECT g.DocumentGroupId AS GroupId
    FROM dbo.DocumentGroups g
    INNER JOIN GroupFilter gf ON g.ParentDocumentGroupId = gf.GroupId
)
SELECT d.DocumentId,
       d.Title,
       ISNULL(dg.Name, 'Ungrouped') AS GroupName,
       cur.CurrentOfficialText,
       latest.LatestStatus
FROM dbo.Documents d
LEFT JOIN dbo.DocumentGroups dg ON dg.DocumentGroupId = d.DocumentGroupId
OUTER APPLY
(
    SELECT TOP 1 CONCAT('v', v.VersionNumber) AS CurrentOfficialText
    FROM dbo.DocumentVersions v
    WHERE v.DocumentId = d.DocumentId
      AND v.IsCurrentOfficial = 1
    ORDER BY v.VersionNumber DESC
) cur
OUTER APPLY
(
    SELECT TOP 1 v2.Status AS LatestStatus
    FROM dbo.DocumentVersions v2
    WHERE v2.DocumentId = d.DocumentId
    ORDER BY v2.VersionNumber DESC
) latest
WHERE (@q = '' OR d.Title LIKE @like OR ISNULL(d.Description, '') LIKE @like)
  AND (@groupId IS NULL OR d.DocumentGroupId IN (SELECT GroupId FROM GroupFilter))
ORDER BY d.Title
OPTION (MAXRECURSION 100);";

        const string fallbackSql = @"
SELECT d.DocumentId,
       d.Title,
       'Ungrouped' AS GroupName,
       cur.CurrentOfficialText,
       latest.LatestStatus
FROM dbo.Documents d
OUTER APPLY
(
    SELECT TOP 1 CONCAT('v', v.VersionNumber) AS CurrentOfficialText
    FROM dbo.DocumentVersions v
    WHERE v.DocumentId = d.DocumentId
      AND v.IsCurrentOfficial = 1
    ORDER BY v.VersionNumber DESC
) cur
OUTER APPLY
(
    SELECT TOP 1 v2.Status AS LatestStatus
    FROM dbo.DocumentVersions v2
    WHERE v2.DocumentId = d.DocumentId
    ORDER BY v2.VersionNumber DESC
) latest
WHERE (@q = '' OR d.Title LIKE @like OR ISNULL(d.Description, '') LIKE @like)
ORDER BY d.Title;";

        var rows = new List<DocumentRow>();

        try
        {
            await using var cn = new SqlConnection(cs);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(_groupSchemaAvailable ? groupedSql : fallbackSql, cn);
            AddNVarChar(cmd, "@q", DocumentDescriptionMax, q);
            AddNVarChar(cmd, "@like", DocumentDescriptionMax + 2, $"%{q}%");
            if (_groupSchemaAvailable)
            {
            AddParam(cmd, "@groupId", SqlDbType.BigInt, effectiveGroupId);
            }

            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                rows.Add(new DocumentRow
                {
                    DocumentId = r.GetInt64(0),
                    Title = r.IsDBNull(1) ? "" : r.GetString(1),
                    GroupName = r.IsDBNull(2) ? "Ungrouped" : r.GetString(2),
                    CurrentOfficialText = r.IsDBNull(3) ? "None" : r.GetString(3),
                    LatestStatusText = ToStatusText(r.IsDBNull(4) ? (byte?)null : r.GetByte(4))
                });
            }
        }
        catch (SqlException ex) when (_groupSchemaAvailable && (ex.Number is 208 or 207))
        {
            _groupSchemaAvailable = false;
            _selectedGroupId = null;
            SetActivityMessage("Grouping disabled because required DB objects are missing.", BannerTone.Warning);
            Debug.WriteLine($"Group schema missing while loading documents: {ex.Message}");
            await LoadGroupsAsync();
            await LoadDocumentsAsync();
            return;
        }

        _documentSnapshot = rows;
        _versionSnapshot = new List<VersionRow>();
        UpdateHeroMetrics();
        DocumentsGrid.ItemsSource = rows;
        VersionsGrid.ItemsSource = null;
        UpdateSelectionSummary();
    }

    private async Task LoadVersionsAsync(long documentId)
    {
        var cs = GetConnectionString();
        if (string.IsNullOrWhiteSpace(cs)) return;

        const string sql = @"
SELECT v.DocumentVersionId, v.DocumentId, v.VersionNumber, v.Status, v.IsCurrentOfficial, v.CreatedUtc,
       b.OriginalFileName, b.ContentLengthBytes, b.StoragePath, v.RowVersion
FROM dbo.DocumentVersions v
INNER JOIN dbo.FileBlobs b ON b.FileBlobId = v.FileBlobId
WHERE v.DocumentId = @docId
ORDER BY v.VersionNumber DESC;";

        var rows = new List<VersionRow>();

        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        AddParam(cmd, "@docId", SqlDbType.BigInt, documentId);

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var versionNo = r.GetInt32(2);
            rows.Add(new VersionRow
            {
                DocumentVersionId = r.GetInt64(0),
                DocumentId = r.GetInt64(1),
                VersionNumber = versionNo,
                VersionLabel = $"v{versionNo}",
                Status = r.GetByte(3),
                StatusText = ToStatusText(r.GetByte(3)),
                IsCurrentOfficial = r.GetBoolean(4),
                CreatedUtc = r.GetDateTime(5),
                CreatedUtcDisplay = r.GetDateTime(5).ToString("u"),
                OriginalFileName = r.IsDBNull(6) ? "" : r.GetString(6),
                FileSizeKb = Math.Round((r.IsDBNull(7) ? 0L : r.GetInt64(7)) / 1024.0, 1),
                StoragePath = r.IsDBNull(8) ? "" : r.GetString(8),
                RowVersion = r.IsDBNull(9) ? Array.Empty<byte>() : (byte[])r[9]
            });
        }

        _versionSnapshot = rows;
        UpdateHeroMetrics();
        VersionsGrid.ItemsSource = rows;
        ApplyVersionSummaryToSelectedDocument(rows);
        UpdateActionStates();
    }

    private void ApplyVersionSummaryToSelectedDocument(IReadOnlyList<VersionRow> versions)
    {
        if (DocumentsGrid.SelectedItem is not DocumentRow doc)
        {
            return;
        }

        var latest = versions.FirstOrDefault();
        var currentOfficial = versions.FirstOrDefault(x => x.IsCurrentOfficial);

        doc.LatestStatusText = latest?.StatusText ?? "None";
        doc.CurrentOfficialText = currentOfficial?.VersionLabel ?? "None";

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

    private async void Search_Click(object sender, RoutedEventArgs e) => await LoadDocumentsAsync();

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadGroupsAsync();
        await LoadDocumentsAsync();
    }

    private async void GroupsTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (GroupsTree.SelectedItem is GroupNode node)
        {
            _selectedGroupId = node.GroupId;
        }
        else
        {
            _selectedGroupId = null;
        }

        if (_filterRecordsBySelectedGroup)
        {
            await LoadDocumentsAsync();
            return;
        }

        // When a record is already selected, treat group selection as an assignment target first.
        // Avoid reloading/filtering immediately, which would clear selection and block reassignment.
        if (DocumentsGrid.SelectedItem is DocumentRow selectedDoc)
        {
            if (GroupsTree.SelectedItem is GroupNode selectedGroup)
            {
                SetActivityMessage(selectedGroup.GroupId.HasValue
                    ? $"Target group set to '{selectedGroup.Name}' for record '{selectedDoc.Title}'. Click Assign Record to apply."
                    : "Target set to 'All Records'. Click Assign Record to move record to Ungrouped.", BannerTone.Info);
            }

            UpdateActionStates();
            return;
        }

        SetActivityMessage("Target group selected. Select a record and click Move Selected Record.", BannerTone.Info);
        UpdateActionStates();
    }

    private async void FilterByGroupCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        _filterRecordsBySelectedGroup = FilterByGroupCheckBox.IsChecked == true;
        await LoadDocumentsAsync();
        SetActivityMessage(_filterRecordsBySelectedGroup
            ? "Group filter is ON. Records list now follows tree selection."
            : "Group filter is OFF. Tree selection sets assignment target without filtering records.", BannerTone.Info);
    }

    private async void DocumentsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DocumentsGrid.SelectedItem is DocumentRow doc)
        {
            await LoadVersionsAsync(doc.DocumentId);
        }
        else
        {
            VersionsGrid.ItemsSource = null;
            UpdateActionStates();
        }

        UpdateSelectionSummary();
    }

    private void VersionsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectionSummary();
    }

    private void UpdateSelectionSummary()
    {
        var doc = DocumentsGrid.SelectedItem as DocumentRow;
        var version = VersionsGrid.SelectedItem as VersionRow;

        SelectedRecordValue.Text = doc is null ? "None selected" : $"{doc.Title} [{doc.GroupName}]";
        SelectedRevisionValue.Text = version?.VersionLabel ?? "None selected";

        if (version is null)
        {
            SelectedStatusBadge.Tag = "None";
            SelectedStatusValue.Text = "None";
            SelectedFileValue.Text = "Choose a revision to see details";
            UpdateActionStates();
            return;
        }

        var fileLabel = string.IsNullOrWhiteSpace(version.OriginalFileName) ? "(no file name)" : version.OriginalFileName;
        SelectedStatusBadge.Tag = version.StatusText;
        SelectedStatusValue.Text = version.StatusText;
        SelectedFileValue.Text = fileLabel;
        UpdateActionStates();
    }

    private async void AddGroup_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureGroupFeatureAvailable()) return;
        if (!EnsureCanManageGroupAction("manage groups")) return;

        var dlg = new GroupEditWindow("Add Group", "Create a new top-level group.") { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var cs = GetConnectionString();
        if (string.IsNullOrWhiteSpace(cs)) return;

        const string sql = @"
INSERT INTO dbo.DocumentGroups (ParentDocumentGroupId, Name, IsActive, CreatedByUserId, CreatedUtc)
VALUES (NULL, @name, 1, @uid, SYSUTCDATETIME());";

        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        AddNVarChar(cmd, "@name", 200, dlg.GroupName);
        AddParam(cmd, "@uid", SqlDbType.UniqueIdentifier, _actorUserId);
        await cmd.ExecuteNonQueryAsync();

        await LoadGroupsAsync();
    }

    private async void AddSubgroup_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureGroupFeatureAvailable()) return;
        if (!EnsureCanManageGroupAction("manage groups")) return;

        if (GroupsTree.SelectedItem is not GroupNode selected || selected.GroupId is null)
        {
            MessageBox.Show(this, "Select a parent group first.", "Add Subgroup", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new GroupEditWindow("Add Subgroup", $"Create a subgroup under '{selected.Name}'.") { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var cs = GetConnectionString();
        if (string.IsNullOrWhiteSpace(cs)) return;

        const string sql = @"
INSERT INTO dbo.DocumentGroups (ParentDocumentGroupId, Name, IsActive, CreatedByUserId, CreatedUtc)
VALUES (@parentId, @name, 1, @uid, SYSUTCDATETIME());";

        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        AddParam(cmd, "@parentId", SqlDbType.BigInt, selected.GroupId.Value);
        AddNVarChar(cmd, "@name", 200, dlg.GroupName);
        AddParam(cmd, "@uid", SqlDbType.UniqueIdentifier, _actorUserId);
        await cmd.ExecuteNonQueryAsync();

        await LoadGroupsAsync();
    }

    private async void RenameGroup_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureGroupFeatureAvailable()) return;
        if (!EnsureCanManageGroupAction("rename groups")) return;

        if (GroupsTree.SelectedItem is not GroupNode selected || selected.GroupId is null)
        {
            MessageBox.Show(this, "Select a group to rename.", "Rename Group", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new GroupEditWindow("Rename Group", "Update group name.", selected.Name) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var cs = GetConnectionString();
        if (string.IsNullOrWhiteSpace(cs)) return;

        const string sql = @"
UPDATE dbo.DocumentGroups
SET Name = @name,
    UpdatedByUserId = @uid,
    UpdatedUtc = SYSUTCDATETIME()
WHERE DocumentGroupId = @id;";

        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        AddNVarChar(cmd, "@name", 200, dlg.GroupName);
        AddParam(cmd, "@uid", SqlDbType.UniqueIdentifier, _actorUserId);
        AddParam(cmd, "@id", SqlDbType.BigInt, selected.GroupId.Value);
        await cmd.ExecuteNonQueryAsync();

        await LoadGroupsAsync();
        await LoadDocumentsAsync();
    }

    private async void RemoveGroup_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureGroupFeatureAvailable()) return;
        if (!EnsureCanManageGroupAction("remove groups")) return;

        if (GroupsTree.SelectedItem is not GroupNode selected || selected.GroupId is null)
        {
            MessageBox.Show(this, "Select a group to remove.", "Remove Group", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var cs = GetConnectionString();
        if (string.IsNullOrWhiteSpace(cs)) return;

        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync();

        var childCmd = new SqlCommand("SELECT COUNT(1) FROM dbo.DocumentGroups WHERE ParentDocumentGroupId=@id AND IsActive=1", cn);
        AddParam(childCmd, "@id", SqlDbType.BigInt, selected.GroupId.Value);
        var childCount = Convert.ToInt32(await childCmd.ExecuteScalarAsync() ?? 0);
        if (childCount > 0)
        {
            MessageBox.Show(this, "This group has subgroups. Remove or move subgroups first.", "Remove Group", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var docCmd = new SqlCommand("SELECT COUNT(1) FROM dbo.Documents WHERE DocumentGroupId=@id", cn);
        AddParam(docCmd, "@id", SqlDbType.BigInt, selected.GroupId.Value);
        var docCount = Convert.ToInt32(await docCmd.ExecuteScalarAsync() ?? 0);
        if (docCount > 0)
        {
            MessageBox.Show(this, "This group has document records. Reassign or ungroup records before removing.", "Remove Group", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show(this, $"Remove group '{selected.Name}'?", "Confirm Remove", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        var delCmd = new SqlCommand("DELETE FROM dbo.DocumentGroups WHERE DocumentGroupId=@id", cn);
        AddParam(delCmd, "@id", SqlDbType.BigInt, selected.GroupId.Value);
        await delCmd.ExecuteNonQueryAsync();

        _selectedGroupId = null;
        await LoadGroupsAsync();
        await LoadDocumentsAsync();
    }

    private async void AssignRecordToGroup_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureGroupFeatureAvailable()) return;
        if (!EnsureCanContribute("assign records to groups")) return;

        if (DocumentsGrid.SelectedItem is not DocumentRow doc)
        {
            SetActivityMessage("Choose a document record first, then assign it to a group.", BannerTone.Info);
            MessageBox.Show(this, "Select a document record first.", "Assign Record", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (GroupsTree.SelectedItem is not GroupNode selectedGroup)
        {
            MessageBox.Show(this, "Select a target group first. Choose 'All Records' to ungroup the record.", "Assign Record", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (selectedGroup.GroupId is null && !string.Equals(selectedGroup.Name, "All Records", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Select a specific group. To remove a group assignment, select 'All Records'.", "Assign Record", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var targetGroupId = selectedGroup.GroupId;

        var cs = GetConnectionString();
        if (string.IsNullOrWhiteSpace(cs)) return;

        const string sql = @"
UPDATE dbo.Documents
SET DocumentGroupId = @groupId,
    UpdatedByUserId = @uid,
    UpdatedUtc = SYSUTCDATETIME()
WHERE DocumentId = @docId;";

        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        AddParam(cmd, "@groupId", SqlDbType.BigInt, targetGroupId);
        AddParam(cmd, "@uid", SqlDbType.UniqueIdentifier, _actorUserId);
        AddParam(cmd, "@docId", SqlDbType.BigInt, doc.DocumentId);
        await cmd.ExecuteNonQueryAsync();

        await LoadDocumentsAsync();
        SetActivityMessage(targetGroupId.HasValue
            ? $"Record '{doc.Title}' assigned to group '{selectedGroup.Name}'."
            : $"Record '{doc.Title}' moved to Ungrouped.", BannerTone.Success);
    }

    private async void CreateDocument_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureCanContribute("create records")) return;

        var dlg = new CreateStandardDocumentWindow { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var cs = GetConnectionString();
        if (string.IsNullOrWhiteSpace(cs)) return;

        var sql = _groupSchemaAvailable
            ? @"INSERT INTO dbo.Documents (DocumentUid, Title, Description, DocumentGroupId, CreatedByUserId, CreatedUtc)
VALUES (NEWID(), @title, @desc, @groupId, @userId, SYSUTCDATETIME());"
            : @"INSERT INTO dbo.Documents (DocumentUid, Title, Description, CreatedByUserId, CreatedUtc)
VALUES (NEWID(), @title, @desc, @userId, SYSUTCDATETIME());";

        try
        {
            await using var cn = new SqlConnection(cs);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql, cn);
            AddNVarChar(cmd, "@title", DocumentTitleMax, dlg.DocumentTitle);
            AddNVarChar(cmd, "@desc", DocumentDescriptionMax, dlg.DocumentDescription);
            if (_groupSchemaAvailable)
            {
                AddParam(cmd, "@groupId", SqlDbType.BigInt, _selectedGroupId);
            }
            AddParam(cmd, "@userId", SqlDbType.UniqueIdentifier, _actorUserId);
            await cmd.ExecuteNonQueryAsync();
            await LoadDocumentsAsync();
            SetActivityMessage($"Created record '{dlg.DocumentTitle}'.", BannerTone.Success);
        }
        catch (Exception ex)
        {
            SetActivityMessage("Could not create the record. Please review the error and try again.", BannerTone.Error);
            MessageBox.Show(this, ex.Message, "Create Record Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void UploadVersion_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureCanContribute("upload versions")) return;

        if (DocumentsGrid.SelectedItem is not DocumentRow doc)
        {
            SetActivityMessage("Choose a document record first, then upload a revision file.", BannerTone.Info);
            MessageBox.Show(this, "Select a document record first.", "Add Revision", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var picker = new OpenFileDialog
        {
            Title = "Select version file",
            Filter = "Allowed files (*.pdf;*.dwg;*.docx)|*.pdf;*.dwg;*.docx"
        };

        if (picker.ShowDialog(this) != true) return;

        var sourcePath = picker.FileName;
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (ext != ".pdf" && ext != ".dwg" && ext != ".docx")
        {
            SetActivityMessage("Upload blocked: only PDF, DWG, and DOCX files are supported.", BannerTone.Warning);
            MessageBox.Show(this, "Only PDF, DWG, DOCX are allowed.", "Add Revision", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var cs = GetConnectionString();
        if (string.IsNullOrWhiteSpace(cs)) return;

        string? targetPath = null;
        string? folder = null;
        var committed = false;

        try
        {
            await using var cn = new SqlConnection(cs);
            await cn.OpenAsync();
            await using var tx = await cn.BeginTransactionAsync(IsolationLevel.Serializable);

            // Serialize version allocation per document to prevent duplicate VersionNumber under concurrent uploads.
            var lockDocCmd = new SqlCommand(
                "SELECT 1 FROM dbo.Documents WITH (UPDLOCK, HOLDLOCK) WHERE DocumentId=@docId",
                cn, (SqlTransaction)tx);
            AddParam(lockDocCmd, "@docId", SqlDbType.BigInt, doc.DocumentId);
            await lockDocCmd.ExecuteScalarAsync();

            var nextVersionCmd = new SqlCommand(
                "SELECT ISNULL(MAX(VersionNumber),0)+1 FROM dbo.DocumentVersions WITH (UPDLOCK, HOLDLOCK) WHERE DocumentId=@docId",
                cn, (SqlTransaction)tx);
            AddParam(nextVersionCmd, "@docId", SqlDbType.BigInt, doc.DocumentId);
            var nextVersion = Convert.ToInt32(await nextVersionCmd.ExecuteScalarAsync());

            var root = GetStorageRoot();
            var recordFolder = $"{ToSafeFolderSegment(doc.Title)} (ID {doc.DocumentId})";
            var versionFolder = $"v{nextVersion}";
            folder = Path.Combine(root, "Document Details", recordFolder, versionFolder);
            Directory.CreateDirectory(folder);

            var targetName = $"{Guid.NewGuid():N}{ext}";
            targetPath = Path.Combine(folder, targetName);
            File.Copy(sourcePath, targetPath, false);

            if (targetPath.Length > FileStoragePathMax)
            {
                throw new InvalidOperationException($"Generated storage path exceeds {FileStoragePathMax} characters. Shorten the record title.");
            }

            await using var fs = File.OpenRead(targetPath);
            var sha = Convert.ToHexString(await SHA256.HashDataAsync(fs));
            var fi = new FileInfo(targetPath);

            var insertBlob = new SqlCommand(@"
INSERT INTO dbo.FileBlobs (BlobUid, StoragePath, OriginalFileName, FileExtension, ContentType, ContentLengthBytes, Sha256Hash, UploadedByUserId, CreatedUtc)
VALUES (NEWID(), @path, @name, @ext, @contentType, @len, @sha, @userId, SYSUTCDATETIME());
SELECT CAST(SCOPE_IDENTITY() AS bigint);", cn, (SqlTransaction)tx);
            AddNVarChar(insertBlob, "@path", FileStoragePathMax, targetPath);
            AddNVarChar(insertBlob, "@name", FileNameMax, Path.GetFileName(sourcePath));
            AddNVarChar(insertBlob, "@ext", FileExtensionMax, ext);
            AddNVarChar(insertBlob, "@contentType", ContentTypeMax, ext == ".pdf" ? "application/pdf" : ext == ".dwg" ? "application/acad" : "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
            AddParam(insertBlob, "@len", SqlDbType.BigInt, fi.Length);
            AddNVarChar(insertBlob, "@sha", Sha256HexLength, sha);
            AddParam(insertBlob, "@userId", SqlDbType.UniqueIdentifier, _actorUserId);
            var blobId = (long)(await insertBlob.ExecuteScalarAsync() ?? 0L);

            var insertVersion = new SqlCommand(@"
INSERT INTO dbo.DocumentVersions (VersionUid, DocumentId, VersionNumber, FileBlobId, Status, IsCurrentOfficial, CreatedByUserId, CreatedUtc)
VALUES (NEWID(), @docId, @ver, @blobId, 0, 0, @userId, SYSUTCDATETIME());", cn, (SqlTransaction)tx);
            AddParam(insertVersion, "@docId", SqlDbType.BigInt, doc.DocumentId);
            AddParam(insertVersion, "@ver", SqlDbType.Int, nextVersion);
            AddParam(insertVersion, "@blobId", SqlDbType.BigInt, blobId);
            AddParam(insertVersion, "@userId", SqlDbType.UniqueIdentifier, _actorUserId);
            await insertVersion.ExecuteNonQueryAsync();

            await tx.CommitAsync();
            committed = true;
            await LoadVersionsAsync(doc.DocumentId);
            SetActivityMessage($"Uploaded revision v{nextVersion} for '{doc.Title}'.", BannerTone.Success);
        }
        catch (Exception ex)
        {
            if (!committed)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(targetPath) && File.Exists(targetPath))
                    {
                        File.Delete(targetPath);
                    }

                    if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder) &&
                        !Directory.EnumerateFileSystemEntries(folder).Any())
                    {
                        Directory.Delete(folder, false);
                    }
                }
                catch (Exception cleanupEx)
                {
                    Debug.WriteLine($"Upload cleanup failed for '{targetPath}': {cleanupEx.Message}");
                }
            }

            SetActivityMessage("Upload failed. The file was not added.", BannerTone.Error);
            MessageBox.Show(this, ex.Message, "Add Revision Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (VersionsGrid.SelectedItem is not VersionRow version)
        {
            SetActivityMessage("Select a revision to open its file.", BannerTone.Info);
            MessageBox.Show(this, "Select a version first.", "Open File", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!File.Exists(version.StoragePath))
        {
            SetActivityMessage("File could not be opened because it is missing from storage.", BannerTone.Warning);
            MessageBox.Show(this, "File not found in storage.", "Open File", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var launchPath = version.StoragePath;
        var note = string.Empty;

        if (version.Status != StatusPublished)
        {
            var watermark = GetStatusWatermarkText(version.Status, version.StatusText);
            StatusWatermarkRenderer.TryPrepareOpenCopy(
                version.StoragePath,
                watermark,
                out launchPath,
                out note);
        }

        Process.Start(new ProcessStartInfo { FileName = launchPath, UseShellExecute = true });

        if (string.IsNullOrWhiteSpace(note))
        {
            SetActivityMessage($"Opened file '{version.OriginalFileName}'.", BannerTone.Success);
        }
        else
        {
            SetActivityMessage(note, BannerTone.Warning);
        }
    }

    private static string GetStatusWatermarkText(byte status, string fallback)
        => status switch
        {
            StatusDraft => "DRAFT",
            StatusSubmitted => "SUBMITTED",
            StatusApproved => "APPROVED - NOT PUBLISHED",
            StatusRejected => "REJECTED",
            _ => string.IsNullOrWhiteSpace(fallback) ? "WORKING COPY" : fallback.ToUpperInvariant()
        };

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureCanContribute("submit versions")) return;
        await UpdateStatusAsync(StatusDraft, StatusSubmitted, true);
    }

    private async void Approve_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureCanApproveReject("approve versions")) return;
        await DecideAsync(StatusApproved, 1);
    }

    private async void Reject_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureCanApproveReject("reject versions")) return;
        await DecideAsync(StatusRejected, 2);
    }

    private async void Publish_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureCanPublishAction()) return;
        await PublishAsync();
    }

    private async void DeleteRecord_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureCanContribute("delete document records")) return;

        if (DocumentsGrid.SelectedItem is not DocumentRow doc)
        {
            SetActivityMessage("Select a document record first.", BannerTone.Info);
            MessageBox.Show(this, "Select a document record first.", "Delete Record", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var firstConfirm = MessageBox.Show(
            this,
            $"Delete record '{doc.Title}' and all associated revisions?\n\nThis cannot be undone.",
            "Delete Record",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (firstConfirm != MessageBoxResult.Yes)
            return;

        var secondConfirm = MessageBox.Show(
            this,
            "Final confirmation: this will permanently remove record metadata, revision history, and approval/publication history.",
            "Confirm Permanent Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (secondConfirm != MessageBoxResult.Yes)
            return;

        var cs = GetConnectionString();
        if (string.IsNullOrWhiteSpace(cs)) return;

        var deletedPaths = new List<string>();

        try
        {
            await using var cn = new SqlConnection(cs);
            await cn.OpenAsync();
            await using var tx = await cn.BeginTransactionAsync();

            var existsCmd = new SqlCommand("SELECT COUNT(1) FROM dbo.Documents WHERE DocumentId=@id", cn, (SqlTransaction)tx);
            AddParam(existsCmd, "@id", SqlDbType.BigInt, doc.DocumentId);
            var exists = Convert.ToInt32(await existsCmd.ExecuteScalarAsync() ?? 0);
            if (exists == 0)
            {
                await tx.RollbackAsync();
                SetActivityMessage("Selected record no longer exists. Refreshing list.", BannerTone.Warning);
                MessageBox.Show(this, "Selected record no longer exists.", "Delete Record", MessageBoxButton.OK, MessageBoxImage.Warning);
                await LoadDocumentsAsync();
                return;
            }

            var deleteCmd = new SqlCommand(@"
DECLARE @DocBlobIds TABLE (FileBlobId bigint PRIMARY KEY);

INSERT INTO @DocBlobIds(FileBlobId)
SELECT DISTINCT v.FileBlobId
FROM dbo.DocumentVersions v
WHERE v.DocumentId = @docId;

DELETE ar
FROM dbo.ApprovalRecords ar
INNER JOIN dbo.DocumentVersions v ON v.DocumentVersionId = ar.DocumentVersionId
WHERE v.DocumentId = @docId;

DELETE pr
FROM dbo.PublicationRecords pr
INNER JOIN dbo.DocumentVersions v ON v.DocumentVersionId = pr.DocumentVersionId
WHERE v.DocumentId = @docId;

DELETE FROM dbo.DocumentVersions
WHERE DocumentId = @docId;

DELETE FROM dbo.Documents
WHERE DocumentId = @docId;

DELETE fb
OUTPUT DELETED.StoragePath
FROM dbo.FileBlobs fb
WHERE fb.FileBlobId IN (SELECT b.FileBlobId FROM @DocBlobIds b)
  AND NOT EXISTS (
      SELECT 1
      FROM dbo.DocumentVersions dv
      WHERE dv.FileBlobId = fb.FileBlobId
  );", cn, (SqlTransaction)tx);
            AddParam(deleteCmd, "@docId", SqlDbType.BigInt, doc.DocumentId);

            await using (var reader = await deleteCmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    if (!reader.IsDBNull(0))
                    {
                        var p = reader.GetString(0);
                        if (!string.IsNullOrWhiteSpace(p))
                            deletedPaths.Add(p);
                    }
                }
            }

            var safeTitle = (doc.Title ?? string.Empty)
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);

            var auditCmd = new SqlCommand(@"
INSERT INTO dbo.AuditEvents (EventUtc, ActorUserId, EntityType, EntityId, EventType, NewValuesJson, Source)
VALUES (SYSUTCDATETIME(), @uid, 'Document', @id, 'Deleted', @json, 'Desktop');", cn, (SqlTransaction)tx);
            AddParam(auditCmd, "@uid", SqlDbType.UniqueIdentifier, _actorUserId);
            AddParam(auditCmd, "@id", SqlDbType.BigInt, doc.DocumentId);
            AddNVarCharMax(auditCmd, "@json", $"{{\"Title\":\"{safeTitle}\"}}");
            await auditCmd.ExecuteNonQueryAsync();

            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            SetActivityMessage("Delete failed. No changes were committed.", BannerTone.Error);
            MessageBox.Show(this, ex.Message, "Delete Record Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        foreach (var path in deletedPaths)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception cleanupEx)
            {
                Debug.WriteLine($"Could not delete file '{path}': {cleanupEx.Message}");
            }
        }

        await LoadDocumentsAsync();
        VersionsGrid.ItemsSource = null;
        SetActivityMessage($"Record '{doc.Title}' was deleted.", BannerTone.Success);
    }

    private async Task UpdateStatusAsync(byte expected, byte next, bool writeAudit)
    {
        if (VersionsGrid.SelectedItem is not VersionRow v)
        {
            SetActivityMessage("Select a revision first.", BannerTone.Info);
            MessageBox.Show(this, "Select a version first.", "Status", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var cs = GetConnectionString();
        if (string.IsNullOrWhiteSpace(cs)) return;

        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync();

        var updateCmd = new SqlCommand(@"
UPDATE dbo.DocumentVersions
SET Status=@next, UpdatedByUserId=@uid, UpdatedUtc=SYSUTCDATETIME()
WHERE DocumentVersionId=@id AND Status=@expected AND RowVersion=@rowVersion;", cn);
        AddParam(updateCmd, "@next", SqlDbType.TinyInt, next);
        AddParam(updateCmd, "@uid", SqlDbType.UniqueIdentifier, _actorUserId);
        AddParam(updateCmd, "@id", SqlDbType.BigInt, v.DocumentVersionId);
        AddParam(updateCmd, "@expected", SqlDbType.TinyInt, expected);
        AddRowVersionParam(updateCmd, "@rowVersion", v.RowVersion);
        var rows = await updateCmd.ExecuteNonQueryAsync();
        if (rows == 0)
        {
            var checkCmd = new SqlCommand("SELECT Status FROM dbo.DocumentVersions WHERE DocumentVersionId=@id", cn);
            AddParam(checkCmd, "@id", SqlDbType.BigInt, v.DocumentVersionId);
            var current = await checkCmd.ExecuteScalarAsync();
            if (current is null or DBNull)
            {
                SetActivityMessage("Selected revision no longer exists. Refreshing list.", BannerTone.Warning);
                MessageBox.Show(this, "Selected revision no longer exists.", "Status", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else if (Convert.ToByte(current) != expected)
            {
                SetActivityMessage("This action is not available for the selected revision status.", BannerTone.Warning);
                MessageBox.Show(this, "Selected version is not in the expected state for this action.", "Status", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                SetActivityMessage("Revision changed by another user. Refresh and retry.", BannerTone.Warning);
                MessageBox.Show(this, "Revision was updated by another user. Reload and try again.", "Concurrency Conflict", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            await LoadVersionsAsync(v.DocumentId);
            return;
        }

        if (writeAudit)
        {
            var auditCmd = new SqlCommand(@"
INSERT INTO dbo.AuditEvents (EventUtc, ActorUserId, EntityType, EntityId, EventType, NewValuesJson, Source)
VALUES (SYSUTCDATETIME(), @uid, 'DocumentVersion', @id, 'StatusChanged', @json, 'Desktop');", cn);
            AddParam(auditCmd, "@uid", SqlDbType.UniqueIdentifier, _actorUserId);
            AddParam(auditCmd, "@id", SqlDbType.BigInt, v.DocumentVersionId);
            AddNVarCharMax(auditCmd, "@json", $"{{\"Status\":\"{ToStatusText(next)}\"}}");
            await auditCmd.ExecuteNonQueryAsync();
        }

        await LoadVersionsAsync(v.DocumentId);
        SetActivityMessage(next == StatusSubmitted
            ? "Revision submitted for approval."
            : $"Revision moved to {ToStatusText(next)}.", BannerTone.Success);
    }

    private async Task DecideAsync(byte targetStatus, int decision)
    {
        if (VersionsGrid.SelectedItem is not VersionRow v)
        {
            SetActivityMessage("Select a revision first.", BannerTone.Info);
            MessageBox.Show(this, "Select a version first.", "Approval", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var cs = GetConnectionString();
        if (string.IsNullOrWhiteSpace(cs)) return;

        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync();

        var oldStatusText = ToStatusText(StatusSubmitted);
        var newStatusText = ToStatusText(targetStatus);
        await using var tx = await cn.BeginTransactionAsync();

        var updateCmd = new SqlCommand(@"
UPDATE dbo.DocumentVersions
SET Status=@status, UpdatedByUserId=@uid, UpdatedUtc=SYSUTCDATETIME()
WHERE DocumentVersionId=@id AND Status=@expected AND RowVersion=@rowVersion;", cn, (SqlTransaction)tx);
        AddParam(updateCmd, "@status", SqlDbType.TinyInt, targetStatus);
        AddParam(updateCmd, "@uid", SqlDbType.UniqueIdentifier, _actorUserId);
        AddParam(updateCmd, "@id", SqlDbType.BigInt, v.DocumentVersionId);
        AddParam(updateCmd, "@expected", SqlDbType.TinyInt, StatusSubmitted);
        AddRowVersionParam(updateCmd, "@rowVersion", v.RowVersion);
        var rows = await updateCmd.ExecuteNonQueryAsync();
        if (rows == 0)
        {
            await tx.RollbackAsync();
            var checkCmd = new SqlCommand("SELECT Status FROM dbo.DocumentVersions WHERE DocumentVersionId=@id", cn);
            AddParam(checkCmd, "@id", SqlDbType.BigInt, v.DocumentVersionId);
            var current = await checkCmd.ExecuteScalarAsync();
            if (current is null or DBNull)
            {
                SetActivityMessage("Selected revision no longer exists. Refreshing list.", BannerTone.Warning);
                MessageBox.Show(this, "Selected revision no longer exists.", "Approval", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else if (Convert.ToByte(current) != StatusSubmitted)
            {
                SetActivityMessage("Approve/Reject is only available when status is Submitted.", BannerTone.Warning);
                MessageBox.Show(this, "Approve/Reject requires Submitted status.", "Approval", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                SetActivityMessage("Revision changed by another user. Refresh and retry.", BannerTone.Warning);
                MessageBox.Show(this, "Revision was updated by another user. Reload and try again.", "Concurrency Conflict", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            await LoadVersionsAsync(v.DocumentId);
            return;
        }

        var approvalCmd = new SqlCommand(@"
INSERT INTO dbo.ApprovalRecords (DocumentVersionId, Decision, Comment, DecidedByUserId, DecidedUtc)
VALUES (@id, @decision, @comment, @uid, SYSUTCDATETIME());", cn, (SqlTransaction)tx);
        AddParam(approvalCmd, "@id", SqlDbType.BigInt, v.DocumentVersionId);
        AddParam(approvalCmd, "@decision", SqlDbType.TinyInt, decision);
        AddNVarChar(approvalCmd, "@comment", DocumentDescriptionMax, decision == 1 ? "Approved from Operations module" : "Rejected from Operations module");
        AddParam(approvalCmd, "@uid", SqlDbType.UniqueIdentifier, _actorUserId);
        await approvalCmd.ExecuteNonQueryAsync();

        var auditCmd = new SqlCommand(@"
INSERT INTO dbo.AuditEvents (EventUtc, ActorUserId, EntityType, EntityId, EventType, OldValuesJson, NewValuesJson, Source)
VALUES (SYSUTCDATETIME(), @uid, 'DocumentVersion', @id, 'StatusChanged', @oldJson, @newJson, 'Desktop');", cn, (SqlTransaction)tx);
        AddParam(auditCmd, "@uid", SqlDbType.UniqueIdentifier, _actorUserId);
        AddParam(auditCmd, "@id", SqlDbType.BigInt, v.DocumentVersionId);
        AddNVarCharMax(auditCmd, "@oldJson", $"{{\"Status\":\"{oldStatusText}\"}}");
        AddNVarCharMax(auditCmd, "@newJson", $"{{\"Status\":\"{newStatusText}\"}}");
        await auditCmd.ExecuteNonQueryAsync();

        await tx.CommitAsync();
        await LoadVersionsAsync(v.DocumentId);
        SetActivityMessage(decision == 1 ? "Revision approved." : "Revision rejected.", BannerTone.Success);
    }

    private async Task PublishAsync()
    {
        if (VersionsGrid.SelectedItem is not VersionRow v)
        {
            SetActivityMessage("Select a revision first.", BannerTone.Info);
            MessageBox.Show(this, "Select a version first.", "Publish", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var cs = GetConnectionString();
        if (string.IsNullOrWhiteSpace(cs)) return;

        try
        {
            await using var cn = new SqlConnection(cs);
            await cn.OpenAsync();
            if (v.Status != StatusApproved)
            {
                SetActivityMessage("Publish is only available when status is Approved.", BannerTone.Warning);
                MessageBox.Show(this, "Publish requires Approved status.", "Publish", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var oldStatusText = ToStatusText(StatusApproved);
            var oldIsOfficial = v.IsCurrentOfficial;
            await using var tx = await cn.BeginTransactionAsync();

            var clearCmd = new SqlCommand("UPDATE dbo.DocumentVersions SET IsCurrentOfficial=0, UpdatedByUserId=@uid, UpdatedUtc=SYSUTCDATETIME() WHERE DocumentId=@docId AND IsCurrentOfficial=1", cn, (SqlTransaction)tx);
            AddParam(clearCmd, "@uid", SqlDbType.UniqueIdentifier, _actorUserId);
            AddParam(clearCmd, "@docId", SqlDbType.BigInt, v.DocumentId);
            await clearCmd.ExecuteNonQueryAsync();

            var publishCmd = new SqlCommand(@"
UPDATE dbo.DocumentVersions
SET Status=4, IsCurrentOfficial=1, UpdatedByUserId=@uid, UpdatedUtc=SYSUTCDATETIME()
WHERE DocumentVersionId=@id AND Status=@expected AND RowVersion=@rowVersion;", cn, (SqlTransaction)tx);
            AddParam(publishCmd, "@uid", SqlDbType.UniqueIdentifier, _actorUserId);
            AddParam(publishCmd, "@id", SqlDbType.BigInt, v.DocumentVersionId);
            AddParam(publishCmd, "@expected", SqlDbType.TinyInt, StatusApproved);
            AddRowVersionParam(publishCmd, "@rowVersion", v.RowVersion);
            var publishRows = await publishCmd.ExecuteNonQueryAsync();
            if (publishRows == 0)
            {
                await tx.RollbackAsync();
                var checkCmd = new SqlCommand("SELECT Status FROM dbo.DocumentVersions WHERE DocumentVersionId=@id", cn);
                AddParam(checkCmd, "@id", SqlDbType.BigInt, v.DocumentVersionId);
                var current = await checkCmd.ExecuteScalarAsync();
                if (current is null or DBNull)
                {
                    SetActivityMessage("Selected revision no longer exists. Refreshing list.", BannerTone.Warning);
                    MessageBox.Show(this, "Selected revision no longer exists.", "Publish", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else if (Convert.ToByte(current) != StatusApproved)
                {
                    SetActivityMessage("Publish is only available when status is Approved.", BannerTone.Warning);
                    MessageBox.Show(this, "Publish requires Approved status.", "Publish", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    SetActivityMessage("Revision changed by another user. Refresh and retry.", BannerTone.Warning);
                    MessageBox.Show(this, "Revision was updated by another user. Reload and try again.", "Concurrency Conflict", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                await LoadVersionsAsync(v.DocumentId);
                return;
            }

            var recCmd = new SqlCommand(@"
INSERT INTO dbo.PublicationRecords (DocumentVersionId, ActionType, Comment, ActedByUserId, ActedUtc)
VALUES (@id, 1, 'Published from Operations module', @uid, SYSUTCDATETIME());", cn, (SqlTransaction)tx);
            AddParam(recCmd, "@id", SqlDbType.BigInt, v.DocumentVersionId);
            AddParam(recCmd, "@uid", SqlDbType.UniqueIdentifier, _actorUserId);
            await recCmd.ExecuteNonQueryAsync();

            var auditCmd = new SqlCommand(@"
INSERT INTO dbo.AuditEvents (EventUtc, ActorUserId, EntityType, EntityId, EventType, OldValuesJson, NewValuesJson, Source)
VALUES (SYSUTCDATETIME(), @uid, 'DocumentVersion', @id, 'StatusChanged', @oldJson, @newJson, 'Desktop');", cn, (SqlTransaction)tx);
            AddParam(auditCmd, "@uid", SqlDbType.UniqueIdentifier, _actorUserId);
            AddParam(auditCmd, "@id", SqlDbType.BigInt, v.DocumentVersionId);
            AddNVarCharMax(auditCmd, "@oldJson", $"{{\"Status\":\"{oldStatusText}\",\"IsCurrentOfficial\":{(oldIsOfficial ? "true" : "false")}}}");
            AddNVarCharMax(auditCmd, "@newJson", "{\"Status\":\"Published\",\"IsCurrentOfficial\":true}");
            await auditCmd.ExecuteNonQueryAsync();

            await tx.CommitAsync();
            await LoadVersionsAsync(v.DocumentId);
            SetActivityMessage("Revision published and set as the official version.", BannerTone.Success);
        }
        catch (SqlException ex)
        {
            SetActivityMessage("Publish failed. Try refreshing and publishing again.", BannerTone.Error);
            MessageBox.Show(this, ex.Message, "Publish Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private sealed class DocumentRow
    {
        public long DocumentId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string GroupName { get; set; } = "Ungrouped";
        public string CurrentOfficialText { get; set; } = "None";
        public string LatestStatusText { get; set; } = "None";
    }

    private sealed class VersionRow
    {
        public long DocumentVersionId { get; init; }
        public long DocumentId { get; init; }
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

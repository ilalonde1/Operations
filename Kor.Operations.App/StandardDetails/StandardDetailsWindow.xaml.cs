#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Kor.Operations.App.Options;
using Kor.Operations.Services;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Kor.Operations.StandardDetails;

public partial class StandardDetailsWindow : Window
{
    private const byte StatusDraft = 0;
    private const byte StatusSubmitted = 1;
    private const byte StatusApproved = 2;
    private const byte StatusRejected = 3;
    private const byte StatusPublished = 4;
    private readonly Guid _actorUserId = CreateStableUserGuid(Environment.UserName);
    private long? _selectedGroupId;
    private int _groupCount;
    private List<DocumentRow> _documentSnapshot = new();
    private List<VersionRow> _versionSnapshot = new();
    private bool _groupSchemaAvailable = true;
    private bool _filterRecordsBySelectedGroup;
    private StandardDetailsRepository? _repo;
    private KorStandardsReadRepository? _korStandardsRepo;
    private KorStandardsPromoterRepository? _promoterRepo;
    private StandardDetailsAccessPolicy? _policy;
    private StandardDetailsFileStore? _fileStore;
    private enum BannerTone { Info, Success, Warning, Error }

    public StandardDetailsWindow()
    {
        InitializeComponent();
        Loaded += StandardDetailsWindow_Loaded;
    }

    private async void StandardDetailsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try { await HeaderLoader.ApplyAsync(HeaderBar); }
        catch (Exception ex) { Log.Warning(ex, "Standard Details: header loader failed."); }

        var databaseOptions = Kor.Operations.Services.AppServices.Get<DatabaseOptions>();
        var connectionString = databaseOptions.KorTransmittalsDb;
        if (!string.IsNullOrWhiteSpace(connectionString))
            _repo = new StandardDetailsRepository(connectionString);
        if (!string.IsNullOrWhiteSpace(databaseOptions.KorStandardsDb))
            _korStandardsRepo = new KorStandardsReadRepository(databaseOptions.KorStandardsDb);
        if (!string.IsNullOrWhiteSpace(databaseOptions.KorStandardsPromoterDb))
            _promoterRepo = new KorStandardsPromoterRepository(databaseOptions.KorStandardsPromoterDb);
        var storageRoot = StandardDetailsFileStore.NormalizeStorageRoot(Kor.Operations.Services.AppServices.Get<StorageOptions>().StandardDetailsFileStorageRootPath);
        _fileStore = new StandardDetailsFileStore(storageRoot);
        _policy = new StandardDetailsAccessPolicy(StandardDetailsAccessPolicy.ResolveCurrentUserIdentity(Kor.Operations.Services.AppServices.Get<UserOptions>(), HeaderBar?.UserEmail));
        _filterRecordsBySelectedGroup = FilterByGroupCheckBox.IsChecked == true;
        await EnsureGroupSchemaStateAsync();
        await LoadGroupsUiAsync();
        await LoadDocumentsUiAsync();
        UpdateActionStates();
        SetActivityMessage("Ready.", BannerTone.Info);
    }

    private static Guid CreateStableUserGuid(string input)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input ?? "unknown"));
        var bytes = new byte[16];
        Array.Copy(hash, bytes, 16);
        return new Guid(bytes);
    }

    private bool EnsureCanContribute(string action) => EnsureAllowed(_policy?.CanContribute() == true, $"You do not have permission to {action}.", "Permission");
    private bool EnsureCanApproveReject(string action) => EnsureAllowed(_policy?.CanApproveOrReject() == true, $"You do not have permission to {action}.", "Permission");
    private bool EnsureCanPublishAction() => EnsureAllowed(_policy?.CanPublish() == true, "You do not have permission to publish versions.", "Permission");
    private bool EnsureCanManageGroupAction(string action) => EnsureAllowed(_policy?.CanManageGroups() == true, $"You do not have permission to {action}.", "Permission");
    private bool EnsureGroupFeatureAvailable() => EnsureAllowed(_groupSchemaAvailable, "Group features are unavailable for this database login.", "Group Features Unavailable", "Group features are unavailable with the current database permissions. Core record and revision workflow is still available.", MessageBoxImage.Information);

    private bool EnsureAllowed(bool allowed, string message, string caption, string? dialogMessage = null, MessageBoxImage icon = MessageBoxImage.Warning)
    {
        if (allowed) return true;
        SetActivityMessage(message, BannerTone.Warning);
        MessageBox.Show(this, dialogMessage ?? message, $"Standard Details — {caption}", MessageBoxButton.OK, icon);
        return false;
    }

    private void SetActivityMessage(string message, BannerTone tone)
    {
        ActivityMessageText.Text = message;
        ActivityMessageText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(tone switch
        {
            BannerTone.Success => "#FF1E6B3A",
            BannerTone.Warning => "#FF8A5A00",
            BannerTone.Error => "#FF9D1C1C",
            _ => "#FF5E7185"
        }));
    }

    private void UpdateHeroMetrics()
    {
        var recordCount = _documentSnapshot.Count;
        if (recordCount == 0)
        {
            HeroSummaryText.Text = "No records yet. Click New Record to start.";
            return;
        }

        var official = _documentSnapshot.Count(x => !string.Equals(x.CurrentOfficialText, "None", StringComparison.OrdinalIgnoreCase));
        var awaiting = _documentSnapshot.Count(x => string.Equals(x.LatestStatusText, "Submitted", StringComparison.OrdinalIgnoreCase));
        HeroSummaryText.Text = $"{recordCount} records · {official} official · {awaiting} awaiting review";
    }

    private void UpdateActionStates()
    {
        var selectedGroup = GroupsTree.SelectedItem as GroupNode;
        var selectedDoc = DocumentsGrid.SelectedItem as DocumentRow;
        var selectedVersion = VersionsGrid.SelectedItem as VersionRow;
        var canManageGroups = (_policy?.CanManageGroups() == true) && _groupSchemaAvailable;
        var canContribute = _policy?.CanContribute() == true;
        var canAssignToSelectedGroup = selectedGroup is not null && (selectedGroup.GroupId is not null || string.Equals(selectedGroup.Name, "All Records", StringComparison.OrdinalIgnoreCase));

        AddGroupButton.IsEnabled = canManageGroups; AddSubgroupButton.IsEnabled = canManageGroups && selectedGroup?.GroupId is not null; RenameGroupButton.IsEnabled = canManageGroups && selectedGroup?.GroupId is not null; RemoveGroupButton.IsEnabled = canManageGroups && selectedGroup?.GroupId is not null;
        CreateRecordButton.IsEnabled = canContribute; UploadVersionButton.IsEnabled = canContribute && selectedDoc is not null; LinkDetailButton.IsEnabled = canContribute && selectedDoc is not null && _korStandardsRepo is not null; RegistersButton.IsEnabled = _korStandardsRepo is not null; AssignRecordButton.IsEnabled = canContribute && selectedDoc is not null && _groupSchemaAvailable && canAssignToSelectedGroup; DeleteRecordButton.IsEnabled = canContribute && selectedDoc is not null;
        OpenFileButton.IsEnabled = selectedVersion is not null; SubmitButton.IsEnabled = canContribute && selectedVersion is not null && selectedVersion.Status == StatusDraft; ApproveButton.IsEnabled = (_policy?.CanApproveOrReject() == true) && selectedVersion is not null && selectedVersion.Status == StatusSubmitted; RejectButton.IsEnabled = (_policy?.CanApproveOrReject() == true) && selectedVersion is not null && selectedVersion.Status == StatusSubmitted; PublishButton.IsEnabled = (_policy?.CanPublish() == true) && selectedVersion is not null && selectedVersion.Status == StatusApproved;
    }

    private static void SetStepChipVisual(Border chip, TextBlock label, bool isActive)
    {
        chip.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isActive ? "#FFDCEEF9" : "#FFF1F3F6"));
        chip.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isActive ? "#FF9FC6E3" : "#FFD9DEE5"));
        label.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isActive ? "#FF114F78" : "#FF4F6275"));
    }
}

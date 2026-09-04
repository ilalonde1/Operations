#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
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
    private string _userIdentity = "operations";
    private string? _selectedDiscipline;   // null = All disciplines
    private string? _selectedKind;         // null = All detail kinds
    private bool _syncingKindUi;           // guards programmatic kind-combo updates
    private bool _syncingSheetUi;          // guards programmatic IsSheet checkbox updates
    private bool _partsMode;               // true = Parts tab
    private bool _sheetsMode;              // true = Sheets tab
    private int _previewToken;             // guards against a slow image load landing after the selection moved on
    private bool _uiReady;                 // true after Loaded — chip/tab Checked events fire during XAML init and must no-op until then
    private string _partImageRoot = "";    // Quick Insert imageRoot (QuickPick\BMP) — bare thumbnail names resolve here
    private StandardDetailsFileStore? _fileStore;
    private StandardDetailsMasterPublishOptions? _masterPublishOptions;
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
        var storageOptions = Kor.Operations.Services.AppServices.Get<StorageOptions>();
        var storageRoot = StandardDetailsFileStore.NormalizeStorageRoot(storageOptions.StandardDetailsFileStorageRootPath);
        _fileStore = new StandardDetailsFileStore(storageRoot);
        _masterPublishOptions = new StandardDetailsMasterPublishOptions(
            storageOptions.StandardDetailsAuthoringPath,
            storageOptions.StandardDetailsMasterPath,
            storageOptions.StandardDetailsBridgeRoot,
            storageOptions.StandardDetailsPreviewCachePath);
        _partImageRoot = storageOptions.StandardDetailsPartImageRoot;
        _userIdentity = StandardDetailsAccessPolicy.ResolveCurrentUserIdentity(Kor.Operations.Services.AppServices.Get<UserOptions>(), HeaderBar?.UserEmail);
        _policy = new StandardDetailsAccessPolicy(_userIdentity);
        _filterRecordsBySelectedGroup = FilterByGroupCheckBox.IsChecked == true;
        await EnsureGroupSchemaStateAsync();
        ApplyCatalogModeLayout(null);
        await LoadGroupsUiAsync();
        await LoadDocumentsUiAsync();
        UpdateActionStates();
        _uiReady = true;
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
        var n = _documentSnapshot.Count;
        if (_partsMode)
        {
            var active = _documentSnapshot.Count(x => string.Equals(x.StatusLabel, "Active", StringComparison.Ordinal));
            var retired = _documentSnapshot.Count(x => string.Equals(x.StatusLabel, "Retired", StringComparison.Ordinal));
            HeroSummaryText.Text = n == 0 ? "No parts" : $"{n} parts  ·  {active} active  ·  {retired} retired";
        }
        else if (_sheetsMode)
        {
            var collections = _documentSnapshot.Select(x => x.ViewGroup).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var approved = _documentSnapshot.Count(x => string.Equals(x.StatusLabel, "Approved", StringComparison.Ordinal));
            HeroSummaryText.Text = n == 0 ? "No sheets" : $"{n} sheets  ·  {collections} collections  ·  {approved} approved";
        }
        else
        {
            var approved = _documentSnapshot.Count(x => string.Equals(x.StatusLabel, "Approved", StringComparison.Ordinal));
            var pending = _documentSnapshot.Count(x => string.Equals(x.StatusLabel, "Pending", StringComparison.Ordinal) || string.Equals(x.StatusLabel, "Held", StringComparison.Ordinal));
            HeroSummaryText.Text = n == 0 ? "No details" : $"{n} details  ·  {approved} approved  ·  {pending} pending";
        }
    }

    // Drives the right-hand pane from the selected row: title, subtitle, status pill, drawing (details
    // only), and which action affordances make sense. Parts have no standalone drawing and no approve
    // action — the pane says so rather than showing dead buttons.
    // Sets the text/status/action side of the pane synchronously. The drawing itself is loaded
    // asynchronously from the DB store by LoadPreviewAsync (details AND parts are approvable and both
    // carry art now), so this shows a placeholder and lets the image arrive.
    private void UpdateDetailPane(DocumentRow? row)
    {
        ApplyCatalogModeLayout(row);
        if (row is null)
        {
            DetailTitleText.Text = _partsMode ? "Select a part" : _sheetsMode ? "Select a sheet" : "Select a detail";
            DetailSubtitleText.Text = _partsMode ? "Pick a part on the left to review it." : _sheetsMode ? "Pick a sheet on the left to review it." : "Pick a detail on the left to review it.";
            DetailStatusPill.Visibility = Visibility.Collapsed;
            ActionHintText.Text = _partsMode
                ? "Approve a part and it goes into the Quick Insert palette."
                : _sheetsMode
                    ? "Sheets are reviewed in the larger pane and can be opened as PDFs."
                    : "This is the actual drawing. Approve it and it goes into the drafters' palette.";
            ApproveButton.Visibility = Visibility.Visible;
            RejectButton.Visibility = Visibility.Visible;
            DrawingFootnote.Visibility = Visibility.Collapsed;
            SetDetailKindControl(null, false);
            SetDetailSheetControl(false, false);
            ShowPreviewEmpty(_partsMode ? "Select a part to see it." : _sheetsMode ? "Select a sheet to see it." : "Select a detail to see its drawing.");
            return;
        }

        DetailTitleText.Text = string.IsNullOrWhiteSpace(row.Title) ? row.DetailNumber : row.Title;
        DetailSubtitleText.Text = row.RightSubtitle;
        ApplyStatusPill(row.StatusLabel);
        ApproveButton.Visibility = Visibility.Visible;
        RejectButton.Visibility = Visibility.Visible;
        ActionHintText.Text = row.IsPart
            ? "Approve → the part goes into the Quick Insert palette.  Reject → it never does."
            : row.IsSheet
                ? "This sheet is a catalog detail. Open its PDF or move it back to Details if it was misclassified."
                : "This is the actual drawing. Approve it and it goes into the drafters' palette.";
        DrawingFootnote.Visibility = Visibility.Collapsed;
        SetDetailKindControl(row.Kind, row.IsDetail);
        SetDetailSheetControl(row.IsSheet, row.IsDetail);
        ShowPreviewEmpty(row.IsPart ? "Loading part image…" : "Loading drawing…");
    }

    // Loads the art from the governed DB store (detail.RenderedImage), keyed by identity. Details fall
    // back to the fs01 preview cache until they are ingested into the store; parts that have no image
    // yet say so honestly. A token guards against a slow load landing after the selection moved on.
    private async Task LoadPreviewAsync(DocumentRow? row)
    {
        var token = ++_previewToken;
        if (row is null) return;

        if (_korStandardsRepo is not null)
        {
            var kind = row.IsDetail ? "detail" : "component";
            var key = row.IsDetail ? row.DetailNumber : $"{row.FamilyName}|{row.TypeName}";
            byte[]? bytes = null;
            try { bytes = await _korStandardsRepo.LoadRenderedImageAsync(kind, key); }
            catch (Exception ex) { Log.Warning(ex, "Standard Details: rendered image load failed for {Kind} {Key}.", kind, key); }
            if (token != _previewToken) return;
            if (bytes is { Length: > 0 })
            {
                ShowPreviewBytes(bytes);
                if (row.IsDetail) SetDrawingFootnote(row.DetailNumber);
                return;
            }
        }

        if (token != _previewToken) return;
        if (row.IsDetail)
        {
            SetDetailPreview(row.DetailNumber); // fs01 fallback until details are ingested
            if (PreviewImage.Source is not null) SetDrawingFootnote(row.DetailNumber);
        }
        else
        {
            ShowPreviewEmpty("No image rendered for this part yet — the next parts render adds it to the store.");
        }
    }

    private void SetDrawingFootnote(string detailNumber)
    {
        DrawingFootnote.Text = $"KOR-Standards-Authoring-R25.rvt  ·  {detailNumber}";
        DrawingFootnote.Visibility = Visibility.Visible;
    }

    private void ShowPreviewBytes(byte[] png)
    {
        try
        {
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            using (var ms = new MemoryStream(png))
            {
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
            }
            bmp.Freeze();
            PreviewImage.Source = bmp;
            PreviewImage.Visibility = Visibility.Visible;
            ZoomHintBadge.Visibility = Visibility.Visible;
            PreviewEmpty.Visibility = Visibility.Collapsed;
        }
        catch
        {
            ShowPreviewEmpty("Could not load the image.");
        }
    }

    private void ShowPreviewEmpty(string message)
    {
        PreviewImage.Source = null;
        PreviewImage.Visibility = Visibility.Collapsed;
        ZoomHintBadge.Visibility = Visibility.Collapsed;
        PreviewEmpty.Text = message;
        PreviewEmpty.Visibility = Visibility.Visible;
    }

    private void ApplyStatusPill(string label)
    {
        string bg, fg;
        var text = label switch
        {
            "Approved" => "Approved",
            "Pending" => "Pending review",
            "Held" => "On hold",
            "Rejected" => "Rejected",
            "Active" => "Active",
            "Retired" => "Retired",
            _ => label
        };
        switch (label)
        {
            case "Approved":
            case "Active":
                bg = "#FFE7F4EA"; fg = "#FF2C7A3F"; break;
            case "Pending":
            case "Held":
                bg = "#FFFBF3E2"; fg = "#FF8A6D1F"; break;
            case "Rejected":
                bg = "#FFFBECEB"; fg = "#FFB23A2E"; break;
            default:
                bg = "#FFEFF1F3"; fg = "#FF5B636D"; break;
        }
        DetailStatusPill.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg));
        DetailStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fg));
        DetailStatusPillText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fg));
        DetailStatusPillText.Text = text;
        DetailStatusPill.Visibility = Visibility.Visible;
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
        CreateRecordButton.IsEnabled = canContribute; UploadVersionButton.IsEnabled = canContribute && selectedDoc is { IsDetail: false }; LinkDetailButton.IsEnabled = canContribute && selectedDoc is { IsDetail: false } && _korStandardsRepo is not null; RegistersButton.IsEnabled = _korStandardsRepo is not null; PublishToMasterButton.IsEnabled = _korStandardsRepo is not null && (_policy?.CanPublish() == true); ComposeSheetButton.IsEnabled = _repo is not null && _korStandardsRepo is not null && (_policy?.CanPublish() == true); AssignRecordButton.IsEnabled = canContribute && selectedDoc is { IsDetail: false } && _groupSchemaAvailable && canAssignToSelectedGroup; DeleteRecordButton.IsEnabled = canContribute && selectedDoc is { IsDetail: false };
        OpenFileButton.IsEnabled = selectedVersion is not null; SubmitButton.IsEnabled = canContribute && selectedVersion is not null && selectedVersion.Status == StatusDraft; ApproveButton.IsEnabled = (_policy?.CanApproveOrReject() == true) && (((selectedDoc is { IsDetail: true } or { IsPart: true }) && _promoterRepo is not null) || (selectedVersion is not null && selectedVersion.Status == StatusSubmitted)); RejectButton.IsEnabled = (_policy?.CanApproveOrReject() == true) && (((selectedDoc is { IsDetail: true } or { IsPart: true }) && _promoterRepo is not null) || (selectedVersion is not null && selectedVersion.Status == StatusSubmitted)); PublishButton.IsEnabled = (_policy?.CanPublish() == true) && selectedVersion is not null && selectedVersion.Status == StatusApproved;
        DetailKindCombo.IsEnabled = selectedDoc is { IsDetail: true } && _promoterRepo is not null && _policy?.CanApproveOrReject() == true;
        DetailIsSheetCheckBox.IsEnabled = selectedDoc is { IsDetail: true } && _promoterRepo is not null && _policy?.CanApproveOrReject() == true;
        OpenSheetPdfButton.IsEnabled = selectedDoc is { IsDetail: true, IsSheet: true } && _masterPublishOptions?.IsConfigured == true;
    }

    private void ApplyCatalogModeLayout(DocumentRow? row)
    {
        var sheetLayout = _sheetsMode || row?.IsSheet == true;
        CatalogListColumn.MinWidth = sheetLayout ? 470 : 360;
        CatalogListColumn.Width = new GridLength(sheetLayout ? 1.65 : 2.25, GridUnitType.Star);
        DetailPaneColumn.Width = new GridLength(sheetLayout ? 4 : 3, GridUnitType.Star);
        PreviewImage.Margin = new Thickness(sheetLayout ? 10 : 26);
        PreviewEmpty.MaxWidth = sheetLayout ? 520 : 360;
    }

    // The drawing previews are PRE-RENDERED to a SHARED cache (beside the master template on the
    // Drafting share), so every reviewer loads the SAME images — nobody needs Revit or the bridge to
    // VIEW a detail. The bridge only (re)generates this cache when a detail changes.
    private string ResolvePreviewCacheDir()
    {
        // The shared fs01 cache path (StandardDetails.PreviewCachePath in App.config) — the drawing
        // previews live on the file server with the templates, so the whole fleet reads the same
        // images and nobody needs Revit or the bridge to VIEW a detail.
        var configured = _masterPublishOptions?.PreviewCachePath;
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        return @"\\Kor-fs01\Drafting\KOR-Standards\detail-previews";
    }

    // Shows the selected detail's pre-rendered drawing from the shared cache. Fully guarded — a
    // missing/broken image never throws into the UI; it just shows the fallback.
    private void SetDetailPreview(string? detailNumber)
    {
        try
        {
            string? file = null;
            if (!string.IsNullOrWhiteSpace(detailNumber))
            {
                var candidate = System.IO.Path.Combine(ResolvePreviewCacheDir(), detailNumber.Trim() + ".png");
                if (System.IO.File.Exists(candidate)) file = candidate;
            }

            if (file is null)
            {
                PreviewImage.Source = null;
                PreviewImage.Visibility = Visibility.Collapsed;
                ZoomHintBadge.Visibility = Visibility.Collapsed;
                PreviewEmpty.Text = "Select a detail to see its drawing.";
                PreviewEmpty.Visibility = Visibility.Visible;
                return;
            }

            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(file);
            bmp.EndInit();
            PreviewImage.Source = bmp;
            PreviewImage.Visibility = Visibility.Visible;
            ZoomHintBadge.Visibility = Visibility.Visible;
            PreviewEmpty.Visibility = Visibility.Collapsed;
        }
        catch
        {
            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            ZoomHintBadge.Visibility = Visibility.Collapsed;
            PreviewEmpty.Visibility = Visibility.Visible;
        }
    }
}

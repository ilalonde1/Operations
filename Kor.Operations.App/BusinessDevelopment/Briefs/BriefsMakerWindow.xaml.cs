#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Awards;
using Kor.Opportunities.Data.Briefs;

namespace Kor.Operations.App.BusinessDevelopment.Briefs;

public partial class BriefsMakerWindow : Window, Kor.Operations.Services.IAiContextProvider
{
    // BD-Audit-2026-06-09 M16: expose Briefs Maker state to AI. This window is
    // code-behind only (no VM), so the provider lives on the window itself.
    // _activeTabIndex mirrors TypeTabs.SelectedIndex so BuildContext never has
    // to touch a DependencyObject off the UI thread.
    public string ProviderName => "BD Briefs Maker";
    public bool HasData => true;
    public string BuildContext()
        => _activeTabIndex switch
        {
            0 => "BD Briefs Maker — Organization brief tab; selected org: "
                 + (_selectedOrg is { } org ? $"{org.DisplayName} ({org.Kind}, canonical org id {org.Id})" : "(none)") + ".",
            1 => "BD Briefs Maker — Region brief tab; region: "
                 + (_selectedProvince is null ? "(none)" : _selectedProvince + (_selectedCity is null ? "" : $" / {_selectedCity}")) + ".",
            2 => "BD Briefs Maker — Project brief tab; selected project: "
                 + (_selectedProject is { } proj ? $"{proj.ProjectName} (id {_selectedProjectId ?? proj.Id})" : "(none)") + ".",
            3 => "BD Briefs Maker — Person brief tab; selected person: "
                 + (_selectedPerson is { } person ? $"{person.DisplayName} (id {_selectedPersonId ?? person.IntelPersonId})" : "(none)") + ".",
            4 => "BD Briefs Maker — Sector brief tab; geography: "
                 + (_sectorProvince is null ? "(none)" : _sectorProvince + (_sectorCity is null ? "" : $" / {_sectorCity}"))
                 + (_sectorBuckets.Count > 0 ? $"; sectors: {string.Join(" + ", _sectorBuckets)}" : "")
                 + (_sectorStructuralTypes.Count > 0 ? $"; structural types: {string.Join(" + ", _sectorStructuralTypes)}" : "") + ".",
            _ => "BD Briefs Maker — open.",
        };
    public string BuildLocalContext() => BuildContext();

    private bool _aiRegistered;
    private int _activeTabIndex;

    private static readonly Dictionary<string, string[]> CityByProvince = new(StringComparer.Ordinal)
    {
        ["BC"] = new[] { "(All cities)", "Vancouver", "GVRD", "Victoria", "Kelowna", "Nanaimo", "Burnaby", "Surrey" },
        ["AB"] = new[] { "(All cities)", "Calgary", "Edmonton", "Red Deer", "Lethbridge", "Cardston" },
        ["ON"] = new[] { "(All cities)", "Toronto", "Ottawa", "Hamilton" },
        ["QC"] = new[] { "(All cities)", "Montreal", "Quebec City" },
        ["Other"] = new[] { "(All cities)" },
    };

    private readonly ICanonicalOrgStore _canonicalOrgStore;
    private readonly IBriefDataStore _briefStore;
    private readonly IBriefGenerator _wordGen;
    private readonly IBriefPdfGenerator _pdfGen;

    private CanonicalOrgRow? _selectedOrg;
    // Latest-requested picker ids. Each picker handler awaits a load and then
    // re-checks these before writing _selected* — two rapid picks A->B where
    // A's load lands last must not leave A's data under B's id (the Generate
    // path names the output file by id but writes _selected* content).
    private long? _selectedOrgId;
    private string? _selectedProvince;
    private string? _selectedCity;
    private ProjectBriefData? _selectedProject;
    private long? _selectedProjectId;
    private PersonBriefData? _selectedPerson;
    private long? _selectedPersonId;
    private string? _sectorProvince;
    private string? _sectorCity;
    private readonly HashSet<string> _sectorBuckets = new(StringComparer.Ordinal);
    private readonly HashSet<string> _sectorStructuralTypes = new(StringComparer.Ordinal);

    public BriefsMakerWindow(
        ICanonicalOrgStore canonicalOrgStore,
        IBriefDataStore briefStore,
        IBriefGenerator wordGen,
        IBriefPdfGenerator pdfGen)
    {
        _canonicalOrgStore = canonicalOrgStore ?? throw new ArgumentNullException(nameof(canonicalOrgStore));
        _briefStore = briefStore ?? throw new ArgumentNullException(nameof(briefStore));
        _wordGen = wordGen ?? throw new ArgumentNullException(nameof(wordGen));
        _pdfGen = pdfGen ?? throw new ArgumentNullException(nameof(pdfGen));

        InitializeComponent();
        OrgPicker.Store = canonicalOrgStore;
        OrgPicker.OrgSelected += OrgPicker_OrgSelected;
        ProjectPicker.Search = (q, ct) => _briefStore.SearchProjectsAsync(q, 20, ct);
        ProjectPicker.ProjectSelected += ProjectPicker_ProjectSelected;
        PersonPicker.Search = (q, ct) => _briefStore.SearchPeopleAsync(q, 20, ct);
        PersonPicker.PersonSelected += PersonPicker_PersonSelected;
        TypeTabs.SelectionChanged += TypeTabs_SelectionChanged;
        _activeTabIndex = TypeTabs.SelectedIndex;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // BD-Audit-2026-06-09 M16: let the AI assistant see the open Briefs Maker.
        if (!_aiRegistered)
        {
            Kor.Operations.Services.AppServices.Get<Kor.Operations.Services.AppAiContextBuilder>().Register(this);
            _aiRegistered = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_aiRegistered)
        {
            Kor.Operations.Services.AppServices.Get<Kor.Operations.Services.AppAiContextBuilder>().Unregister(this);
            _aiRegistered = false;
        }

        base.OnClosed(e);
    }

    private async void OrgPicker_OrgSelected(object? sender, long orgId)
    {
        _selectedOrgId = orgId;
        Kor.Opportunities.Core.Models.CanonicalOrgRow? row;
        try
        {
            row = await _canonicalOrgStore.GetCanonicalOrgAsync(orgId, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // async void picker handler: an unguarded store failure here crashed
            // the whole app on the first click in this window (audit 2026-07-01).
            Serilog.Log.Warning(ex, "BriefsMaker org pick failed for org {OrgId}.", orgId);
            MessageBox.Show(this, $"Could not load that organization:\n{ex.Message}", "Briefs Maker", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (_selectedOrgId != orgId)
        {
            return; // stale load — a newer pick superseded this one
        }

        if (row is null)
        {
            _selectedOrg = null;
            OrgPreview.Visibility = Visibility.Collapsed;
            UpdateGenerateButtonsEnabled();
            return;
        }

        _selectedOrg = row;
        OrgPreviewName.Text = row.DisplayName;
        OrgPreviewMeta.Text = $"{row.Kind}  canonical org id {row.Id}";
        OrgPreview.Visibility = Visibility.Visible;
        UpdateGenerateButtonsEnabled();
    }

    private async void ProjectPicker_ProjectSelected(object? sender, long id)
    {
        _selectedProjectId = id;
        Kor.Opportunities.Data.Briefs.ProjectBriefData? data;
        try
        {
            data = await _briefStore.GetProjectBriefAsync(id, CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "BriefsMaker project pick failed for project {ProjectId}.", id);
            MessageBox.Show(this, $"Could not load that project:\n{ex.Message}", "Briefs Maker", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (_selectedProjectId != id)
        {
            return; // stale load — a newer pick superseded this one
        }

        if (data is null)
        {
            _selectedProject = null;
            ProjectPreview.Visibility = Visibility.Collapsed;
            UpdateGenerateButtonsEnabled();
            return;
        }

        _selectedProject = data;
        ProjectPreviewName.Text = data.ProjectName;
        var meta = $"{data.Stage ?? ""}    {data.City ?? data.Province}    " +
                   (data.EstimatedCostCad is { } v
                       ? v.ToString("C0", CultureInfo.CurrentCulture)
                       : data.EstimatedCostText ?? "");
        ProjectPreviewMeta.Text = meta.Trim();
        ProjectPreview.Visibility = Visibility.Visible;
        UpdateGenerateButtonsEnabled();
    }

    private async void PersonPicker_PersonSelected(object? sender, long id)
    {
        _selectedPersonId = id;
        Kor.Opportunities.Data.Briefs.PersonBriefData? data;
        try
        {
            data = await _briefStore.GetPersonBriefAsync(id, CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "BriefsMaker person pick failed for person {PersonId}.", id);
            MessageBox.Show(this, $"Could not load that person:\n{ex.Message}", "Briefs Maker", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (_selectedPersonId != id)
        {
            return; // stale load — a newer pick superseded this one
        }

        if (data is null)
        {
            _selectedPerson = null;
            PersonPreview.Visibility = Visibility.Collapsed;
            UpdateGenerateButtonsEnabled();
            return;
        }

        _selectedPerson = data;
        PersonPreviewName.Text = data.DisplayName;
        var bits = new List<string>();
        if (!string.IsNullOrWhiteSpace(data.CurrentTitle)) bits.Add(data.CurrentTitle);
        if (!string.IsNullOrWhiteSpace(data.CurrentEmployerName)) bits.Add("@ " + data.CurrentEmployerName);
        PersonPreviewMeta.Text = bits.Count == 0 ? "Unaffiliated" : string.Join("    ", bits);
        PersonPreview.Visibility = Visibility.Visible;
        UpdateGenerateButtonsEnabled();
    }

    private void ProvinceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var province = SelectedText(ProvinceCombo);
        _selectedProvince = string.IsNullOrWhiteSpace(province) ? null : province;
        _selectedCity = null;

        CityCombo.ItemsSource = null;
        CityCombo.SelectedIndex = -1;
        CityCombo.IsEnabled = false;
        RegionPreview.Visibility = Visibility.Collapsed;

        if (_selectedProvince is not null
            && CityByProvince.TryGetValue(_selectedProvince, out var cities))
        {
            CityCombo.ItemsSource = cities;
            CityCombo.IsEnabled = true;
        }

        UpdateGenerateButtonsEnabled();
    }

    private void CityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var city = SelectedText(CityCombo);
        _selectedCity = string.Equals(city, "(All cities)", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(city)
            ? null
            : city;

        if (_selectedProvince is not null)
        {
            RegionPreviewText.Text = $"Region brief for {_selectedProvince}" +
                (_selectedCity is not null ? $" / {_selectedCity}" : "");
            RegionPreview.Visibility = Visibility.Visible;
        }
        else
        {
            RegionPreview.Visibility = Visibility.Collapsed;
        }

        UpdateGenerateButtonsEnabled();
    }

    private void SectorProvinceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var province = SelectedText(SectorProvinceCombo);
        _sectorProvince = string.IsNullOrWhiteSpace(province) ? null : province;
        _sectorCity = null;

        SectorCityCombo.ItemsSource = null;
        SectorCityCombo.SelectedIndex = -1;
        SectorCityCombo.IsEnabled = false;

        if (_sectorProvince is not null && CityByProvince.TryGetValue(_sectorProvince, out var cities))
        {
            SectorCityCombo.ItemsSource = cities;
            SectorCityCombo.IsEnabled = true;
        }

        RefreshSectorPreview();
        UpdateGenerateButtonsEnabled();
    }

    private void SectorCityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var city = SelectedText(SectorCityCombo);
        _sectorCity = string.Equals(city, "(All cities)", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(city)
                ? null
                : city;
        RefreshSectorPreview();
        UpdateGenerateButtonsEnabled();
    }

    private void SectorBucketChanged(object sender, RoutedEventArgs e)
    {
        ToggleHashSet(sender, _sectorBuckets);
        RefreshSectorPreview();
        UpdateGenerateButtonsEnabled();
    }

    private void SectorStructuralChanged(object sender, RoutedEventArgs e)
    {
        ToggleHashSet(sender, _sectorStructuralTypes);
        RefreshSectorPreview();
        UpdateGenerateButtonsEnabled();
    }

    private void SectorIndigenousToggleChanged(object sender, RoutedEventArgs e)
    {
        SectorNationBox.IsEnabled = SectorIndigenousToggle.IsChecked == true;
        if (SectorIndigenousToggle.IsChecked != true)
        {
            SectorNationBox.Text = string.Empty;
        }
        RefreshSectorPreview();
        UpdateGenerateButtonsEnabled();
    }

    private void SectorFilterTextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshSectorPreview();
        UpdateGenerateButtonsEnabled();
    }

    private static void ToggleHashSet(object sender, HashSet<string> set)
    {
        if (sender is CheckBox cb && cb.Tag is string tag && !string.IsNullOrEmpty(tag))
        {
            if (cb.IsChecked == true) set.Add(tag);
            else set.Remove(tag);
        }
    }

    private void RefreshSectorPreview()
    {
        if (_sectorProvince is null)
        {
            SectorPreview.Visibility = Visibility.Collapsed;
            return;
        }

        var geo = _sectorCity is null ? _sectorProvince : $"{_sectorCity}, {_sectorProvince}";
        var bits = new List<string> { geo };
        if (_sectorBuckets.Count > 0) bits.Add(string.Join(" + ", _sectorBuckets));
        if (_sectorStructuralTypes.Count > 0) bits.Add(string.Join(" + ", _sectorStructuralTypes));
        if (SectorIndigenousToggle.IsChecked == true)
        {
            var nation = SectorNationBox.Text?.Trim();
            bits.Add(string.IsNullOrWhiteSpace(nation) ? "Indigenous" : $"Indigenous: {nation}");
        }
        var kw = SectorExtraKeywordBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(kw)) bits.Add($"\"{kw}\"");

        SectorPreviewText.Text = "Sector brief  " + string.Join("    ", bits);
        SectorPreview.Visibility = Visibility.Visible;
    }

    private void TypeTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, TypeTabs))
        {
            return;
        }

        _activeTabIndex = TypeTabs.SelectedIndex;
        UpdateGenerateButtonsEnabled();
    }

    private void UpdateGenerateButtonsEnabled()
    {
        var ready =
            (TypeTabs.SelectedIndex == 0 && _selectedOrg is not null) ||
            (TypeTabs.SelectedIndex == 1 && _selectedProvince is not null) ||
            (TypeTabs.SelectedIndex == 2 && _selectedProject is not null) ||
            (TypeTabs.SelectedIndex == 3 && _selectedPerson is not null) ||
            (TypeTabs.SelectedIndex == 4 && _sectorProvince is not null);
        GeneratePdfButton.IsEnabled = ready;
        GenerateDocxButton.IsEnabled = ready;
    }

    private async Task GenerateAsync(bool asPdf)
    {
        GeneratePdfButton.IsEnabled = false;
        GenerateDocxButton.IsEnabled = false;

        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var ts = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var ext = asPdf ? "pdf" : "docx";
            string path;

            if (TypeTabs.SelectedIndex == 0 && _selectedOrg is { } org)
            {
                path = Path.Combine(desktop, $"KOR-Org-Brief-{org.Id}-{ts}.{ext}");
                var data = await _briefStore.GetOrgBriefAsync(org.Id, CancellationToken.None).ConfigureAwait(true);
                if (data is null)
                {
                    MessageBox.Show(
                        this,
                        "Organization not found.",
                        "Generate Brief",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (asPdf)
                {
                    await Task.Run(() => _pdfGen.WriteOrgBrief(data, path)).ConfigureAwait(true);
                }
                else
                {
                    await Task.Run(() => _wordGen.WriteOrgBrief(data, path)).ConfigureAwait(true);
                }
            }
            else if (TypeTabs.SelectedIndex == 1 && _selectedProvince is { } province)
            {
                var citySlug = _selectedCity?.Replace(' ', '-') ?? "all";
                path = Path.Combine(desktop, $"KOR-Region-Brief-{province}-{citySlug}-{ts}.{ext}");
                var data = await _briefStore.GetRegionBriefAsync(province, _selectedCity, CancellationToken.None)
                    .ConfigureAwait(true);

                if (asPdf)
                {
                    await Task.Run(() => _pdfGen.WriteRegionBrief(data, path)).ConfigureAwait(true);
                }
                else
                {
                    await Task.Run(() => _wordGen.WriteRegionBrief(data, path)).ConfigureAwait(true);
                }
            }
            else if (TypeTabs.SelectedIndex == 2 && _selectedProject is { } proj)
            {
                path = Path.Combine(desktop, $"KOR-Project-Brief-{_selectedProjectId ?? proj.Id}-{ts}.{ext}");
                if (asPdf)
                {
                    await Task.Run(() => _pdfGen.WriteProjectBrief(proj, path)).ConfigureAwait(true);
                }
                else
                {
                    await Task.Run(() => _wordGen.WriteProjectBrief(proj, path)).ConfigureAwait(true);
                }
            }
            else if (TypeTabs.SelectedIndex == 3 && _selectedPerson is { } person)
            {
                var safeName = new string(person.DisplayName
                    .Where(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_')
                    .ToArray());
                if (safeName.Length == 0)
                {
                    safeName = "person";
                }

                path = Path.Combine(desktop, $"KOR-Person-Brief-{_selectedPersonId ?? person.IntelPersonId}-{safeName}-{ts}.{ext}");
                if (asPdf)
                {
                    await Task.Run(() => _pdfGen.WritePersonBrief(person, path)).ConfigureAwait(true);
                }
                else
                {
                    await Task.Run(() => _wordGen.WritePersonBrief(person, path)).ConfigureAwait(true);
                }
            }
            else if (TypeTabs.SelectedIndex == 4 && _sectorProvince is { } sp)
            {
                var request = new SectorBriefRequest(
                    Province: sp,
                    City: _sectorCity,
                    SectorBuckets: _sectorBuckets.ToArray(),
                    StructuralTypes: _sectorStructuralTypes.ToArray(),
                    IndigenousOnly: SectorIndigenousToggle.IsChecked == true,
                    IndigenousNationFilter: string.IsNullOrWhiteSpace(SectorNationBox.Text)
                        ? null
                        : SectorNationBox.Text.Trim(),
                    ExtraKeyword: string.IsNullOrWhiteSpace(SectorExtraKeywordBox.Text)
                        ? null
                        : SectorExtraKeywordBox.Text.Trim());

                path = Path.Combine(desktop, $"KOR-Sector-Brief-{BuildSectorSlug(request)}-{ts}.{ext}");
                var data = await _briefStore.GetSectorBriefAsync(request, CancellationToken.None)
                    .ConfigureAwait(true);

                if (asPdf)
                {
                    await Task.Run(() => _pdfGen.WriteSectorBrief(data, path)).ConfigureAwait(true);
                }
                else
                {
                    await Task.Run(() => _wordGen.WriteSectorBrief(data, path)).ConfigureAwait(true);
                }
            }
            else
            {
                return;
            }

            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(path)
                {
                    UseShellExecute = true,
                });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                "Brief generation failed: " + ex.Message,
                "Generate Brief",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            UpdateGenerateButtonsEnabled();
        }
    }

    private async void GeneratePdf_Click(object sender, RoutedEventArgs e)
        => await GenerateAsync(asPdf: true).ConfigureAwait(true);

    private async void GenerateDocx_Click(object sender, RoutedEventArgs e)
        => await GenerateAsync(asPdf: false).ConfigureAwait(true);

    private static string? SelectedText(ComboBox combo)
    {
        return combo.SelectedItem switch
        {
            ComboBoxItem item => item.Content?.ToString(),
            string value => value,
            _ => null,
        };
    }

    private static string BuildSectorSlug(SectorBriefRequest r)
    {
        var parts = new List<string> { r.Province };
        if (!string.IsNullOrWhiteSpace(r.City)) parts.Add(r.City);
        if (r.SectorBuckets.Count > 0) parts.AddRange(r.SectorBuckets);
        if (r.StructuralTypes.Count > 0) parts.AddRange(r.StructuralTypes);
        if (r.IndigenousOnly) parts.Add("Indigenous");
        var raw = string.Join("-", parts);
        var safe = new string(raw.Where(ch => char.IsLetterOrDigit(ch) || ch == '-').ToArray());
        return safe.Length == 0 ? "sector" : safe;
    }
}

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

public partial class BriefsMakerWindow : Window
{
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
    private string? _selectedProvince;
    private string? _selectedCity;
    private ProjectBriefData? _selectedProject;
    private long? _selectedProjectId;
    private PersonBriefData? _selectedPerson;
    private long? _selectedPersonId;

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
    }

    private async void OrgPicker_OrgSelected(object? sender, long orgId)
    {
        var row = await _canonicalOrgStore.GetCanonicalOrgAsync(orgId, CancellationToken.None).ConfigureAwait(true);
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
        var data = await _briefStore.GetProjectBriefAsync(id, CancellationToken.None)
            .ConfigureAwait(true);
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
        var data = await _briefStore.GetPersonBriefAsync(id, CancellationToken.None)
            .ConfigureAwait(true);
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

    private void TypeTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, TypeTabs))
        {
            return;
        }

        UpdateGenerateButtonsEnabled();
    }

    private void UpdateGenerateButtonsEnabled()
    {
        var ready =
            (TypeTabs.SelectedIndex == 0 && _selectedOrg is not null) ||
            (TypeTabs.SelectedIndex == 1 && _selectedProvince is not null) ||
            (TypeTabs.SelectedIndex == 2 && _selectedProject is not null) ||
            (TypeTabs.SelectedIndex == 3 && _selectedPerson is not null);
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
}

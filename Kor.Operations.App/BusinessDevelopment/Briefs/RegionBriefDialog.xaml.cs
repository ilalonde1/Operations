#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.Services;

namespace Kor.Operations.App.BusinessDevelopment.Briefs;

/// <summary>
/// Province + optional-city picker that drives the Region Brief generator.
/// Saves a .docx to the user's Desktop and opens it.
/// </summary>
public partial class RegionBriefDialog : Window
{
    public RegionBriefDialog()
    {
        InitializeComponent();
    }

    private async void GenerateButton_Click(object sender, RoutedEventArgs e)
    {
        var province = (ProvinceCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (string.IsNullOrWhiteSpace(province))
        {
            StatusText.Text = "Select a province / state.";
            return;
        }

        var city = string.IsNullOrWhiteSpace(CityBox.Text) ? null : CityBox.Text.Trim();

        GenerateButton.IsEnabled = false;
        StatusText.Text = "Generating region brief…";
        try
        {
            var briefStore = AppServices.Get<Kor.Opportunities.Data.Briefs.IBriefDataStore>();
            var generator = AppServices.Get<IBriefGenerator>();
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var citySuffix = string.IsNullOrEmpty(city) ? string.Empty : "-" + SanitizeForFilename(city);
            var path = System.IO.Path.Combine(
                desktop,
                $"KOR-Region-Brief-{province}{citySuffix}-{DateTime.Now:yyyyMMdd-HHmmss}.docx");

            var data = await briefStore.GetRegionBriefAsync(province, city, CancellationToken.None).ConfigureAwait(true);
            await Task.Run(() => generator.WriteRegionBrief(data, path)).ConfigureAwait(true);

            StatusText.Text = "Saved: " + path;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText.Text = "Failed: " + ex.Message;
        }
        finally
        {
            GenerateButton.IsEnabled = true;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static string SanitizeForFilename(string value)
    {
        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c.ToString(), string.Empty);
        }
        return value.Trim();
    }
}

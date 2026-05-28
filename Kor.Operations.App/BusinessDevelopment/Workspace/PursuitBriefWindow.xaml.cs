#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Navigation;
using Kor.Operations.Services;
using Microsoft.Win32;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

public partial class PursuitBriefWindow : Window
{
    public PursuitBriefWindow(PursuitBriefViewModel vm)
    {
        InitializeComponent();
        DataContext = vm ?? throw new ArgumentNullException(nameof(vm));
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await HeaderLoader.ApplyAsync(HeaderBar).ConfigureAwait(true);
        }
        catch
        {
            // Header identity is cosmetic and should not block loading the brief.
        }
    }

    private void OnHyperlinkRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        if (e.Uri is null || !e.Uri.IsAbsoluteUri)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.ToString(),
                UseShellExecute = true,
            });
            e.Handled = true;
        }
        catch (Exception ex)
        {
            if (DataContext is PursuitBriefViewModel vm)
            {
                vm.StatusMessage = $"Open source failed: {ex.GetType().Name}: {ex.Message}";
            }
        }
    }

    private async void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PursuitBriefViewModel vm || vm.Brief is null)
        {
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title = "Export Pursuit Brief",
            Filter = "PDF Document|*.pdf",
            FileName = $"PursuitBrief_{SanitizeFileName(vm.Brief.Project.ProjectName)}.pdf",
            AddExtension = true,
            OverwritePrompt = true,
        };

        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        var snapshot = new PursuitBriefPdfSnapshot(
            vm.Brief,
            vm.KorEdgeDisplay,
            vm.OwnerContactsDisplay,
            vm.OwnerBdRecordDisplay,
            vm.ArchitectWarmthDisplay,
            vm.ThePlayDisplay,
            vm.FitScoreDisplay);

        try
        {
            var path = dlg.FileName;
            await Task.Run(() => PursuitBriefPdfExporter.Export(snapshot, path)).ConfigureAwait(true);
            MessageBox.Show(this, "PDF export completed.", "Pursuit Brief", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"PDF export failed:\n{ex.Message}", "Pursuit Brief", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string SanitizeFileName(string? value)
    {
        var name = string.IsNullOrWhiteSpace(value) ? "PursuitBrief" : value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        name = WhitespaceRegex().Replace(name, "_");
        return name.Length > 80 ? name[..80] : name;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}

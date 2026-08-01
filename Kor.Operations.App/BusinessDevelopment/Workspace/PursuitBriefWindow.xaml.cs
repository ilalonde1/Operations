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
    private readonly PursuitBriefViewModel _vm;
    private bool _aiRegistered;

    public PursuitBriefWindow(PursuitBriefViewModel vm)
    {
        InitializeComponent();
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        DataContext = _vm;
    }

    // ---- Pursuit lifecycle (migration 284) — the weekly attack sheet's
    // kor://mpi deep links land here, so Own it / Not for us live here too. ----

    private async void OwnPlay_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.LoadedMpiId <= 0)
        {
            return;
        }

        var actor = ResolveActor();
        var name = _vm.Brief?.Project.ProjectName ?? $"project #{_vm.LoadedMpiId}";
        var confirm = MessageBox.Show(this,
            $"Own “{name}” as {actor}?\n\nIt leaves the shared boards and the weekly attack sheet. Convert it to a pursuit within 14 days or it returns to the pool (your morning digest will warn you first).",
            "Own this play", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK)
        {
            return;
        }

        await RunLifecycleAsync(
            store => store.OwnProjectAsync(_vm.LoadedMpiId, actor, System.Threading.CancellationToken.None),
            $"Owned — “{name}” is yours now.",
            "Someone already owns or removed this play — it has left the pool.").ConfigureAwait(true);
    }

    private async void DismissPlay_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.LoadedMpiId <= 0)
        {
            return;
        }

        var name = _vm.Brief?.Project.ProjectName ?? $"project #{_vm.LoadedMpiId}";
        var dialog = new DismissReasonDialog(name) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var actor = ResolveActor();
        await RunLifecycleAsync(
            store => store.DismissProjectAsync(_vm.LoadedMpiId, actor, dialog.Reason, System.Threading.CancellationToken.None),
            $"Removed — “{name}” is out of the actionable pool (restorable by admins).",
            "This play was already removed or retired.").ConfigureAwait(true);
    }

    private async Task RunLifecycleAsync(
        Func<Kor.Opportunities.Data.MajorProjects.IPursuitLifecycleStore, Task<Kor.Opportunities.Data.MajorProjects.LifecycleOutcome>> action,
        string appliedMessage,
        string conflictMessage)
    {
        try
        {
            var store = AppServices.Get<Kor.Opportunities.Data.MajorProjects.IPursuitLifecycleStore>();
            var outcome = await action(store).ConfigureAwait(true);
            _vm.StatusMessage = outcome == Kor.Opportunities.Data.MajorProjects.LifecycleOutcome.Applied
                ? appliedMessage
                : conflictMessage;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Action failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string ResolveActor()
    {
        if (!string.IsNullOrWhiteSpace(global::Kor.Operations.OperationsApp.SignedInUserUpn))
        {
            return global::Kor.Operations.OperationsApp.SignedInUserUpn.Trim();
        }

        var overrideUpn = AppServices.Get<Kor.Operations.App.Options.UserOptions>().UserUpnOverride;
        if (!string.IsNullOrWhiteSpace(overrideUpn))
        {
            return overrideUpn.Trim();
        }

        return Environment.UserName;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // T5.001 made the VM an IAiContextProvider but nothing ever
        // registered it, so the AI never saw an open Pursuit Brief
        // (BD-Audit-2026-06-09 M16).
        if (!_aiRegistered)
        {
            AppServices.Get<AppAiContextBuilder>().Register(_vm);
            _aiRegistered = true;
        }

        try
        {
            await HeaderLoader.ApplyAsync(HeaderBar).ConfigureAwait(true);
        }
        catch
        {
            // Header identity is cosmetic and should not block loading the brief.
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_aiRegistered)
        {
            AppServices.Get<AppAiContextBuilder>().Unregister(_vm);
            _aiRegistered = false;
        }

        base.OnClosed(e);
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

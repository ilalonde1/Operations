#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Kor.Operations.App.Crm;
using Kor.Opportunities.Data.Crm;
using Microsoft.Win32;
using Serilog;

namespace Kor.Operations.App.BusinessDevelopment.Director;

/// <summary>One line of the Documents window.</summary>
public sealed class PursuitDocumentRow(PursuitFile file)
{
    public PursuitFile File { get; } = file;
    public string Name => File.FileName;
    public string Detail => (File.SizeBytes is { } bytes ? Size(bytes) + "  ·  " : "")
        + "added " + File.UploadedAtUtc.ToLocalTime().ToString("ddd d MMM", CultureInfo.InvariantCulture);

    internal static string Size(long bytes) => bytes >= 1024 * 1024
        ? (bytes / 1024d / 1024d).ToString("0.#", CultureInfo.InvariantCulture) + " MB"
        : Math.Max(1, bytes / 1024).ToString(CultureInfo.InvariantCulture) + " KB";
}

/// <summary>A pursuit's files, reached from its card: open any one, or save them all to a folder.</summary>
public partial class PursuitDocumentsWindow : Window
{
    private readonly long _engagementId;
    private readonly IPursuitFileStore _store;
    private readonly IPursuitFileOpener _opener;
    private readonly CancellationTokenSource _closing = new();
    private IReadOnlyList<PursuitFile> _files = Array.Empty<PursuitFile>();

    public PursuitDocumentsWindow(long engagementId, string project, IPursuitFileStore store, IPursuitFileOpener opener)
    {
        InitializeComponent();
        (_engagementId, _store, _opener) = (engagementId, store, opener);
        Heading.Text = project;
        Closed += (_, _) => { _closing.Cancel(); _closing.Dispose(); };
        Loaded += async (_, _) => await LoadAsync().ConfigureAwait(true);
    }

    private async Task LoadAsync()
    {
        try
        {
            _files = (await _store.ListByEngagementAsync(_engagementId, _closing.Token).ConfigureAwait(true))
                .OrderByDescending(f => f.UploadedAtUtc).ThenBy(f => f.FileName, StringComparer.OrdinalIgnoreCase).ToList();
            Files.ItemsSource = _files.Select(f => new PursuitDocumentRow(f)).ToList();
            Byline.Text = $"{_files.Count} document{(_files.Count == 1 ? "" : "s")}, newest first";
            SaveAllButton.IsEnabled = _files.Count > 0;
        }
        catch (OperationCanceledException) { /* The window closed while it loaded. */ }
        catch (Exception ex)
        {
            Log.Warning(ex, "Documents: listing pursuit {Id} failed", _engagementId);
            Byline.Text = "The documents could not be read. Close this and try again.";
            SaveAllButton.IsEnabled = false;
        }
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PursuitDocumentRow row }) return;
        Status.Text = $"Opening {row.Name}…";
        try
        {
            await _opener.OpenAsync(row.File, _closing.Token).ConfigureAwait(true);
            Status.Text = "";
        }
        catch (OperationCanceledException) { /* The window closed while it opened. */ }
        catch (Exception ex)
        {
            Log.Warning(ex, "Documents: opening file {Id} failed", row.File.Id);
            Status.Text = $"{row.Name} could not be opened: {ex.Message}";
        }
    }

    private async void SaveAll_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Save all the documents to this folder" };
        if (dialog.ShowDialog(this) != true) return;
        SaveAllButton.IsEnabled = false;
        try
        {
            var progress = new Progress<string>(name => Status.Text = $"Saving {name}…");
            var result = await PursuitDocumentSaver.SaveAllAsync(_store, _files, dialog.FolderName, progress, _closing.Token).ConfigureAwait(true);
            Status.Text = $"{result.Saved} saved"
                + (result.AlreadyThere > 0 ? $", {result.AlreadyThere} already in the folder" : "")
                + (result.NotSaved.Count > 0 ? $". Not saved, open them instead: {string.Join(", ", result.NotSaved)}" : ".");
        }
        catch (OperationCanceledException) { /* The window closed mid-save; the file in progress was removed. */ }
        catch (Exception ex)
        {
            Log.Warning(ex, "Documents: saving pursuit {Id} failed", _engagementId);
            Status.Text = "Saving stopped: " + ex.Message;
        }
        finally
        {
            if (IsLoaded) SaveAllButton.IsEnabled = true;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

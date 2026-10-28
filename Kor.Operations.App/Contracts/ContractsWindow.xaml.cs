#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Serilog;

namespace Kor.Operations.App.Contracts;

/// <summary>Home → Contracts. The Positions screen; "Check a contract" is the next screen in this window.</summary>
public partial class ContractsWindow : Window
{
    private readonly ContractPositionsViewModel _positions;

    public ContractsWindow(ContractPositionsViewModel positions)
    {
        _positions = positions ?? throw new ArgumentNullException(nameof(positions));
        InitializeComponent();
        DataContext = _positions;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await _positions.LoadClientsAsync();
        await _positions.SearchAsync();
    }

    private async void Search_Click(object sender, RoutedEventArgs e) => await _positions.SearchAsync();

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await _positions.SearchAsync();
    }

    private void OpenSource_Click(object sender, RoutedEventArgs e)
    {
        var row = _positions.SelectedRow;
        if (row is null) return;
        try
        {
            var path = row.SourcePath;
            var folder = Path.GetDirectoryName(path) ?? path;
            // Select the file when it is a loose file; an email attachment's "file" is the .msg/.eml, which is still the right thing to open.
            if (File.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Opening the contract source folder failed for {Path}", row.SourcePath);
            MessageBox.Show(this, $"Could not open the folder on KOR-FS01:\n{row.SourcePath}\n\n{ex.Message}", "Contracts", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CopyClause_Click(object sender, RoutedEventArgs e)
    {
        var row = _positions.SelectedRow;
        if (row is null) return;
        try
        {
            Clipboard.SetText($"{row.Client} · {row.Job} · {row.Year} · {row.Status} · §{row.Section}\n{row.SourcePath}\n\n{row.Text}");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Copying the clause to the clipboard failed.");
        }
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using Kor.Operations.EngineeringTools.Dxf;

namespace Kor.Operations.EngineeringTools.DxfToEtabs
{
    /// <summary>
    /// Builds an ETABS model from a folder of plan DXFs, in the app rather than at a command line.
    ///
    /// The generator has run for two jobs as a CLI driven by whoever was sitting at it, and the
    /// scope of a run was a flag typed by hand. That is how a site model reached an engineer who
    /// had asked for one building of it: the towers' storeys sit BELOW the mid-rise's roof and
    /// carry no prefix, so the cut that was typed could not express what she wanted and nothing
    /// in the run said so. Here the storeys are a list she ticks, read from her own model, and
    /// what is unticked is neither built nor read.
    ///
    /// All of the work lives in EngineeringTools.Core; this window is intake, scope and output.
    /// </summary>
    public partial class DxfToEtabsWindow : Window
    {
        /// <summary>A storey of the reference model, and whether this run builds it.</summary>
        public sealed class StoreyChoice : INotifyPropertyChanged
        {
            private bool _build = true;

            public StoreyChoice(string name, double elevation)
            {
                Name = name;
                Elevation = elevation;
            }

            public string Name { get; }
            public double Elevation { get; }

            /// <summary>Elevation shown alongside the name: a site model repeats level numbers per building.</summary>
            public string Label => $"{Name}   ({Elevation / 12.0:0.0} ft)";

            public bool Build
            {
                get => _build;
                set
                {
                    if (_build == value) return;
                    _build = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Build)));
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
        }

        private readonly ObservableCollection<StoreyChoice> _storeys = new();
        private string? _lastOutputFolder;

        public DxfToEtabsWindow()
        {
            InitializeComponent();
            StoreyList.ItemsSource = _storeys;
            _storeys.CollectionChanged += (_, _) => UpdateStoreyCount();
            UpdateStoreyCount();
        }

        private void UpdateStoreyCount()
            => StoreyCountText.Text = _storeys.Count == 0
                ? "load a reference model"
                : $"{_storeys.Count(s => s.Build)} of {_storeys.Count}";

        // ===================== intake =====================

        private void BrowseDxf_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "Select the folder holding the plan DXFs" };
            if (!string.IsNullOrWhiteSpace(DxfFolderBox.Text) && Directory.Exists(DxfFolderBox.Text))
                dlg.InitialDirectory = DxfFolderBox.Text;
            if (dlg.ShowDialog(this) != true) return;

            DxfFolderBox.Text = dlg.FolderName;
            int sheets = Directory.EnumerateFiles(dlg.FolderName, "*.dxf", SearchOption.TopDirectoryOnly).Count();
            StatusText.Text = $"{sheets} drawing(s) in that folder.";

            // The reference and the output usually sit a level up from the drawings, which is where
            // both jobs keep them. Offered, never assumed: the boxes stay editable.
            if (string.IsNullOrWhiteSpace(ReferenceBox.Text)) SuggestReference(dlg.FolderName);
        }

        private void SuggestReference(string dxfFolder)
        {
            try
            {
                var parent = Directory.GetParent(dxfFolder)?.FullName;
                if (parent is null) return;

                // Never one of ours. Tool output carries KOR-prefixed object names and a model
                // round-tripped through ETABS keeps them, which is exactly how a generated model
                // was once mistaken for an engineer's own work.
                var candidate = Directory.EnumerateFiles(parent, "*.e2k", SearchOption.TopDirectoryOnly)
                    .Concat(Directory.EnumerateFiles(parent, "*.$et", SearchOption.TopDirectoryOnly))
                    .Where(f => !Path.GetFileName(f).Contains("FROM-DRAWINGS", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => Path.GetFileNameWithoutExtension(f)
                        .Contains("reference", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();

                if (candidate is not null) LoadReference(candidate);
            }
            catch (IOException) { /* a suggestion that cannot be made is not an error */ }
            catch (UnauthorizedAccessException) { }
        }

        private void BrowseReference_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Select the model ETABS exported for this job",
                Filter = "ETABS model (*.e2k;*.$et)|*.e2k;*.$et|All files (*.*)|*.*",
            };
            if (ofd.ShowDialog(this) != true) return;
            LoadReference(ofd.FileName);
        }

        private void BrowseStickFile_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Select the structural stick file PDF",
                Filter = "PDF drawing set (*.pdf)|*.pdf|All files (*.*)|*.*",
            };
            if (ofd.ShowDialog(this) == true) StickFileBox.Text = ofd.FileName;
        }

        private void LoadReference(string path)
        {
            try
            {
                var doc = E2kDocument.Load(path);
                var stories = doc.ReadStories().OrderByDescending(s => s.Elevation).ToList();

                _storeys.Clear();
                foreach (var s in stories) _storeys.Add(new StoreyChoice(s.Name, s.Elevation));
                foreach (var s in _storeys) s.PropertyChanged += (_, _) => UpdateStoreyCount();

                ReferenceBox.Text = path;
                UpdateStoreyCount();

                if (string.IsNullOrWhiteSpace(OutputBox.Text))
                {
                    var folder = Path.GetDirectoryName(path);
                    if (folder is not null)
                        OutputBox.Text = Path.Combine(folder, "FROM-DRAWINGS.e2k");
                }

                StatusText.Text = $"{stories.Count} storeys in {Path.GetFileName(path)}. " +
                                  "Untick the ones this model should not contain.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not read that model: {ex.Message}",
                    "Drawings to ETABS Model", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog
            {
                Title = "Save the generated model as",
                Filter = "ETABS model (*.e2k)|*.e2k",
                FileName = string.IsNullOrWhiteSpace(OutputBox.Text)
                    ? "FROM-DRAWINGS.e2k"
                    : Path.GetFileName(OutputBox.Text),
            };
            if (sfd.ShowDialog(this) == true) OutputBox.Text = sfd.FileName;
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var s in _storeys) s.Build = true;
            UpdateStoreyCount();
        }

        private void SelectNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (var s in _storeys) s.Build = false;
            UpdateStoreyCount();
        }

        // ===================== the run =====================

        private async void Build_Click(object sender, RoutedEventArgs e)
        {
            string dxf = DxfFolderBox.Text.Trim();
            string reference = ReferenceBox.Text.Trim();
            string stickFile = StickFileBox.Text.Trim();
            string output = OutputBox.Text.Trim();

            if (!Directory.Exists(dxf)) { Complain("Choose the folder holding the plan DXFs."); return; }
            if (!File.Exists(reference)) { Complain("Choose the model ETABS exported for this job."); return; }
            if (!string.IsNullOrWhiteSpace(stickFile) && !File.Exists(stickFile)) { Complain("Choose a stick file PDF that exists, or leave it blank."); return; }
            if (string.IsNullOrWhiteSpace(output)) { Complain("Choose where to save the generated model."); return; }
            if (_storeys.Count == 0) { Complain("The reference model lists no storeys."); return; }
            if (_storeys.All(s => !s.Build)) { Complain("Tick at least one storey to build."); return; }

            // A missing rule stops a production run by design: there is no fallback value, and a
            // model built from built-in numbers is not a model built to the office's standard.
            string? rules = Environment.GetEnvironmentVariable("KOR_ENGINEERINGTOOLS_STANDARDSDB");
            if (string.IsNullOrWhiteSpace(rules))
            {
                Complain("KOR_ENGINEERINGTOOLS_STANDARDSDB is not set on this machine, so the rules " +
                         "in KorStandards cannot be read. Building from built-in values would produce " +
                         "a model to nobody's standard, so this run is refused.");
                return;
            }

            var drop = _storeys.Where(s => !s.Build).Select(s => s.Name).ToList();

            SetBusy(true, drop.Count == 0
                ? "Reading drawings…"
                : $"Reading drawings — leaving out {drop.Count} storey(s)…");

            try
            {
                var request = new DxfToEtabsRequest
                {
                    DxfFolder = dxf,
                    ReferenceE2k = reference,
                    StickFilePdf = string.IsNullOrWhiteSpace(stickFile) ? null : stickFile,
                    OutputE2k = output,
                    DropStoreys = drop,
                    RuleSettingsConnection = rules,
                };

                var report = await Task.Run(() => DxfToEtabsService.Run(request));

                ReportBox.Text = DxfToEtabsService.FormatReport(report);
                FlagList.ItemsSource = report.Warnings
                    .Concat(report.Sheets.SelectMany(s => s.Flags.Select(f => $"{s.File}: {f}")))
                    .ToList();
                RulesGrid.ItemsSource = report.RulesApplied.Values.OrderBy(r => r.Key, StringComparer.Ordinal).ToList();

                _lastOutputFolder = Path.GetDirectoryName(output);
                OpenFolderButton.IsEnabled = _lastOutputFolder is not null && Directory.Exists(_lastOutputFolder);

                int flags = ((IEnumerable<string>)FlagList.ItemsSource).Count();
                StatusText.Text =
                    $"{report.StoriesPopulated} storeys · {report.Summary.Walls} walls · " +
                    $"{report.Summary.Columns} columns · {report.Summary.Floors} floors · " +
                    $"{report.SheetsPlaced} of {report.SheetsRead} drawings placed · {flags} to look at.";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Nothing was written.";
                MessageBox.Show(ex.Message, "Drawings to ETABS Model",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (_lastOutputFolder is null || !Directory.Exists(_lastOutputFolder)) return;
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_lastOutputFolder}\"") { UseShellExecute = true });
        }

        private void SetBusy(bool busy, string? message)
        {
            BuildButton.IsEnabled = !busy;
            BrowseDxfButton.IsEnabled = !busy;
            BrowseReferenceButton.IsEnabled = !busy;
            BrowseStickFileButton.IsEnabled = !busy;
            BrowseOutputButton.IsEnabled = !busy;
            AllButton.IsEnabled = !busy;
            NoneButton.IsEnabled = !busy;
            StoreyList.IsEnabled = !busy;
            Cursor = busy ? System.Windows.Input.Cursors.Wait : null;
            if (message is not null) StatusText.Text = message;
        }

        private void Complain(string message)
            => MessageBox.Show(message, "Drawings to ETABS Model", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}

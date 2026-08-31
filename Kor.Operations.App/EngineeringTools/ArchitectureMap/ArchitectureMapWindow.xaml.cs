#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Kor.Operations.Architecture;

namespace Kor.Operations.EngineeringTools.ArchitectureMap
{
    /// <summary>
    /// THE MAP, AS A FEATURE OF THE APP.
    ///
    /// It was a console exe with a PowerShell launcher and a JSON file on disk beside it, which is
    /// not the same thing as being in the application however many C# lines it ran to. There is no
    /// script now and no intermediate file: the model is extracted into memory and handed straight
    /// to the renderer, so there is nothing to leave lying around and nothing that can be stale.
    ///
    /// The work happens off the UI thread because reading a repository of this size takes the better
    /// part of a minute and drawing ten pages over COM takes another half.
    /// </summary>
    public partial class ArchitectureMapWindow : Window
    {
        public ArchitectureMapWindow()
        {
            InitializeComponent();

            SourceFolderBox.Text = GuessCodebase();
            OutputFolderBox.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "KOR Application Map");
        }

        private string? _lastDrawn;

        /// <summary>The repository this app was built from, if it is still where it was built.
        /// A guess the user can overwrite beats an empty box.</summary>
        private static string GuessCodebase()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) return dir.FullName;
                dir = dir.Parent;
            }
            return string.Empty;
        }

        private void BrowseSource_Click(object sender, RoutedEventArgs e)
            => PickFolder(SourceFolderBox, "Choose the codebase to map");

        private void BrowseOutput_Click(object sender, RoutedEventArgs e)
            => PickFolder(OutputFolderBox, "Choose where to save the map");

        private static void PickFolder(System.Windows.Controls.TextBox target, string title)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = title };
            if (Directory.Exists(target.Text)) dialog.InitialDirectory = target.Text;
            if (dialog.ShowDialog() == true) target.Text = dialog.FolderName;
        }

        private async void Draw_Click(object sender, RoutedEventArgs e)
        {
            string source = SourceFolderBox.Text.Trim();
            string output = OutputFolderBox.Text.Trim();

            if (!Directory.Exists(source))
            {
                Say($"There is no folder at {source}.");
                return;
            }
            if (output.Length == 0)
            {
                Say("Choose somewhere to save the map.");
                return;
            }

            DrawButton.IsEnabled = false;
            OpenButton.IsEnabled = false;
            StatusText.Text = "reading the source…";
            Say($"Reading {source}");

            try
            {
                var result = await Task.Run(() =>
                {
                    var model = Extractor.Extract(source);
                    return (Model: model, Render: VisioRenderer.Render(model, output));
                }).ConfigureAwait(true);

                var m = result.Model;
                Say($"{m.Projects.Count} projects · {m.Types.Count:N0} types · {m.Stats.Lines:N0} lines · " +
                    $"{m.Verbs.Count} CLI verbs · {m.Scripts.Count} scripts · {m.Cycles.Count} dependency cycles");
                foreach (string note in result.Render.Notes) Say("  " + note);

                _lastDrawn = result.Render.VsdxPath;
                Say($"Wrote {_lastDrawn}");
                StatusText.Text = $"{result.Render.PngPaths.Count} pages";
                OpenButton.IsEnabled = true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException
                                          or System.Runtime.InteropServices.COMException)
            {
                // Visio missing, a locked output file, a folder that cannot be written — all things
                // a person can fix, so say which rather than falling over.
                Say("Could not draw the map: " + ex.Message);
                StatusText.Text = "not drawn";
            }
            finally
            {
                DrawButton.IsEnabled = true;
            }
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            if (_lastDrawn is null || !File.Exists(_lastDrawn)) return;
            Process.Start(new ProcessStartInfo(_lastDrawn) { UseShellExecute = true });
        }

        private void Say(string line)
        {
            LogText.Text = LogText.Text.StartsWith("Choose a codebase", StringComparison.Ordinal)
                ? line
                : LogText.Text + Environment.NewLine + line;
            LogScroller.ScrollToEnd();
        }
    }
}

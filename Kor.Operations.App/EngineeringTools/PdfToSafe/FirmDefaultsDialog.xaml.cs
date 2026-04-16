#nullable enable
using System;
using System.Globalization;
using System.Windows;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    /// <summary>
    /// Modal editor for <see cref="FirmDefaults"/>. Callers pass the current
    /// instance; on Save the dialog mutates the caller's instance in-place
    /// and persists it via <see cref="FirmDefaults.Save"/>. Cancel discards.
    /// </summary>
    internal partial class FirmDefaultsDialog : Window
    {
        private static readonly (string Key, string Label)[] DesignCodeOptions =
        {
            ("None",            "None (no design prefs)"),
            ("CSA_A23_3_19",    "CSA A23.3-19 (Canada)"),
            ("ACI_318_19",      "ACI 318-19 (USA)"),
            ("AS_3600_09",      "AS 3600-09 (Australia)"),
            ("NZS_3101_06",     "NZS 3101-06 (New Zealand)"),
            ("EC2_2004",        "Eurocode 2-2004 (Europe)"),
        };

        private static readonly (string Key, string Label)[] LoadCombOptions =
        {
            ("",        "(none)"),
            ("NBC",     "NBC (Canada)"),
            ("ASCE7",   "ASCE 7 (USA)"),
            ("EC0",     "Eurocode 0"),
            ("AS/NZS",  "AS/NZS 1170"),
        };

        private readonly FirmDefaults _defaults;

        public FirmDefaultsDialog(FirmDefaults defaults)
        {
            InitializeComponent();
            _defaults = defaults ?? throw new ArgumentNullException(nameof(defaults));

            // Populate fixed-list controls.
            foreach (var grade in StructuralMaterialDatabase.SupportedGrades)
                GradeCombo.Items.Add(grade);
            foreach (var (_, label) in DesignCodeOptions)
                DesignCodeCombo.Items.Add(label);
            foreach (var (_, label) in LoadCombOptions)
                LoadCombCombo.Items.Add(label);

            PopulateFromModel();
            Loaded += (_, _) => _ = RefreshSafeCompatibilityAsync();
        }

        private bool IsImperialSelected => ImperialRadio.IsChecked == true;

        private void UnitSystem_Changed(object sender, RoutedEventArgs e)
        {
            if (GradeCombo is null) return; // guard for InitializeComponent phase
            // Re-populate the grade dropdown with the appropriate set.
            var currentGrade = GradeCombo.SelectedItem as string;
            GradeCombo.Items.Clear();
            var grades = IsImperialSelected
                ? StructuralMaterialDatabase.SupportedImperialGrades
                : StructuralMaterialDatabase.SupportedMetricGrades;
            foreach (var g in grades) GradeCombo.Items.Add(g);
            // Try to keep the same selection; fall back to the first.
            if (currentGrade != null && GradeCombo.Items.Contains(currentGrade))
                GradeCombo.SelectedItem = currentGrade;
            else if (GradeCombo.Items.Count > 0)
                GradeCombo.SelectedIndex = IsImperialSelected ? 1 : 3; // 4000Psi or C30
        }

        /// <summary>
        /// Runs <see cref="SafeApiExporter.CheckAllInstalledCompatibility"/>
        /// off the UI thread (each probe loads SAFEv1.dll via reflection —
        /// fine but not instantaneous) and renders one row per installed
        /// version. "✅ SAFE 22" for compatible; "⚠ SAFE X — <issues>" for any
        /// mismatch. SAFE 2016 (32-bit) is excluded by the enumerator.
        /// </summary>
        /// <summary>
        /// Scans for all installed CSI products (SAFE, ETABS, SAP2000) off the
        /// UI thread and renders one row per install. Compatibility is checked
        /// for SAFE and ETABS (which have DLL-level preflight); SAP2000 installs
        /// are listed by presence only.
        /// </summary>
        private async System.Threading.Tasks.Task RefreshSafeCompatibilityAsync()
        {
            SafeCompatibilityList.ItemsSource = null;
            SafeCompatibilityEmpty.Text = "Scanning…";
            SafeCompatibilityEmpty.Visibility = Visibility.Visible;

            var lines = new System.Collections.Generic.List<string>();
            try
            {
                await System.Threading.Tasks.Task.Run(() =>
                {
                    // SAFE
                    foreach (var r in SafeApiExporter.CheckAllInstalledCompatibility())
                    {
                        string marker = r.IsCompatible ? "\u2705" : "\u26A0";
                        string pin    = r.Install.Version == SafeApiExporter.DefaultPreferredVersion ? "  (default)" : "";
                        string detail = r.IsCompatible ? "OK" : string.Join("; ", r.Issues);
                        lines.Add($"{marker}  {r.Install.FolderName,-14}  {detail}{pin}");
                    }
                    // ETABS
                    foreach (var r in EtabsApiExporter.CheckAllInstalledCompatibility())
                    {
                        string marker = r.IsCompatible ? "\u2705" : "\u26A0";
                        string pin    = r.Install.Version == EtabsApiExporter.DefaultPreferredVersion ? "  (default)" : "";
                        string detail = r.IsCompatible ? "OK" : string.Join("; ", r.Issues);
                        lines.Add($"{marker}  {r.Install.FolderName,-14}  {detail}{pin}");
                    }
                    // SAP2000 (no DLL-level compatibility check; list by presence)
                    foreach (var install in Sap2000ApiExporter.EnumerateSapInstalls())
                    {
                        string pin = install.Version == Sap2000ApiExporter.DefaultPreferredVersion ? "  (default)" : "";
                        lines.Add($"\u2705  {install.FolderName,-14}  found{pin}");
                    }
                }).ConfigureAwait(true);
            }
            catch (System.Exception ex)
            {
                SafeCompatibilityEmpty.Text = "Scan failed: " + ex.Message;
                return;
            }

            if (lines.Count == 0)
            {
                SafeCompatibilityEmpty.Text = "No 64-bit CSI installs found under Program Files\\Computers and Structures.";
                return;
            }

            SafeCompatibilityList.ItemsSource = lines;
            SafeCompatibilityEmpty.Visibility = Visibility.Collapsed;
        }

        private void PopulateFromModel() => PopulateFrom(_defaults);

        private void PopulateFrom(FirmDefaults source)
        {
            GradeCombo.SelectedItem = source.DefaultGradeCode;

            DesignCodeCombo.SelectedIndex = IndexOfKey(DesignCodeOptions, source.DefaultDesignCode);
            LoadCombCombo.SelectedIndex   = IndexOfKey(LoadCombOptions,   source.DefaultLoadCombCode);

            SlabThicknessBox.Text = FormatMm(source.DefaultSlabThicknessMm);
            WallDepthBox.Text     = FormatMm(source.DefaultWallDepthMm);
            MeshSizeBox.Text      = FormatMm(source.DefaultMeshSizeMm);
            SdlBox.Text           = FormatKpa(source.DefaultSdlKPa);
            LiveBox.Text          = FormatKpa(source.DefaultLiveKPa);
            MembraneModBox.Text   = FormatMod(source.DefaultSlabMembraneModifier);
            BendingModBox.Text    = FormatMod(source.DefaultSlabBendingModifier);
            ShearModBox.Text      = FormatMod(source.DefaultSlabShearModifier);
            SafeExePathBox.Text     = source.SafeExePath ?? string.Empty;
            EtabsExePathBox.Text    = source.EtabsExePath ?? string.Empty;
            Sap2000ExePathBox.Text  = source.Sap2000ExePath ?? string.Empty;

            bool isImp = string.Equals(source.UnitSystem, "Imperial", StringComparison.OrdinalIgnoreCase);
            ImperialRadio.IsChecked = isImp;
            MetricRadio.IsChecked   = !isImp;
            UnitSystem_Changed(this, new RoutedEventArgs()); // force grade list refresh

            ValidationText.Visibility = Visibility.Collapsed;
        }

        private static int IndexOfKey((string Key, string Label)[] table, string key)
        {
            for (int i = 0; i < table.Length; i++)
                if (string.Equals(table[i].Key, key, StringComparison.OrdinalIgnoreCase))
                    return i;
            return 0;
        }

        private static string FormatMm(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        private static string FormatKpa(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        private static string FormatMod(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            // Populate the UI from shipped defaults WITHOUT mutating the live
            // _defaults object. The live object is only updated when the user
            // clicks Save (TryReadInto). If the user cancels after reset, the
            // in-memory defaults remain unchanged.
            PopulateFrom(new FirmDefaults());
        }

        private void SafeExeBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select licensed SAFE.exe",
                Filter = "SAFE executable|SAFE.exe|All executables|*.exe",
                InitialDirectory = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Computers and Structures"),
            };
            if (dlg.ShowDialog(this) == true)
                SafeExePathBox.Text = dlg.FileName;
        }

        private void EtabsExeBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select licensed ETABS.exe",
                Filter = "ETABS executable|ETABS.exe|All executables|*.exe",
                InitialDirectory = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Computers and Structures"),
            };
            if (dlg.ShowDialog(this) == true)
                EtabsExePathBox.Text = dlg.FileName;
        }

        private void Sap2000ExeBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select licensed SAP2000.exe",
                Filter = "SAP2000 executable|SAP2000.exe|All executables|*.exe",
                InitialDirectory = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Computers and Structures"),
            };
            if (dlg.ShowDialog(this) == true)
                Sap2000ExePathBox.Text = dlg.FileName;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryReadInto(_defaults, out string? error))
            {
                ValidationText.Text = "Fix before saving — " + error;
                ValidationText.Visibility = Visibility.Visible;
                return;
            }

            if (!_defaults.Save())
            {
                ValidationText.Text = "Could not write the defaults file (check folder permissions).";
                ValidationText.Visibility = Visibility.Visible;
                return;
            }

            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Pulls every field from the UI into <paramref name="target"/>.
        /// Returns false with a human-readable reason if any field is
        /// invalid — the caller re-displays and the dialog stays open.
        /// </summary>
        private bool TryReadInto(FirmDefaults target, out string? error)
        {
            error = null;

            var grade = GradeCombo.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(grade))
            { error = "pick a concrete grade."; return false; }
            target.DefaultGradeCode = grade;

            int dcIdx = Math.Max(0, DesignCodeCombo.SelectedIndex);
            target.DefaultDesignCode = DesignCodeOptions[dcIdx].Key;

            int lcIdx = Math.Max(0, LoadCombCombo.SelectedIndex);
            target.DefaultLoadCombCode = LoadCombOptions[lcIdx].Key;

            if (!TryPositive(SlabThicknessBox.Text, "slab thickness", out var slabMm, ref error)) return false;
            target.DefaultSlabThicknessMm = slabMm;

            if (!TryPositive(WallDepthBox.Text, "wall depth", out var wallMm, ref error)) return false;
            target.DefaultWallDepthMm = wallMm;

            if (!TryPositive(MeshSizeBox.Text, "mesh size", out var meshMm, ref error)) return false;
            target.DefaultMeshSizeMm = meshMm;

            if (!TryNonNeg(SdlBox.Text,  "SDL",        out var sdl,  ref error)) return false;
            target.DefaultSdlKPa = sdl;

            if (!TryNonNeg(LiveBox.Text, "live load",  out var live, ref error)) return false;
            target.DefaultLiveKPa = live;

            if (!TryPositive(MembraneModBox.Text, "membrane modifier", out var mem, ref error)) return false;
            target.DefaultSlabMembraneModifier = mem;

            if (!TryPositive(BendingModBox.Text,  "bending modifier",  out var bend, ref error)) return false;
            target.DefaultSlabBendingModifier = bend;

            if (!TryPositive(ShearModBox.Text,    "shear modifier",    out var shr,  ref error)) return false;
            target.DefaultSlabShearModifier = shr;

            if (!TryValidateExePath(SafeExePathBox.Text, "SAFE.exe", "SAFE", ref error)) return false;
            target.SafeExePath = (SafeExePathBox.Text ?? string.Empty).Trim();

            if (!TryValidateExePath(EtabsExePathBox.Text, "ETABS.exe", "ETABS", ref error)) return false;
            target.EtabsExePath = (EtabsExePathBox.Text ?? string.Empty).Trim();

            if (!TryValidateExePath(Sap2000ExePathBox.Text, "SAP2000.exe", "SAP2000", ref error)) return false;
            target.Sap2000ExePath = (Sap2000ExePathBox.Text ?? string.Empty).Trim();

            target.UnitSystem = IsImperialSelected ? "Imperial" : "Metric";

            return true;
        }

        private static bool TryValidateExePath(string? text, string expectedFileName, string product, ref string? error)
        {
            string path = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(path)) return true; // blank = auto-detect, always OK
            if (!System.IO.File.Exists(path))
            {
                error = $"{product} path does not exist: {path}";
                return false;
            }
            if (!string.Equals(System.IO.Path.GetFileName(path), expectedFileName, StringComparison.OrdinalIgnoreCase))
            {
                error = $"{product} path should point to {expectedFileName}, got '{System.IO.Path.GetFileName(path)}'.";
                return false;
            }
            return true;
        }

        private static bool TryPositive(string text, string name, out double value, ref string? error)
        {
            if (!double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value) || value <= 0)
            {
                error = $"{name} must be a positive number.";
                return false;
            }
            return true;
        }

        private static bool TryNonNeg(string text, string name, out double value, ref string? error)
        {
            if (!double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value) || value < 0)
            {
                error = $"{name} cannot be negative.";
                return false;
            }
            return true;
        }
    }
}

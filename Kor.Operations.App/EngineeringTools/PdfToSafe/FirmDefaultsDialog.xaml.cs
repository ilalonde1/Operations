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
        private async System.Threading.Tasks.Task RefreshSafeCompatibilityAsync()
        {
            SafeCompatibilityList.ItemsSource = null;
            SafeCompatibilityEmpty.Text = "Scanning…";
            SafeCompatibilityEmpty.Visibility = Visibility.Visible;

            System.Collections.Generic.List<SafeApiExporter.CompatibilityReport> reports;
            try
            {
                reports = await System.Threading.Tasks.Task.Run(
                    SafeApiExporter.CheckAllInstalledCompatibility).ConfigureAwait(true);
            }
            catch (System.Exception ex)
            {
                SafeCompatibilityEmpty.Text = "Scan failed: " + ex.Message;
                return;
            }

            if (reports.Count == 0)
            {
                SafeCompatibilityEmpty.Text = "No 64-bit SAFE install found under Program Files\\Computers and Structures.";
                return;
            }

            var lines = new System.Collections.Generic.List<string>();
            int preferred = SafeApiExporter.DefaultPreferredVersion;
            foreach (var r in reports)
            {
                string marker = r.IsCompatible ? "\u2705" : "\u26A0";
                string pin    = r.Install.Version == preferred ? "  (default)" : "";
                string detail = r.IsCompatible ? "OK" : string.Join("; ", r.Issues);
                lines.Add($"{marker}  {r.Install.FolderName,-10}  {detail}{pin}");
            }
            SafeCompatibilityList.ItemsSource = lines;
            SafeCompatibilityEmpty.Visibility = Visibility.Collapsed;
        }

        private void PopulateFromModel()
        {
            GradeCombo.SelectedItem = _defaults.DefaultGradeCode;

            DesignCodeCombo.SelectedIndex = IndexOfKey(DesignCodeOptions, _defaults.DefaultDesignCode);
            LoadCombCombo.SelectedIndex   = IndexOfKey(LoadCombOptions,   _defaults.DefaultLoadCombCode);

            SlabThicknessBox.Text = FormatMm(_defaults.DefaultSlabThicknessMm);
            WallDepthBox.Text     = FormatMm(_defaults.DefaultWallDepthMm);
            MeshSizeBox.Text      = FormatMm(_defaults.DefaultMeshSizeMm);
            SdlBox.Text           = FormatKpa(_defaults.DefaultSdlKPa);
            LiveBox.Text          = FormatKpa(_defaults.DefaultLiveKPa);
            MembraneModBox.Text   = FormatMod(_defaults.DefaultSlabMembraneModifier);
            BendingModBox.Text    = FormatMod(_defaults.DefaultSlabBendingModifier);
            ShearModBox.Text      = FormatMod(_defaults.DefaultSlabShearModifier);
            SafeExePathBox.Text     = _defaults.SafeExePath ?? string.Empty;
            EtabsExePathBox.Text    = _defaults.EtabsExePath ?? string.Empty;
            Sap2000ExePathBox.Text  = _defaults.Sap2000ExePath ?? string.Empty;

            bool isImp = string.Equals(_defaults.UnitSystem, "Imperial", StringComparison.OrdinalIgnoreCase);
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
            // A fresh FirmDefaults is the shipped-defaults reference.
            var fresh = new FirmDefaults();
            _defaults.DefaultGradeCode            = fresh.DefaultGradeCode;
            _defaults.DefaultSlabThicknessMm      = fresh.DefaultSlabThicknessMm;
            _defaults.DefaultWallDepthMm          = fresh.DefaultWallDepthMm;
            _defaults.DefaultDesignCode           = fresh.DefaultDesignCode;
            _defaults.DefaultLoadCombCode         = fresh.DefaultLoadCombCode;
            _defaults.DefaultSdlKPa               = fresh.DefaultSdlKPa;
            _defaults.DefaultLiveKPa              = fresh.DefaultLiveKPa;
            _defaults.DefaultMeshSizeMm           = fresh.DefaultMeshSizeMm;
            _defaults.DefaultSlabMembraneModifier = fresh.DefaultSlabMembraneModifier;
            _defaults.DefaultSlabBendingModifier  = fresh.DefaultSlabBendingModifier;
            _defaults.DefaultSlabShearModifier    = fresh.DefaultSlabShearModifier;
            _defaults.UnitSystem                  = fresh.UnitSystem;
            _defaults.SafeExePath                 = fresh.SafeExePath;
            _defaults.EtabsExePath                = fresh.EtabsExePath;
            _defaults.Sap2000ExePath              = fresh.Sap2000ExePath;
            PopulateFromModel();
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

            target.SafeExePath    = (SafeExePathBox.Text ?? string.Empty).Trim();
            target.EtabsExePath   = (EtabsExePathBox.Text ?? string.Empty).Trim();
            target.Sap2000ExePath = (Sap2000ExePathBox.Text ?? string.Empty).Trim();
            target.UnitSystem = IsImperialSelected ? "Imperial" : "Metric";

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

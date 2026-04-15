using System;
using System.IO;
using System.Reflection;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

public class FirmDefaultsTests
{
    private static readonly Assembly _asm = typeof(Kor.Operations.Services.AppAiService).Assembly;
    private static readonly Type _tDefaults = _asm.GetType(
        "Kor.Operations.EngineeringTools.PdfToSafe.FirmDefaults", throwOnError: true)!;
    private static readonly Type _tExport = _asm.GetType(
        "Kor.Operations.EngineeringTools.PdfToSafe.ExportSettings", throwOnError: true)!;
    private static readonly Type _tDesignCode = _asm.GetType(
        "Kor.Operations.EngineeringTools.PdfToSafe.DesignCodeOption", throwOnError: true)!;

    private static object NewDefaults() =>
        Activator.CreateInstance(_tDefaults, nonPublic: true)!;

    [Fact]
    public void Load_with_no_file_returns_shipped_defaults()
    {
        var load = _tDefaults.GetMethod("Load", BindingFlags.Public | BindingFlags.Static)!;
        var d = load.Invoke(null, null)!;
        // Grade default 'C30', slab thickness 250mm — the KOR Vancouver defaults.
        Assert.Equal("C30", _tDefaults.GetProperty("DefaultGradeCode")!.GetValue(d));
        Assert.Equal(250.0, (double)_tDefaults.GetProperty("DefaultSlabThicknessMm")!.GetValue(d)!, precision: 3);
    }

    [Fact]
    public void ApplyTo_copies_design_code_combo_mesh_modifiers_into_export_settings()
    {
        var d = NewDefaults();
        _tDefaults.GetProperty("DefaultDesignCode")!.SetValue(d, "ACI_318_19");
        _tDefaults.GetProperty("DefaultLoadCombCode")!.SetValue(d, "ASCE7");
        _tDefaults.GetProperty("DefaultMeshSizeMm")!.SetValue(d, 750.0);
        _tDefaults.GetProperty("DefaultSlabMembraneModifier")!.SetValue(d, 0.25);
        _tDefaults.GetProperty("DefaultSlabBendingModifier")!.SetValue(d, 0.35);

        var es = Activator.CreateInstance(_tExport, nonPublic: true)!;

        var applyTo = _tDefaults.GetMethod("ApplyTo", BindingFlags.Public | BindingFlags.Instance)!;
        applyTo.Invoke(d, new[] { es });

        var dc = _tExport.GetProperty("DesignCode")!.GetValue(es);
        Assert.Equal(Enum.Parse(_tDesignCode, "ACI_318_19"), dc);
        Assert.Equal("ASCE7", _tExport.GetProperty("LoadCombCode")!.GetValue(es));
        Assert.Equal(750.0, (double)_tExport.GetProperty("MeshSizeMm")!.GetValue(es)!, precision: 3);
        Assert.Equal(0.25, (double)_tExport.GetProperty("SlabMembraneModifier")!.GetValue(es)!, precision: 3);
        Assert.Equal(0.35, (double)_tExport.GetProperty("SlabBendingModifier")!.GetValue(es)!, precision: 3);
    }

    [Fact]
    public void Save_then_Load_roundtrips_via_the_default_path()
    {
        // DefaultPath lives in %AppData%\KorOperations\. We back up any
        // existing file, write our own, reload, and restore — tests never
        // clobber real firm defaults.
        var pathProp = _tDefaults.GetProperty("DefaultPath", BindingFlags.Public | BindingFlags.Static)!;
        var path = (string)pathProp.GetValue(null)!;
        string? backup = null;
        if (File.Exists(path))
        {
            backup = path + ".bak_" + Guid.NewGuid().ToString("N");
            File.Move(path, backup);
        }

        try
        {
            var d = NewDefaults();
            _tDefaults.GetProperty("DefaultGradeCode")!.SetValue(d, "C50");
            _tDefaults.GetProperty("DefaultSlabThicknessMm")!.SetValue(d, 325.0);
            _tDefaults.GetProperty("DefaultSdlKPa")!.SetValue(d, 2.1);

            var save = _tDefaults.GetMethod("Save", BindingFlags.Public | BindingFlags.Instance)!;
            Assert.True((bool)save.Invoke(d, null)!);
            Assert.True(File.Exists(path));

            var load = _tDefaults.GetMethod("Load", BindingFlags.Public | BindingFlags.Static)!;
            var reloaded = load.Invoke(null, null)!;
            Assert.Equal("C50", _tDefaults.GetProperty("DefaultGradeCode")!.GetValue(reloaded));
            Assert.Equal(325.0, (double)_tDefaults.GetProperty("DefaultSlabThicknessMm")!.GetValue(reloaded)!, precision: 3);
            Assert.Equal(2.1, (double)_tDefaults.GetProperty("DefaultSdlKPa")!.GetValue(reloaded)!, precision: 3);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (backup is not null && File.Exists(backup)) File.Move(backup, path);
        }
    }
}

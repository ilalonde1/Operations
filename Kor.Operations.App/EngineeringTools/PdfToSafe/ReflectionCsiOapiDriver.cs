#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    /// <summary>
    /// Generic <see cref="ISafeOapiDriver"/> implementation that talks to any
    /// CSI product (SAFE, ETABS, SAP2000) via reflection against a pre-loaded
    /// <see cref="SafeOapiTypes"/> bundle. All CSI products share the same
    /// interface shapes — only the namespace prefix and ProgID differ.
    ///
    /// Created by the product-specific facade (<see cref="SafeApiExporter"/>
    /// or <see cref="EtabsApiExporter"/>) after COM activation, then handed
    /// to <see cref="ExportOrchestrator.Run"/>.
    /// </summary>
    internal sealed class ReflectionCsiOapiDriver : ISafeOapiDriver
    {
        private const int DirGravityCode = 10;

        private readonly SafeOapiTypes _types;
        private object? _csi;
        private object? _sapModel;
        private object? _fileObj;
        private object? _propMaterial;
        private object? _propArea;
        private object? _propFrame;
        private object? _loadPatterns;
        private object? _pointObj;
        private object? _areaObj;
        private object? _frameObj;
        private bool _exited;

        public ReflectionCsiOapiDriver(SafeOapiTypes types, object csiObject)
        {
            _types = types;
            _csi = csiObject;
        }

        public int Start()
        {
            RequireCsi();
            int ret;
            if (_types.ApplicationStartArity == 3)
            {
                // SAP2000: ApplicationStart(eUnits, bool visible, string fileName)
                // Start with metric; InitializeNewModel will override if imperial.
                ret = _types.Call<int>(_csi!, _types.COAPI, "ApplicationStart", _types.UnitsNmmC, true, "");
            }
            else
            {
                ret = _types.Call<int>(_csi!, _types.COAPI, "ApplicationStart");
            }
            if (ret == 0) ResolveSubsystems();
            return ret;
        }

        public int Unhide() { RequireCsi(); return _types.Call<int>(_csi!, _types.COAPI, "Unhide"); }

        public int InitializeNewModel(bool imperial)
        {
            RequireSubsystems();
            return _types.Call<int>(_sapModel!, _types.CSapModel, "InitializeNewModel",
                imperial ? _types.UnitsKipInF : _types.UnitsNmmC);
        }

        public int NewBlank() { RequireSubsystems(); return _types.Call<int>(_fileObj!, _types.CFile, "NewBlank"); }
        public int SetMergeTol(double mm) { RequireSubsystems(); return _types.Call<int>(_sapModel!, _types.CSapModel, "SetMergeTol", mm); }
        public int SaveModel(string destPath) { RequireSubsystems(); return _types.Call<int>(_fileObj!, _types.CFile, "Save", destPath); }

        public int SetMaterial(string name, string notes)
        {
            RequireSubsystems();
            return _types.Call<int>(_propMaterial!, _types.CPropMaterial, "SetMaterial",
                name, _types.MatConcrete, 0, notes, "");
        }

        public int SetMPIsotropic(string name, double e, double u, double a)
        {
            RequireSubsystems();
            return _types.Call<int>(_propMaterial!, _types.CPropMaterial, "SetMPIsotropic", name, e, u, a, 0.0);
        }

        public int SetSlabProp(string name, string matName, double thicknessMm)
        {
            RequireSubsystems();
            if (_types.SlabMethodName == "SetShell")
            {
                // SAP2000: SetShell(Name, ShellType, MatProp, MatAng, Thickness, Bending, color, notes, GUID)
                // ShellType 1 = ShellThin, 2 = ShellThick. Use 2 for concrete slabs.
                return _types.Call<int>(_propArea!, _types.CPropArea, "SetShell",
                    name, 2, matName, 0.0, thicknessMm, 0.0, 0, "", "");
            }
            return _types.Call<int>(_propArea!, _types.CPropArea, "SetSlab",
                name, _types.SlabTypeSlab, _types.ShellThick, matName, thicknessMm, 0, "", "");
        }

        public int SetFrameRectangleProp(string name, string matName, double depthMm, double widthMm)
        {
            RequireSubsystems();
            return _types.Call<int>(_propFrame!, _types.CPropFrame, "SetRectangle", name, matName, depthMm, widthMm, 0, "", "");
        }

        public IReadOnlyList<string> ListLoadPatternNames()
        {
            RequireSubsystems();
            var args = new object?[] { 0, Array.Empty<string>() };
            int ret;
            try { ret = _types.Call<int>(_loadPatterns!, _types.CLoadPatterns, "GetNameList", args); }
            catch { return Array.Empty<string>(); }
            if (ret != 0 || args[1] is not string[] names) return Array.Empty<string>();
            return names;
        }

        public int AddLoadPattern(string name, SafeLoadPatternType type)
        {
            RequireSubsystems();
            object enumVal = type switch
            {
                SafeLoadPatternType.SuperDead => _types.PtSuperDead,
                SafeLoadPatternType.Live      => _types.PtLive,
                _ => throw new ArgumentOutOfRangeException(nameof(type)),
            };
            return _types.Call<int>(_loadPatterns!, _types.CLoadPatterns, "Add", name, enumVal, 0.0, true);
        }

        public int AddPoint(double x, double y, double z, out string pointName)
        {
            RequireSubsystems();
            var args = new object?[] { x, y, z, "", "", "GLOBAL", false, 0 };
            int ret = _types.Call<int>(_pointObj!, _types.CPointObj, "AddCartesian", args);
            pointName = ret == 0 ? (string)args[3]! : "";
            return ret;
        }

        public int AddArea(IReadOnlyList<string> pointNames, string propName, out string areaName)
        {
            RequireSubsystems();
            var array = new string[pointNames.Count];
            for (int i = 0; i < pointNames.Count; i++) array[i] = pointNames[i];
            var args = new object?[] { pointNames.Count, array, "", propName, "" };
            int ret = _types.Call<int>(_areaObj!, _types.CAreaObj, "AddByPoint", args);
            areaName = ret == 0 ? (string)args[2]! : "";
            return ret;
        }

        public int AddFrame(string startPointName, string endPointName, string sectionName, out string frameName)
        {
            RequireSubsystems();
            var args = new object?[] { startPointName, endPointName, "", sectionName, "" };
            int ret = _types.Call<int>(_frameObj!, _types.CFrameObj, "AddByPoint", args);
            frameName = ret == 0 ? (string)args[2]! : "";
            return ret;
        }

        public int SetAreaLoadUniform(string areaName, string loadPatternName, double loadNperMm2)
        {
            RequireSubsystems();
            return _types.Call<int>(_areaObj!, _types.CAreaObj, "SetLoadUniform",
                areaName, loadPatternName, loadNperMm2, DirGravityCode, true, "GLOBAL", _types.ItemTypeObjects);
        }

        public int SetPointRestraint(string pointName, bool[] dof6)
        {
            RequireSubsystems();
            if (dof6.Length != 6) throw new ArgumentException("dof6 must be length 6", nameof(dof6));
            var args = new object?[] { pointName, dof6, _types.ItemTypeObjects };
            return _types.Call<int>(_pointObj!, _types.CPointObj, "SetRestraint", args);
        }

        public int SetFrameInsertionPoint(string frameName, int cardinalPoint)
        {
            RequireSubsystems();
            double[] zeros = { 0, 0, 0 };
            var args = new object?[] { frameName, cardinalPoint, false, false, true, zeros, zeros, "GLOBAL", _types.ItemTypeObjects };
            return _types.Call<int>(_frameObj!, _types.CFrameObj, "SetInsertionPoint_1", args);
        }

        public int SetSlabModifiers(string propName, double membrane, double bending, double shear)
        {
            RequireSubsystems();
            // SetModifiers(String Name, ref Double[] Value) — 8 values:
            // f11, f22, f12, m11, m22, m12, v13, v23
            double[] mods = { membrane, membrane, membrane, bending, bending, bending, shear, shear };
            var args = new object?[] { propName, mods };
            return _types.Call<int>(_propArea!, _types.CPropArea, "SetModifiers", args);
        }

        public int SetAreaEdgeConstraint(string areaName, bool enabled)
        {
            RequireSubsystems();
            var args = new object?[] { areaName, enabled, _types.ItemTypeObjects };
            return _types.Call<int>(_areaObj!, _types.CAreaObj, "SetEdgeConstraint", args);
        }

        public int AddGridLines(IReadOnlyList<(string Label, bool IsAlongX, double Ordinate)> gridLines)
        {
            if (gridLines.Count == 0) return 0;
            RequireSubsystems();

            object dbTables = _types.Get(_sapModel!, _types.CSapModel, "DatabaseTables")
                              ?? throw new SafeDriverException("DatabaseTables is null.");
            try
            {
                var sb = new System.Text.StringBuilder();
                foreach (var (label, isAlongX, ordinate) in gridLines)
                {
                    string axisDir = isAlongX ? "X" : "Y";
                    sb.AppendLine($"GLOBAL\t{axisDir}\t{label}\t{ordinate:F4}\tGray8Dark\tYes\tEnd");
                }
                string csv = sb.ToString();

                int version = 0;
                var setArgs = new object?[] { "Grid Lines", version, csv, "\t" };
                int ret;
                try { ret = _types.Call<int>(dbTables, _types.CDatabaseTables, "SetTableForEditingCSVString", setArgs); }
                catch { return 1; }
                if (ret != 0) return ret;

                var applyArgs = new object?[] { false, 0, 0, 0, 0, "" };
                try { ret = _types.Call<int>(dbTables, _types.CDatabaseTables, "ApplyEditedTables", applyArgs); }
                catch { return 1; }
                return ret;
            }
            finally
            {
                ReleaseField(ref dbTables!);
            }
        }

        public void Dispose()
        {
            if (!_exited && _csi is not null)
            {
                try { _types.Call<int>(_csi, _types.COAPI, "ApplicationExit", false); } catch { }
                _exited = true;
            }
            ReleaseField(ref _frameObj);
            ReleaseField(ref _areaObj);
            ReleaseField(ref _pointObj);
            ReleaseField(ref _loadPatterns);
            ReleaseField(ref _propFrame);
            ReleaseField(ref _propArea);
            ReleaseField(ref _propMaterial);
            ReleaseField(ref _fileObj);
            ReleaseField(ref _sapModel);
            ReleaseField(ref _csi);
        }

        private void ResolveSubsystems()
        {
            _sapModel     = _types.Get(_csi!, _types.COAPI, "SapModel")       ?? throw new SafeDriverException("SapModel is null.");
            _fileObj      = _types.Get(_sapModel, _types.CSapModel, "File")    ?? throw new SafeDriverException("File is null.");
            _propMaterial = _types.Get(_sapModel, _types.CSapModel, "PropMaterial") ?? throw new SafeDriverException("PropMaterial is null.");
            _propArea     = _types.Get(_sapModel, _types.CSapModel, "PropArea")     ?? throw new SafeDriverException("PropArea is null.");
            _propFrame    = _types.Get(_sapModel, _types.CSapModel, "PropFrame")    ?? throw new SafeDriverException("PropFrame is null.");
            _loadPatterns = _types.Get(_sapModel, _types.CSapModel, "LoadPatterns") ?? throw new SafeDriverException("LoadPatterns is null.");
            _pointObj     = _types.Get(_sapModel, _types.CSapModel, "PointObj")     ?? throw new SafeDriverException("PointObj is null.");
            _areaObj      = _types.Get(_sapModel, _types.CSapModel, "AreaObj")      ?? throw new SafeDriverException("AreaObj is null.");
            _frameObj     = _types.Get(_sapModel, _types.CSapModel, "FrameObj")     ?? throw new SafeDriverException("FrameObj is null.");
        }

        private void RequireCsi() { if (_csi is null) throw new SafeDriverException("Driver disposed or never connected."); }
        private void RequireSubsystems() { RequireCsi(); if (_sapModel is null) throw new SafeDriverException("Start() must succeed first."); }

        private static void ReleaseField(ref object? field)
        {
            if (field is null) return;
            try { if (Marshal.IsComObject(field)) Marshal.FinalReleaseComObject(field); } catch { }
            field = null;
        }
    }
}

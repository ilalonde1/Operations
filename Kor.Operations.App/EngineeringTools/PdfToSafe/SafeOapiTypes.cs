#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    /// <summary>
    /// Bundle of reflected interface types, pre-parsed enum values, and the
    /// reflective <see cref="Call{T}"/> / <see cref="Get"/> helpers used to
    /// invoke SAFE's OAPI. Resolved once per export from the SAFEv1.dll that
    /// ships with the target SAFE install, so the exporter is version-matched
    /// to whatever the user is running.
    ///
    /// Extracted from <see cref="SafeApiExporter"/>'s former nested class so
    /// both the real driver and the preflight compatibility scanner can use
    /// it without being re-implemented.
    /// </summary>
    internal sealed class SafeOapiTypes
    {
        public required Type COAPI           { get; init; }
        public required Type CSapModel       { get; init; }
        public required Type CFile           { get; init; }
        public required Type CPropMaterial   { get; init; }
        public required Type CPropArea       { get; init; }
        public required Type CPropFrame      { get; init; }
        public required Type CLoadPatterns   { get; init; }
        public required Type CPointObj       { get; init; }
        public required Type CAreaObj        { get; init; }
        public required Type CFrameObj       { get; init; }
        public required Type CDatabaseTables { get; init; }

        /// <summary>0 for SAFE/ETABS (no-arg), 3 for SAP2000 (units, visible, fileName).</summary>
        public int ApplicationStartArity { get; init; }
        /// <summary>"SetSlab" for SAFE/ETABS (8 args), "SetShell" for SAP2000 (9 args).</summary>
        public string SlabMethodName { get; init; } = "SetSlab";
        /// <summary>8 for SetSlab, 9 for SetShell.</summary>
        public int SlabMethodArity { get; init; } = 8;

        public required object UnitsNmmC     { get; init; }
        public required object UnitsKipInF   { get; init; }
        public required object MatConcrete   { get; init; }
        /// <summary>Null for SAP2000 (uses bare int in SetShell, not an enum).</summary>
        public object? SlabTypeSlab  { get; init; }
        /// <summary>Null for SAP2000.</summary>
        public object? ShellThick    { get; init; }
        public required object PtSuperDead   { get; init; }
        public required object PtLive        { get; init; }
        /// <summary>eItemType.Objects. Resolved as an enum value (not int) because reflection does not auto-coerce int→enum when invoking methods whose parameter type is a specific enum.</summary>
        public required object ItemTypeObjects { get; init; }

        /// <summary>
        /// Non-throwing type/member resolution used by both the real export
        /// and the preflight compatibility check. On failure, <paramref name="types"/>
        /// is null and <paramref name="issues"/> lists every missing type,
        /// enum value, method, or property so the UI can surface the exact gap.
        /// </summary>
        /// <summary>
        /// Loads from a SAFEv1.dll — convenience overload using "SAFEv1" prefix.
        /// </summary>
        public static bool TryLoad(Assembly asm, out SafeOapiTypes? types, out List<string> issues)
            => TryLoad(asm, "SAFEv1", out types, out issues);

        /// <summary>
        /// Loads from any CSI OAPI DLL (SAFEv1.dll or ETABSv1.dll) using the
        /// specified namespace prefix. All CSI products share the same interface
        /// shapes — only the namespace differs.
        /// </summary>
        public static bool TryLoad(Assembly asm, string nsPrefix, out SafeOapiTypes? types, out List<string> issues)
        {
            types = null;
            var foundIssues = new List<string>();

            Type? cOAPI       = TryGetType(asm, $"{nsPrefix}.cOAPI",           foundIssues);
            Type? cSapModel   = TryGetType(asm, $"{nsPrefix}.cSapModel",       foundIssues);
            Type? cFile       = TryGetType(asm, $"{nsPrefix}.cFile",           foundIssues);
            Type? cPropMat    = TryGetType(asm, $"{nsPrefix}.cPropMaterial",   foundIssues);
            Type? cPropArea   = TryGetType(asm, $"{nsPrefix}.cPropArea",       foundIssues);
            Type? cPropFrame  = TryGetType(asm, $"{nsPrefix}.cPropFrame",      foundIssues);
            Type? cLoadPat    = TryGetType(asm, $"{nsPrefix}.cLoadPatterns",   foundIssues);
            Type? cPoint      = TryGetType(asm, $"{nsPrefix}.cPointObj",       foundIssues);
            Type? cArea       = TryGetType(asm, $"{nsPrefix}.cAreaObj",        foundIssues);
            Type? cFrame      = TryGetType(asm, $"{nsPrefix}.cFrameObj",       foundIssues);
            Type? cDbTables   = TryGetType(asm, $"{nsPrefix}.cDatabaseTables", foundIssues);
            Type? eUnits      = TryGetType(asm, $"{nsPrefix}.eUnits",          foundIssues);
            Type? eMat        = TryGetType(asm, $"{nsPrefix}.eMatType",        foundIssues);
            // eSlabType/eShellType exist in SAFE/ETABS but NOT in SAP2000 (which
            // uses bare int ShellType in SetShell). Optional — null is OK.
            Type? eSlab       = asm.GetType($"{nsPrefix}.eSlabType");
            Type? eShell      = asm.GetType($"{nsPrefix}.eShellType");
            Type? eLoadPat    = TryGetType(asm, $"{nsPrefix}.eLoadPatternType",foundIssues);
            Type? eItem       = TryGetType(asm, $"{nsPrefix}.eItemType",       foundIssues);

            if (foundIssues.Count > 0) { issues = foundIssues; return false; }

            var issuesLocal = foundIssues;

            object? unitsVal    = TryEnumValue(eUnits!,   "N_mm_C",    issuesLocal);
            object? unitsImp   = TryEnumValue(eUnits!,   "kip_in_F",  issuesLocal);
            object? matVal      = TryEnumValue(eMat!,     "Concrete",  issuesLocal);
            // Null when SAP2000 (uses bare int ShellType, not enums).
            object? slabVal  = eSlab  is not null ? TryEnumValue(eSlab,  "Slab",      issuesLocal) : null;
            object? shellVal = eShell is not null ? TryEnumValue(eShell, "ShellThick", issuesLocal) : null;
            object? sdlVal      = TryEnumValue(eLoadPat!, "SuperDead", issuesLocal);
            object? liveVal     = TryEnumValue(eLoadPat!, "Live",      issuesLocal);
            object? itemVal     = TryEnumValue(eItem!,    "Objects",   issuesLocal);

            // ApplicationStart: 0 args (SAFE/ETABS) or 3 args (SAP2000).
            int appStartArity = 0;
            bool hasStart0 = HasMethod(cOAPI!, "ApplicationStart", 0);
            bool hasStart3 = HasMethod(cOAPI!, "ApplicationStart", 3);
            if (hasStart0) appStartArity = 0;
            else if (hasStart3) appStartArity = 3;
            else issuesLocal.Add("cOAPI.ApplicationStart(0 or 3 args) missing");
            CheckMethod(cOAPI!,       "ApplicationExit",    1, issuesLocal);
            CheckMethod(cOAPI!,       "Unhide",              0, issuesLocal);
            CheckMethod(cSapModel!,   "InitializeNewModel", 1, issuesLocal);
            CheckMethod(cSapModel!,   "SetMergeTol",        1, issuesLocal);
            CheckMethod(cFile!,       "NewBlank",           0, issuesLocal);
            CheckMethod(cFile!,       "Save",               1, issuesLocal);
            CheckMethod(cPropMat!,    "SetMaterial",        5, issuesLocal);
            CheckMethod(cPropMat!,    "SetMPIsotropic",     5, issuesLocal);
            // Slab property: "SetSlab" (8 args, SAFE/ETABS) or "SetShell" (9 args, SAP2000).
            string slabMethod = "SetSlab"; int slabArity = 8;
            if (HasMethod(cPropArea!, "SetSlab", 8)) { slabMethod = "SetSlab"; slabArity = 8; }
            else if (HasMethod(cPropArea!, "SetShell", 9)) { slabMethod = "SetShell"; slabArity = 9; }
            else issuesLocal.Add("cPropArea.SetSlab(8) or SetShell(9) missing");
            CheckMethod(cPropFrame!,  "SetRectangle",       7, issuesLocal);
            CheckMethod(cLoadPat!,    "Add",                4, issuesLocal);
            CheckMethod(cLoadPat!,    "GetNameList",        2, issuesLocal);
            CheckMethod(cPoint!,      "AddCartesian",       8, issuesLocal);
            CheckMethod(cPoint!,      "SetRestraint",       3, issuesLocal);
            CheckMethod(cArea!,       "AddByPoint",         5, issuesLocal);
            CheckMethod(cArea!,       "SetLoadUniform",     7, issuesLocal);
            CheckMethod(cFrame!,      "AddByPoint",         5, issuesLocal);
            CheckMethod(cPropArea!,   "SetModifiers",        2, issuesLocal);
            CheckMethod(cFrame!,      "SetInsertionPoint_1",9, issuesLocal);
            CheckMethod(cArea!,       "SetEdgeConstraint",  3, issuesLocal);

            CheckProperty(cOAPI!,      "SapModel",     issuesLocal);
            CheckProperty(cSapModel!,  "File",         issuesLocal);
            CheckProperty(cSapModel!,  "PropMaterial", issuesLocal);
            CheckProperty(cSapModel!,  "PropArea",     issuesLocal);
            CheckProperty(cSapModel!,  "PropFrame",    issuesLocal);
            CheckProperty(cSapModel!,  "LoadPatterns", issuesLocal);
            CheckProperty(cSapModel!,  "PointObj",     issuesLocal);
            CheckProperty(cSapModel!,  "AreaObj",      issuesLocal);
            CheckProperty(cSapModel!,  "FrameObj",     issuesLocal);
            CheckProperty(cSapModel!,  "DatabaseTables", issuesLocal);
            CheckMethod(cDbTables!,    "SetTableForEditingCSVString", 4, issuesLocal);
            CheckMethod(cDbTables!,    "ApplyEditedTables",           6, issuesLocal);

            if (issuesLocal.Count > 0) { issues = issuesLocal; return false; }

            types = new SafeOapiTypes
            {
                COAPI           = cOAPI!,
                CSapModel       = cSapModel!,
                CFile           = cFile!,
                CPropMaterial   = cPropMat!,
                CPropArea       = cPropArea!,
                CPropFrame      = cPropFrame!,
                CLoadPatterns   = cLoadPat!,
                CPointObj       = cPoint!,
                CAreaObj        = cArea!,
                CFrameObj       = cFrame!,
                CDatabaseTables = cDbTables!,
                ApplicationStartArity = appStartArity,
                SlabMethodName  = slabMethod,
                SlabMethodArity = slabArity,
                UnitsNmmC       = unitsVal!,
                UnitsKipInF     = unitsImp!,
                MatConcrete     = matVal!,
                SlabTypeSlab    = slabVal!,
                ShellThick      = shellVal!,
                PtSuperDead     = sdlVal!,
                PtLive          = liveVal!,
                ItemTypeObjects = itemVal!,
            };
            issues = issuesLocal;
            return true;
        }

        /// <summary>
        /// Invokes <paramref name="methodName"/> on <paramref name="target"/>
        /// via the declared <paramref name="iface"/>. Picks the first method
        /// with a matching parameter count — SAFE's OAPI does not overload by
        /// arity on any method we call, so this is safe.
        /// </summary>
        public T Call<T>(object target, Type iface, string methodName, params object?[] args)
        {
            MethodInfo mi = FindMethod(iface, methodName, args.Length);
            object? result = mi.Invoke(target, args);
            return result is T t ? t : default!;
        }

        public object? Get(object target, Type iface, string propName)
        {
            PropertyInfo pi = iface.GetProperty(propName)
                ?? throw new MissingMemberException($"{iface.Name}.{propName} property not found.");
            return pi.GetValue(target);
        }

        private static MethodInfo FindMethod(Type iface, string name, int arity)
        {
            foreach (var m in iface.GetMethods())
                if (m.Name == name && m.GetParameters().Length == arity)
                    return m;
            throw new MissingMemberException($"{iface.Name}.{name}({arity} args) not found.");
        }

        private static Type? TryGetType(Assembly asm, string name, List<string> issues)
        {
            var t = asm.GetType(name);
            if (t is null) issues.Add($"type '{name}' missing");
            return t;
        }

        private static object? TryEnumValue(Type enumType, string name, List<string> issues)
        {
            if (!enumType.IsEnum) { issues.Add($"{enumType.Name} is not an enum"); return null; }
            if (!Enum.IsDefined(enumType, name)) { issues.Add($"{enumType.Name}.{name} missing"); return null; }
            return Enum.Parse(enumType, name);
        }

        private static bool HasMethod(Type iface, string name, int arity)
        {
            foreach (var m in iface.GetMethods())
                if (m.Name == name && m.GetParameters().Length == arity)
                    return true;
            return false;
        }

        private static void CheckMethod(Type iface, string name, int arity, List<string> issues)
        {
            foreach (var m in iface.GetMethods())
                if (m.Name == name && m.GetParameters().Length == arity)
                    return;
            issues.Add($"{iface.Name}.{name}({arity} args) missing");
        }

        private static void CheckProperty(Type iface, string name, List<string> issues)
        {
            if (iface.GetProperty(name) is null)
                issues.Add($"{iface.Name}.{name} property missing");
        }
    }
}

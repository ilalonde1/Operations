#nullable enable
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe
{
    /// <summary>
    /// Contract test: loads the bundled SAFEv1.dll in <c>lib/SAFE/</c> and
    /// asserts that <see cref="SafeOapiTypes.TryLoad"/> resolves every type,
    /// enum value, method+arity, and property our exporter calls at runtime.
    ///
    /// Catches: a SAFE DLL upgrade dropping/renaming a required member, or an
    /// edit that adds a new OAPI call to the exporter without updating the
    /// preflight whitelist in <see cref="SafeOapiTypes.TryLoad"/>.
    ///
    /// We load into an isolated AssemblyLoadContext so the test doesn't
    /// pollute the default load context (the production app uses the same
    /// technique in <c>SafeApiExporter.CheckCompatibility</c>).
    /// </summary>
    public class SafeOapiTypesContractTests
    {
        /// <summary>Resolves the bundled SAFEv1.dll via two candidate paths: repo lib/SAFE/ (runs in dev boxes) and beside the test binary (CI scenarios).</summary>
        private static string LocateBundledSafeV1Dll()
        {
            string? asmDir = Path.GetDirectoryName(typeof(SafeOapiTypesContractTests).Assembly.Location);
            string[] candidates =
            {
                Path.Combine(asmDir ?? ".", "SAFEv1.dll"),
                // walk up to repo root then into lib/SAFE
                Path.GetFullPath(Path.Combine(asmDir ?? ".", "..", "..", "..", "..", "..", "lib", "SAFE", "SAFEv1.dll")),
                Path.GetFullPath(Path.Combine(asmDir ?? ".", "..", "..", "..", "..", "lib", "SAFE", "SAFEv1.dll")),
            };
            foreach (var c in candidates)
                if (File.Exists(c)) return c;
            throw new FileNotFoundException(
                "Bundled SAFEv1.dll not found for contract test. Looked at:\n  " +
                string.Join("\n  ", candidates));
        }

        [Fact]
        public void BundledSafeV1_TryLoad_ReturnsTrueWithNoIssues()
        {
            string dllPath = LocateBundledSafeV1Dll();
            var ctx = new AssemblyLoadContext($"contract-test-{Guid.NewGuid():N}", isCollectible: true);
            try
            {
                Assembly asm = ctx.LoadFromAssemblyPath(dllPath);
                bool ok = SafeOapiTypes.TryLoad(asm, out var types, out var issues);

                Assert.True(ok, "TryLoad should resolve all required members on the bundled SAFEv1.dll. Issues: "
                    + string.Join("; ", issues));
                Assert.NotNull(types);
                Assert.Empty(issues);
            }
            finally { ctx.Unload(); }
        }

        [Fact]
        public void BundledSafeV1_ResolvedTypes_AreNonNull()
        {
            string dllPath = LocateBundledSafeV1Dll();
            var ctx = new AssemblyLoadContext($"contract-test-{Guid.NewGuid():N}", isCollectible: true);
            try
            {
                Assembly asm = ctx.LoadFromAssemblyPath(dllPath);
                SafeOapiTypes.TryLoad(asm, out var types, out _);
                Assert.NotNull(types);
                Assert.NotNull(types!.COAPI);
                Assert.NotNull(types.CSapModel);
                Assert.NotNull(types.CFile);
                Assert.NotNull(types.CPropMaterial);
                Assert.NotNull(types.CPropArea);
                Assert.NotNull(types.CPropFrame);
                Assert.NotNull(types.CLoadPatterns);
                Assert.NotNull(types.CPointObj);
                Assert.NotNull(types.CAreaObj);
                Assert.NotNull(types.CFrameObj);
                Assert.NotNull(types.UnitsNmmC);
                Assert.NotNull(types.MatConcrete);
                Assert.NotNull(types.SlabTypeSlab);
                Assert.NotNull(types.ShellThick);
                Assert.NotNull(types.PtSuperDead);
                Assert.NotNull(types.PtLive);
                Assert.NotNull(types.ItemTypeObjects);
            }
            finally { ctx.Unload(); }
        }

        [Fact]
        public void BundledSafeV1_MissingType_ReportsIt()
        {
            // Guard against preflight silently passing when the assembly is
            // actually empty. Build a trivial throwaway assembly with no
            // SAFEv1 types and assert that every expected type is reported
            // missing. This exercises the issue-accumulation logic, not the
            // happy path.
            var emptyAsm = typeof(string).Assembly; // mscorlib — has no SAFEv1 types
            bool ok = SafeOapiTypes.TryLoad(emptyAsm, out var types, out var issues);

            Assert.False(ok);
            Assert.Null(types);
            Assert.NotEmpty(issues);
            // Sanity-spot-check: cOAPI and cSapModel are the root types we
            // absolutely cannot do without. The issue messages MUST name them.
            Assert.Contains(issues, m => m.Contains("cOAPI"));
            Assert.Contains(issues, m => m.Contains("cSapModel"));
        }
    }
}

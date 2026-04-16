#nullable enable
using System.Linq;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe
{
    /// <summary>
    /// Guardrail tests for <see cref="RealSafeOapiDriver"/>'s ProgID resolution.
    /// The actual COM activation requires a live SAFE install and cannot be
    /// unit-tested — but the DATA the driver uses to decide which ProgID to
    /// activate, and the ORDER it tries them in, are plain in-memory config
    /// that should never silently change.
    ///
    /// These tests exist so a future edit that (for example) drops the
    /// SAFE-21-registered "CSI.SAFE.API.ETABSObject" in favour of the old
    /// "CSI.SAFE.API.SafeObject" would break locally at `dotnet test` instead
    /// of requiring a publish + failed click on an engineer's machine.
    /// </summary>
    public class RealSafeOapiDriverTests
    {
        [Fact]
        public void CandidateProgIds_IncludesTheSafe21RegisteredName()
        {
            // From the test PC's registry on 2026-04-15: SAFE 21's RegisterSAFE.exe
            // writes CSI.SAFE.API.ETABSObject (NOT CSI.SAFE.API.SafeObject) as the
            // ProgID for the managed COM class hosted in SAFE.EXE. If this
            // assertion fails, the exporter will throw
            // "No SAFE COM ProgID registered" on every SAFE 21 install and
            // engineers will be stuck. Do not remove.
            Assert.Contains("CSI.SAFE.API.ETABSObject", SafeApiExporter.CandidateProgIds);
        }

        [Fact]
        public void CandidateProgIds_PreservesTheLegacyNameAsFallback()
        {
            // Kept in case any SAFE release / edition uses the sensibly-named
            // ProgID. Removing this means we silently stop working on those
            // installs. If CSI ever rationalises their naming and publishes
            // official guidance, update both this test and the array.
            Assert.Contains("CSI.SAFE.API.SafeObject", SafeApiExporter.CandidateProgIds);
        }

        [Fact]
        public void CandidateProgIds_TriesEtabsObjectNameFirst()
        {
            // Priority order matters: CSI.SAFE.API.ETABSObject is what current
            // SAFE versions actually register, so it MUST be attempted before
            // the legacy fallback. Resolving the legacy name first on a machine
            // where both exist (unlikely but possible) could pick the wrong
            // class and the orchestrator would later fail on interface cast.
            var idx = SafeApiExporter.CandidateProgIds.ToList();
            int etabs  = idx.IndexOf("CSI.SAFE.API.ETABSObject");
            int legacy = idx.IndexOf("CSI.SAFE.API.SafeObject");
            Assert.True(etabs >= 0 && legacy >= 0, "both candidates must be present");
            Assert.True(etabs < legacy, "ETABSObject candidate must be tried before the legacy SafeObject name");
        }

        [Fact]
        public void CandidateProgIds_ContainsNoExtraneousNames()
        {
            // Prevent accidental additions of untested ProgIDs. Each entry
            // should have explicit provenance (registry verified or CSI docs).
            Assert.Equal(2, SafeApiExporter.CandidateProgIds.Length);
        }
    }
}

using System;
using System.Reflection;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

/// <summary>
/// Resolve a PdfToSafe type by name, from whichever assembly currently holds it.
/// </summary>
/// <remarks>
/// These tests reach for their subjects by name because the types were internal and the comment on
/// ExportValidatorTests explains why: reflection kept the tests narrow without widening the
/// production surface.
///
/// That worked while every one of them lived in Kor.Operations.App. On 2 September the geometry
/// model, the classifier and the DXF writer moved to Core so `takeoff pdf-takeoff` could reach
/// them, and 47 of these tests went red at once — not because anything they assert changed, but
/// because `AppAssembly.GetType("...ExtractedGeometry")` now returns null. The type is fine; the
/// address was hard-coded.
///
/// So the address is asked for, not assumed. Core first, since that is where the shared half lives
/// now, then the app for the parts that are still its own — ExportValidator, ValidationResult and
/// ExportSettings among them.
/// </remarks>
internal static class PdfToSafeTestTypes
{
    private static readonly Assembly App =
        typeof(Kor.Operations.Services.AppAiService).Assembly;

    private static readonly Assembly Core =
        typeof(Kor.Operations.EngineeringTools.PdfToSafe.ExtractedGeometry).Assembly;

    public static Type Resolve(string fullName) =>
        Core.GetType(fullName) ?? App.GetType(fullName, throwOnError: true)!;
}

using System.Linq;
using System.Reflection;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.AiTools;

/// <summary>
/// Structural tests for the PDF-to-SAFE AI tool schema catalogue. Ensures every
/// schema is parseable JSON and that every public tool name constant appears in
/// the authoritative list — catches drift between constants and the catalogue.
/// </summary>
public class PdfToSafeAiToolsTests
{
    [Fact]
    public void AllToolSchemasParseAsJson()
    {
        // Accessing the static member forces the type initializer (static ctor)
        // which deserializes every schema. Any malformed JSON throws here.
        var asm = typeof(Kor.Operations.Services.AppAiService).Assembly;
        var toolsType = asm.GetType("Kor.Operations.Services.AiTools.PdfToSafeAiTools", throwOnError: true)!;

        var allField = toolsType.GetField("All", BindingFlags.NonPublic | BindingFlags.Static)!;
        var allEnumerable = (System.Collections.IEnumerable)allField.GetValue(null)!;

        int count = allEnumerable.Cast<object>().Count();
        Assert.True(count >= 5, $"Expected ≥5 tools, got {count}");
    }

    [Fact]
    public void EveryToolNameConstantAppearsInAllList()
    {
        var asm = typeof(Kor.Operations.Services.AppAiService).Assembly;
        var toolsType = asm.GetType("Kor.Operations.Services.AiTools.PdfToSafeAiTools", throwOnError: true)!;

        var constants = toolsType
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetValue(null)!)
            .ToList();

        var allField = toolsType.GetField("All", BindingFlags.NonPublic | BindingFlags.Static)!;
        var allList = ((System.Collections.IEnumerable)allField.GetValue(null)!).Cast<object>().ToList();

        var toolRecordType = asm.GetType("Kor.Operations.Services.AiTool", throwOnError: true)!;
        var nameProp = toolRecordType.GetProperty("Name")!;
        var namesInList = allList.Select(t => (string)nameProp.GetValue(t)!).ToHashSet();

        foreach (var c in constants)
            Assert.Contains(c, namesInList);
    }
}

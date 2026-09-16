using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// AN INSTRUMENT, NOT A GATE (2026-09-16): builds ONE of the six banked sets exactly as the gate does and
/// writes the slab pass's trace per sheet to <c>TestResults/slab-trace/&lt;job&gt;/slab-trace.txt</c>.
/// A single page through <c>takeoff pdf-overlay --walls</c> reads its walls without the set's schedule
/// and can find a floor the set build does not (31202 L6, measured 2026-09-16: 19,860 sq ft alone,
/// 982 in the set); what the gate sees is only measurable the way the gate builds. Runs only when
/// <c>KOR_SLAB_TRACE_JOB</c> names a set:
/// <code>KOR_SLAB_TRACE_JOB=31202-01 dotnet test --filter FullyQualifiedName~SlabPassTraceProbe</code>
/// </summary>
[Trait("Speed", "Slow")]
[Collection(SheetNamingVocabularyCollection.Name)]
public sealed class SlabPassTraceProbe
{
    [Fact]
    public void TraceTheSlabPassOfOneBankedSet()
    {
        string? job = Environment.GetEnvironmentVariable("KOR_SLAB_TRACE_JOB");
        if (string.IsNullOrWhiteSpace(job)) return;
        string? conn = Environment.GetEnvironmentVariable(RuleSettings.ConnectionEnvironmentVariable);
        Assert.False(string.IsNullOrWhiteSpace(conn), $"{RuleSettings.ConnectionEnvironmentVariable} is not set");
        var set = Assert.Single(SixSetsBuildAsBankedTests.Sets, s => s.Job.Equals(job, StringComparison.OrdinalIgnoreCase));
        var (options, _) = PdfIntakeOptions.For(conn);
        string pdf = DrawingMirror.SingleFile(set.SharePath);
        string work = Path.Combine(AppContext.BaseDirectory, "TestResults", "slab-trace", set.Job);

        var lines = new List<string>();
        var buffer = new List<string>();
        GeometryFilterService.FaceTrace = s => { lock (buffer) buffer.Add(s); };
        try
        {
            PdfOnlyBuild.Build(pdf, work, set.Scale, options, rulesConnection: conn, stem: set.Job, onSheet: o =>
            {
                lock (buffer)
                {
                    var slab = buffer.Where(l => l.StartsWith("slab pass", StringComparison.Ordinal)).ToList();
                    if (slab.Count > 0)
                    {
                        lines.Add($"== p{o.Page} {o.SheetNumber} {o.Title} | slabs {o.Slabs} columns {o.Columns} walls {o.Walls}");
                        lines.AddRange(slab);
                    }
                    buffer.Clear();
                }
            });
        }
        finally { GeometryFilterService.FaceTrace = null; }
        Directory.CreateDirectory(work);
        File.WriteAllLines(Path.Combine(work, "slab-trace.txt"), lines);
    }
}

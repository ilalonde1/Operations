using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// AN INSTRUMENT, NOT A GATE (2026-09-16): builds ONE of the six banked sets exactly as the gate does - or any corpus job
/// by the cached census's newest issue, as the analyzer builds it (20:10) - and
/// writes the slab pass's trace per sheet to <c>TestResults/slab-trace/&lt;job&gt;/slab-trace.txt</c>.
/// A single page through <c>takeoff pdf-overlay --walls</c> reads its walls without the set's schedule
/// and can find a floor the set build does not (31202 L6, measured 2026-09-16: 19,860 sq ft alone,
/// 982 in the set); what the gate sees is only measurable the way the gate builds. Runs only when
/// <c>KOR_SLAB_TRACE_JOB</c> names a set; <c>KOR_SLAB_TRACE_PAGES=18</c> (or <c>18-20</c>) reads those pages alone:
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
        var (options, _) = PdfIntakeOptions.For(conn);
        // one of the six banked sets by its banked path and scale; any other corpus job by the cached census's newest
        // issue (the mirror's copy, no share walk) at the options' fallback scale, as the analyzer builds it (20:10)
        var banked = SixSetsBuildAsBankedTests.Sets.FirstOrDefault(s => s.Job.Equals(job, StringComparison.OrdinalIgnoreCase));
        string sharePath; int scale;
        if (banked is not null) { sharePath = banked.SharePath; scale = banked.Scale; }
        else
        {
            var census = StickFileCorpus.CensusCached(PublishDiscovery.ProjectsRoot, new List<string>(), TimeSpan.FromDays(30), false);
            var jc = Assert.Single(census, c => c.Job.Equals(job, StringComparison.OrdinalIgnoreCase));
            sharePath = jc.NewestIssue ?? throw new InvalidOperationException($"{job} has no stick file in the census");
            scale = options.FallbackScale;
        }
        var set = (Job: job!, SharePath: sharePath, Scale: scale);
        string pdf = DrawingMirror.SingleFile(set.SharePath);
        string work = Path.Combine(AppContext.BaseDirectory, "TestResults", "slab-trace", set.Job);

        var lines = new List<string>();
        var buffer = new List<string>();
        GeometryFilterService.FaceTrace = s => { lock (buffer) buffer.Add(s); };
        try
        {
            // KOR_SLAB_TRACE_PAGES=18 or 18-20 reads those pages alone: one sheet's trace in a minute, not the set's (2026-09-18)
            int? firstPage = null, lastPage = null;
            if (Environment.GetEnvironmentVariable("KOR_SLAB_TRACE_PAGES") is { Length: > 0 } span)
            {
                var parts = span.Split('-');
                firstPage = int.Parse(parts[0]); lastPage = int.Parse(parts[^1]);
            }
            PdfOnlyBuild.Build(pdf, work, set.Scale, options, rulesConnection: conn, stem: set.Job, firstPage: firstPage, lastPage: lastPage, onSheet: o =>
            {
                lock (buffer)
                {
                    // KOR_SLAB_TRACE_LINES=1 keeps every line's fate too (a pen's lines refused as walls, 30838's 8 pt outline, 21:50)
                    bool everyLine = Environment.GetEnvironmentVariable("KOR_SLAB_TRACE_LINES") == "1";
                    var slab = buffer.Where(l => l.StartsWith("slab pass", StringComparison.Ordinal) || (everyLine && l.StartsWith("line ", StringComparison.Ordinal))).ToList();
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

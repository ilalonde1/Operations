#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A stick file that is another job's, byte for byte, is not this job's set (intake step 60, 2026-09-13).
/// Run 8 read 39 sets with no storey, and 16 of them were one file: 01783-01's five-page stick file filed
/// under sixteen other job numbers. The analyzer reads it once, under the job whose number its name
/// carries, and the other rows say whose file it is and build nothing.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: two identical files under two jobs map the one whose number is not in the name to
/// the one whose number is; a third file of the same name and length but different bytes is nobody's
/// copy; with no job named in the file, the first by job number owns it. WHAT IT DOES NOT: the row the
/// analyzer writes for a copy (the reason text is a constant it reads back through Reason); a file copied
/// under a different NAME (grouped by length and name first, so never hashed); the share itself.
/// </remarks>
public sealed class AStickFileThatIsAnotherJobsIsReadOnceTests
{
    [Fact]
    public void TheJobNamedInTheFileOwnsItAndTheCopiesSaySo()
    {
        string root = Path.Combine(Path.GetTempPath(), $"kor-dup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            byte[] bytes = new byte[4096]; new Random(7).NextBytes(bytes);
            byte[] other = (byte[])bytes.Clone(); other[100] ^= 0xFF;
            string name = "01783-01 2026-02-23 Bean Around The World Stickfile.pdf";
            string a = Path.Combine(root, "a", name), b = Path.Combine(root, "b", name), c = Path.Combine(root, "c", name), d = Path.Combine(root, "d", "31168-01 set.pdf"), e = Path.Combine(root, "e", "31168-01 set.pdf");
            foreach (var p in new[] { a, b, c, d, e }) Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllBytes(a, bytes); File.WriteAllBytes(b, bytes); File.WriteAllBytes(c, other);
            File.WriteAllBytes(d, other); File.WriteAllBytes(e, other);

            var files = new List<(string Job, string Path, long Bytes)>
            {
                ("31237-01", b, bytes.Length), ("01783-01", a, bytes.Length), ("00904-01", c, other.Length),   // c: same name and length, different bytes
                ("50054-01", e, other.Length), ("30840-01", d, other.Length),                                   // no job named in the file: the first by number owns it
            };
            var log = new List<string>();
            var owners = CorpusAnalyzer.AnotherJobsFile(files, p => p, log.Add);

            Assert.Equal("01783-01", owners["31237-01"]);
            Assert.False(owners.ContainsKey("01783-01"));
            Assert.False(owners.ContainsKey("00904-01"));
            Assert.Equal("30840-01", owners["50054-01"]);
            Assert.False(owners.ContainsKey("30840-01"));
            Assert.Equal(2, owners.Count);
            Assert.Equal(2, log.Count);
            // two copies of two different files group under one reason: the grouper stops at the first digit
            string reason = CorpusAnalyzer.Reason($"{CorpusAnalyzer.AnotherJobsFileReason}01783-01's ({name})");
            Assert.Equal(reason, CorpusAnalyzer.Reason($"{CorpusAnalyzer.AnotherJobsFileReason}30840-01's (31168-01 set.pdf)"));
            Assert.StartsWith("the stick file of another job", reason, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}

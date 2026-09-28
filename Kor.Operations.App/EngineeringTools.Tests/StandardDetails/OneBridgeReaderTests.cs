using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.StandardDetails;

/// <summary>
/// One reader for a bridge reply, one definition of what a KOR-D number is.
///
/// WHY THIS GATE EXISTS. MasterPublisher and SheetComposer each grew a private copy of the whole
/// set — the prefix regex, TryReadViewPrefix, EnumerateResultItems, TryGetProperty/String/Int64 —
/// and the copies had already drifted: MasterPublisher read a "parameters" payload whether the
/// bridge sent it as an object or an array, SheetComposer gave up unless it was an array. The same
/// reply was therefore a catalogued detail to one reader and not to the other, and nothing said so.
/// A third copy was about to be written for the detail intake. This is the check that would have
/// caught both, written instead of writing the third.
///
/// WHAT IT COVERS: that each listed member name is DEFINED exactly once inside the StandardDetails
/// folder — every reader of a KOR.Drafter bridge reply lives there — and that the KOR-D pattern
/// literal appears in exactly one file. It reads source text, so it holds for any future file in
/// that folder without anybody remembering to add it here.
///
/// WHAT IT DOES NOT COVER: two definitions that disagree while using different NAMES — a
/// ReadPrefix() next to a TryReadViewPrefix() passes this gate. It says nothing about whether the
/// surviving implementation is correct, only that there is one of it. It does not look outside
/// Kor.Operations.App, so a copy in KOR.RevitTools or in the bridge add-in itself is invisible.
///
/// AND IT IS SCOPED ON PURPOSE. Written against the whole app, it also flagged
/// EngineeringTools/PdfToSafe/PdfToSafeWindow.AiWiring.cs, which has its own TryGetString /
/// TryGetBool / TryGetDouble. Those read AI responses, not bridge replies: same method names,
/// different payload, no shared rule to drift. Folding them in would make this gate assert
/// something it is not named for. PdfToSafe's own duplication, if it matters, needs its own check.
///
/// A same-class fault it would NOT catch: a caller that parses a bridge reply inline with its own
/// JsonElement walking instead of calling any of these at all.
/// </summary>
public sealed class OneBridgeReaderTests
{
    /// <summary>The members that decide how a bridge reply is read. One definition each.</summary>
    private static readonly string[] SharedReaders =
    [
        "TryReadViewPrefix",
        "ReadScalarOrDisplayValue",
        "EnumerateResultItems",
        "TryGetProperty",
        "TryGetString",
        "TryGetInt64",
        "TryGetInt32",
        "TryGetDouble",
        "TryGetBool",
        "NormalizeDetailNumber",
    ];

    [Fact]
    public void Each_bridge_reply_reader_is_defined_exactly_once()
    {
        var offences = new List<string>();

        foreach (var member in SharedReaders)
        {
            // A definition, not a call: a return type then the name then an open paren. Calls are
            // preceded by '.', '=' or whitespace-plus-argument, never by a type.
            var definition = new Regex(@"(?:static|private|internal|public|protected)[^\n=;]*\s" + Regex.Escape(member) + @"\s*\(", RegexOptions.Compiled);

            var definedIn = SourceFiles()
                .Where(path => definition.IsMatch(File.ReadAllText(path)))
                .Select(Path.GetFileName)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (definedIn.Count > 1)
            {
                offences.Add($"{member} is defined in {definedIn.Count} files: {string.Join(", ", definedIn)}");
            }
        }

        Assert.True(offences.Count == 0,
            "A bridge-reply reader has been copied. Move it to BridgeJson and import it with "
            + "'using static Kor.Operations.StandardDetails.BridgeJson;' instead."
            + Environment.NewLine + string.Join(Environment.NewLine, offences));
    }

    [Fact]
    public void What_counts_as_a_KOR_D_number_is_written_down_once()
    {
        // The pattern decides which views the publisher strips, which sheet slots the composer
        // thinks are occupied, and which views the intake offers as un-catalogued. Three readers,
        // one rule.
        var literal = new Regex(@"KOR-D-\\+d\{5\}", RegexOptions.Compiled);

        var files = SourceFiles()
            .Where(path => literal.IsMatch(File.ReadAllText(path)))
            .Select(Path.GetFileName)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.True(files.Count == 1,
            $"The KOR-D pattern is written in {files.Count} files ({string.Join(", ", files)}); it belongs in BridgeJson alone.");
        Assert.Equal("BridgeJson.cs", files[0]);
    }

    /// <summary>
    /// The StandardDetails folder, which is where every reader of a bridge reply lives. Scoped
    /// deliberately — see the class summary.
    /// </summary>
    private static IEnumerable<string> SourceFiles()
        => Directory.GetFiles(Path.Combine(AppRoot(), "StandardDetails"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                        && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                        && !path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
                        && !path.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Anchored on the csproj, not on a folder name: the first version of the padding gate matched
    /// the TEST project's own StandardDetails folder and passed over nothing at all.
    /// </summary>
    private static string AppRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Kor.Operations.App.csproj")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "Could not locate Kor.Operations.App.csproj from the test output directory.");
        return dir!.FullName;
    }
}

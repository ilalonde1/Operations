#nullable enable
using System.Text.Json;
using Kor.Operations.NetworkOps.Core.Learning;
using Kor.Operations.NetworkOps.Service.Store;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// A stored JSON list is written whole and read safely. 2026-10-02: Truncate(Serialize(list), 400) cut a long list of fixes
// through a "\u" escape and appended "…"; GET /api/resolutions (every device window loads it) then answered 500 -- "History
// failed to load" on every machine. The class: SERIALIZED JSON CUT TO A COLUMN'S SIZE.
//
// WHAT IT COVERS: the list written fits the column and parses, keeping the newest whole items; the exact corrupt value of
// that night reads as empty instead of throwing; a good value still reads; there is no other truncation of serialized JSON
// in the service.
// WHAT IT DOES NOT: the rows already corrupt in the database (migration 009 repairs those; Ian runs it).
// A SAME-CLASS FAULT IT WOULD NOT CATCH: JSON cut by SQL Server itself (a value longer than its column is an error, not a
// cut, with SqlParameter sizes as written -- so it would fail loudly, not corrupt).
public sealed class ResolutionJsonTests
{
    [Fact]
    public void A_long_list_is_stored_as_the_newest_whole_items_that_fit()
    {
        var items = Enumerable.Range(1, 60).Select(i => $"fix {i} install-updates on KOR-2{i:00} … Done").ToList();
        var json = NetworkOpsStore.JsonListWithin(items, NetworkOpsStore.ActionIdsMax);
        Assert.True(json.Length <= NetworkOpsStore.ActionIdsMax);
        var back = JsonSerializer.Deserialize<List<string>>(json)!;   // whole JSON, always
        Assert.NotEmpty(back);
        Assert.Equal(items[^1], back[^1]);                              // the newest kept
        Assert.Equal(items.Skip(items.Count - back.Count), back);      // a contiguous tail, nothing cut inside an item
    }

    [Fact]
    public void A_short_list_is_stored_as_it_is()
        => Assert.Equal(JsonSerializer.Serialize(new List<string> { "a", "b" }), NetworkOpsStore.JsonListWithin(["a", "b"], 400));

    [Fact]
    public void The_corrupt_value_of_that_night_reads_as_empty_and_a_good_one_still_reads()
    {
        // As written then: 399 characters of JSON cut inside "…", then the "…" Truncate appended.
        var corrupt = "[\"fix 172 install-updates Done\",\"fix 186 install-updates \\u" + "…";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<List<string>>(corrupt));
        Assert.Empty(NetworkOpsStore.ListOrEmpty<string>(corrupt));
        Assert.Equal(["a"], NetworkOpsStore.ListOrEmpty<string>("[\"a\"]"));
        Assert.Empty(NetworkOpsStore.ListOrEmpty<FactChange>(null));
    }

    [Fact]
    public void Nothing_in_the_service_truncates_serialized_JSON()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        var offenders = Directory.EnumerateFiles(Path.Combine(dir!.FullName, "Kor.Operations.NetworkOps.Service"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .SelectMany(f => File.ReadAllLines(f).Select((l, i) => (f, i, l)))
            .Where(x => System.Text.RegularExpressions.Regex.IsMatch(x.l, @"Truncate\(\s*JsonSerializer\.Serialize"))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}").ToList();
        Assert.True(offenders.Count == 0, "serialized JSON truncated (invalid JSON in the database): " + string.Join(", ", offenders));
    }
}

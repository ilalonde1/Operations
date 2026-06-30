using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Awards;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kor.Opportunities.Data.Tests;

public sealed class CanonicalOrgResolverTests
{
    private const string Source = "UnitTest.Source";

    [Fact]
    public async Task FuzzySurvivorAttach_ResolvesSuffixParenAndProjectVariantsToOneSurvivor()
    {
        var store = new FakeCanonicalOrgStore();
        var survivorId = store.AddOrg(
            "Read Jones",
            extraFuzzyKeys:
            [
                CanonicalOrgResolver.NormalizeForFuzzyMatch("Read Jones Inc."),
                CanonicalOrgResolver.NormalizeForFuzzyMatch("Read Jones (Read Jones)"),
                CanonicalOrgResolver.NormalizeForFuzzyMatch("Read Jones SD39 Project"),
            ]);
        var resolver = CreateResolver(store);

        var ids = new[]
        {
            await resolver.ResolveAsync("Read Jones", OrgKinds.Buyer, Source, CancellationToken.None),
            await resolver.ResolveAsync("Read Jones Inc.", OrgKinds.Buyer, Source, CancellationToken.None),
            await resolver.ResolveAsync("Read Jones (Read Jones)", OrgKinds.Buyer, Source, CancellationToken.None),
            await resolver.ResolveAsync("Read Jones SD39 Project", OrgKinds.Buyer, Source, CancellationToken.None),
        };

        Assert.All(ids, id => Assert.Equal(survivorId, id));
        Assert.Single(store.LiveOrgs);
    }

    [Fact]
    public async Task RetiredSameNameOrg_IsNeverReturnedOrUnretired()
    {
        var store = new FakeCanonicalOrgStore();
        var retiredId = store.AddOrg("Retired Same Name", retired: true);
        var resolver = CreateResolver(store);

        var resolved = await resolver.ResolveAsync("Retired Same Name", OrgKinds.Buyer, Source, CancellationToken.None);

        Assert.NotEqual(retiredId, resolved);
        Assert.NotNull(resolved);
        Assert.Contains(store.LiveOrgs, o => o.Id == resolved);
        Assert.True(store.Orgs.Single(o => o.Id == retiredId).Retired);
        Assert.Equal(0, store.UnretireCalls);
    }

    [Fact]
    public async Task DistinctShortFuzzyKeys_AreNotCollapsed()
    {
        var store = new FakeCanonicalOrgStore();
        var resolver = CreateResolver(store);

        var first = await resolver.ResolveAsync("ABC Inc", OrgKinds.Buyer, Source, CancellationToken.None);
        var second = await resolver.ResolveAsync("ABC LLC", OrgKinds.Buyer, Source, CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task FuzzySurvivorAttach_PrefersClendorAnchoredOrg()
    {
        var store = new FakeCanonicalOrgStore();
        store.AddOrg("Clendor Winner Ltd", id: 20, affiliationCount: 10, enrichmentCount: 10);
        var clendorId = store.AddOrg("Clendor Winner", id: 21, clendorClientId: "C123");
        var resolver = CreateResolver(store);

        var resolved = await resolver.ResolveAsync("Clendor Winner Inc.", OrgKinds.Buyer, Source, CancellationToken.None);

        Assert.Equal(clendorId, resolved);
    }

    [Fact]
    public async Task NormalizedNamePointingAtMergedLoser_ReturnsLiveSurvivor()
    {
        var store = new FakeCanonicalOrgStore();
        var survivorId = store.AddOrg("Live Survivor", id: 30);
        var loserId = store.AddOrg("Merged Loser", id: 31, retired: true);
        store.AddMerge(loserId, survivorId);
        var resolver = CreateResolver(store);

        var resolved = await resolver.ResolveAsync("Merged Loser", OrgKinds.Buyer, Source, CancellationToken.None);

        Assert.Equal(survivorId, resolved);
        Assert.Single(store.Aliases.Values.Where(a => a.CanonicalOrgId == survivorId));
    }

    [Fact]
    public async Task ResolveAsync_PromotesWebsiteAndNotesOnlyWhenCurrentValuesAreNull()
    {
        var store = new FakeCanonicalOrgStore();
        var orgId = store.AddOrg("Metadata Org", website: "https://old.example", notes: null);
        var resolver = CreateResolver(store);

        var resolved = await resolver.ResolveAsync(
            "Metadata Org",
            OrgKinds.Buyer,
            Source,
            CancellationToken.None,
            website: "https://new.example",
            notes: "first note");
        await resolver.ResolveAsync(
            "Metadata Org",
            OrgKinds.Buyer,
            Source,
            CancellationToken.None,
            website: "https://newer.example",
            notes: "second note");

        var org = store.Orgs.Single(o => o.Id == orgId);
        Assert.Equal(orgId, resolved);
        Assert.Equal("https://old.example", org.Website);
        Assert.Equal("first note", org.Notes);
    }

    [Fact]
    public async Task ResolveAsync_WebsiteDomainResolvesNameVariantToExistingOrg()
    {
        var store = new FakeCanonicalOrgStore();
        var existingId = store.AddOrg("Formline Architecture + Urbanism", website: "https://www.formline.ca/studio");
        var resolver = CreateResolver(store);

        var resolved = await resolver.ResolveAsync(
            "Formline Architecture",
            OrgKinds.Architect,
            Source,
            CancellationToken.None,
            website: "http://formline.ca/projects");

        Assert.Equal(existingId, resolved);
        Assert.Single(store.LiveOrgs);
    }

    [Fact]
    public async Task ResolveAsync_GenericSharedDomainDoesNotCollapseDistinctFirms()
    {
        var store = new FakeCanonicalOrgStore();
        var firstId = store.AddOrg("North Practice", website: "https://business.site/north");
        var resolver = CreateResolver(store);

        var resolved = await resolver.ResolveAsync(
            "South Practice",
            OrgKinds.Architect,
            Source,
            CancellationToken.None,
            website: "https://business.site/south");

        Assert.NotEqual(firstId, resolved);
        Assert.Equal(2, store.LiveOrgs.Count);
        Assert.Equal(0, store.FindByWebsiteDomainCalls);
    }

    [Fact]
    public async Task ResolveAsync_DomainMatchPrefersDeterministicSurvivor()
    {
        var store = new FakeCanonicalOrgStore();
        store.AddOrg("Domain Low Id", id: 40, website: "https://domain-match.test", affiliationCount: 1, enrichmentCount: 1);
        var winnerId = store.AddOrg("Domain Rich", id: 41, website: "https://www.domain-match.test/about", affiliationCount: 10, enrichmentCount: 5);
        var resolver = CreateResolver(store);

        var resolved = await resolver.ResolveAsync(
            "Unrelated Domain Name",
            OrgKinds.Architect,
            Source,
            CancellationToken.None,
            website: "https://domain-match.test/contact");

        Assert.Equal(winnerId, resolved);
    }

    [Fact]
    public async Task ResolveAsync_WithoutWebsiteDoesNotUseDomainMatch()
    {
        var store = new FakeCanonicalOrgStore();
        var existingId = store.AddOrg("Formline Architecture + Urbanism", website: "https://formline.ca");
        var resolver = CreateResolver(store);

        var resolved = await resolver.ResolveAsync("Formline Architecture", OrgKinds.Architect, Source, CancellationToken.None);

        Assert.NotEqual(existingId, resolved);
        Assert.Equal(2, store.LiveOrgs.Count);
        Assert.Equal(0, store.FindByWebsiteDomainCalls);
    }
    private static CanonicalOrgResolver CreateResolver(FakeCanonicalOrgStore store)
        => new(store, NullLogger<CanonicalOrgResolver>.Instance, resolverFuzzySurvivorAttach: true);

    private sealed class FakeCanonicalOrgStore : ICanonicalOrgStore
    {
        private long _nextOrgId = 1000;
        private long _nextAliasId = 1;
        private readonly Dictionary<long, long> _mergeLedger = new();
        private readonly Dictionary<long, HashSet<string>> _extraFuzzyKeys = new();

        public List<FakeOrg> Orgs { get; } = [];
        public IReadOnlyList<FakeOrg> LiveOrgs => Orgs.Where(o => !o.Retired).ToList();
        public Dictionary<(string RawName, string Source), OrgAliasRow> Aliases { get; } = [];
        public int UnretireCalls { get; private set; }
        public int FindByWebsiteDomainCalls { get; private set; }

        public long AddOrg(
            string displayName,
            long? id = null,
            string kind = OrgKinds.Buyer,
            string? clendorClientId = null,
            string? website = null,
            string? notes = null,
            bool retired = false,
            int affiliationCount = 0,
            int enrichmentCount = 0,
            IEnumerable<string>? extraFuzzyKeys = null,
            string? websiteDomain = null)
        {
            var org = new FakeOrg(
                id ?? _nextOrgId++,
                kind,
                displayName,
                clendorClientId,
                website,
                websiteDomain ?? CanonicalOrgResolver.ExtractWebsiteDomain(website),
                notes,
                retired,
                affiliationCount,
                enrichmentCount);
            Orgs.Add(org);
            if (extraFuzzyKeys is not null)
            {
                _extraFuzzyKeys[org.Id] = extraFuzzyKeys.Where(k => !string.IsNullOrWhiteSpace(k)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            return org.Id;
        }

        public void AddMerge(long loserId, long survivorId) => _mergeLedger[loserId] = survivorId;

        public Task<long> UpsertCanonicalOrgAsync(
            string kind,
            string displayName,
            string? clendorClientId,
            string? website,
            string? notes,
            CancellationToken ct)
        {
            var normalized = CanonicalOrgResolver.NormalizeName(displayName);
            var fuzzy = CanonicalOrgResolver.NormalizeForFuzzyMatch(displayName);
            var existing = SelectSurvivor(Orgs.Where(o => !o.Retired
                && ((clendorClientId is not null && o.ClendorClientId == clendorClientId)
                    || (clendorClientId is null && o.ClendorClientId is null && Normalize(o) == normalized))));
            if (existing is null && fuzzy.Length >= 8)
            {
                existing = SelectSurvivor(Orgs.Where(o => !o.Retired && HasFuzzyKey(o, fuzzy)));
            }

            if (existing is not null)
            {
                Promote(existing, website, notes);
                return Task.FromResult(existing.Id);
            }

            var id = AddOrg(displayName, kind: kind, clendorClientId: clendorClientId, website: website, notes: notes);
            return Task.FromResult(id);
        }

        public Task<CanonicalOrgRow?> GetCanonicalOrgAsync(long id, CancellationToken ct)
            => Task.FromResult(Orgs.Where(o => o.Id == id).Select(ToRow).FirstOrDefault());

        public Task<IReadOnlyList<CanonicalOrgRow>> SearchCanonicalOrgsAsync(string? query, string? kind, int take, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<CanonicalOrgRow>>(Orgs.Where(o => !o.Retired).Take(take).Select(ToRow).ToList());

        public Task<IReadOnlyList<CanonicalOrgRow>> SearchCanonicalOrgsWithRelationshipsAsync(string? query, string? kind, int take, CancellationToken ct)
            => SearchCanonicalOrgsAsync(query, kind, take, ct);

        public Task<CanonicalOrgRow?> GetCanonicalOrgByClendorIdAsync(string clendorClientId, CancellationToken ct)
            => Task.FromResult(Orgs.Where(o => o.ClendorClientId == clendorClientId).Select(ToRow).FirstOrDefault());

        public Task RecordBcRegistrySnapshotAsync(long canonicalOrgId, BcRegistrySnapshot snapshot, CancellationToken ct)
            => Task.CompletedTask;

        public Task<(string DisplayName, string Kind)?> GetNameAndKindAsync(long canonicalOrgId, CancellationToken ct)
        {
            var org = Orgs.SingleOrDefault(o => o.Id == canonicalOrgId);
            return Task.FromResult(org is null ? ((string DisplayName, string Kind)?)null : (org.DisplayName, org.Kind));
        }

        public Task<bool> UnretireAsync(long canonicalOrgId, string reason, CancellationToken ct)
        {
            UnretireCalls++;
            var org = Orgs.SingleOrDefault(o => o.Id == canonicalOrgId);
            if (org is null || !org.Retired) return Task.FromResult(false);
            org.Retired = false;
            return Task.FromResult(true);
        }

        public Task MarkRetiredOnIntakeAsync(long canonicalOrgId, string reason, CancellationToken ct)
        {
            var org = Orgs.Single(o => o.Id == canonicalOrgId);
            org.Retired = true;
            return Task.CompletedTask;
        }

        public Task<long?> FindByNormalizedNameAsync(string normalizedName, CancellationToken ct)
            => Task.FromResult(SelectSurvivor(Orgs.Where(o => !o.Retired && Normalize(o) == normalizedName))?.Id);

        public Task<long?> FindByFuzzyNormalizedNameAsync(string fuzzyKey, CancellationToken ct)
            => Task.FromResult(fuzzyKey.Length < 8
                ? null
                : SelectSurvivor(Orgs.Where(o => !o.Retired && HasFuzzyKey(o, fuzzyKey)))?.Id);

        public Task<long?> FindByWebsiteDomainAsync(string domain, CancellationToken ct)
        {
            FindByWebsiteDomainCalls++;
            if (CanonicalOrgResolver.IsGenericWebsiteDomainDenied(domain)) return Task.FromResult<long?>(null);
            return Task.FromResult(SelectSurvivor(Orgs.Where(o => !o.Retired
                && string.Equals(o.WebsiteDomain, domain, StringComparison.OrdinalIgnoreCase)))?.Id);
        }

        public Task<long?> ResolveCanonicalOrgMergeAsync(long canonicalOrgId, CancellationToken ct)
        {
            var current = canonicalOrgId;
            long? deepestLive = Orgs.Any(o => o.Id == current && !o.Retired) ? current : null;
            for (var depth = 0; depth < 16 && _mergeLedger.TryGetValue(current, out var next); depth++)
            {
                current = next;
                if (Orgs.Any(o => o.Id == current && !o.Retired)) deepestLive = current;
            }

            return Task.FromResult(deepestLive);
        }

        public async Task<long?> FindMergedSurvivorByAliasAsync(string rawName, string source, CancellationToken ct)
        {
            if (!Aliases.TryGetValue((rawName, source), out var alias) || !alias.CanonicalOrgId.HasValue) return null;
            var org = Orgs.SingleOrDefault(o => o.Id == alias.CanonicalOrgId.Value);
            return org is { Retired: true }
                ? await ResolveCanonicalOrgMergeAsync(org.Id, ct)
                : null;
        }

        public async Task<long?> FindMergedSurvivorByNormalizedNameAsync(string normalizedName, CancellationToken ct)
        {
            foreach (var org in Orgs.Where(o => o.Retired && Normalize(o) == normalizedName).OrderBy(o => o.Id))
            {
                var survivor = await ResolveCanonicalOrgMergeAsync(org.Id, ct);
                if (survivor.HasValue) return survivor;
            }

            return null;
        }

        public Task<int> CountRetiredMatchesAsync(string rawName, string source, string normalizedName, string fuzzyKey, CancellationToken ct)
        {
            var ids = Orgs.Where(o => o.Retired && (Normalize(o) == normalizedName || HasFuzzyKey(o, fuzzyKey)))
                .Select(o => o.Id)
                .ToHashSet();
            if (Aliases.TryGetValue((rawName, source), out var alias) && alias.CanonicalOrgId.HasValue)
            {
                var org = Orgs.SingleOrDefault(o => o.Id == alias.CanonicalOrgId.Value);
                if (org is { Retired: true }) ids.Add(org.Id);
            }

            return Task.FromResult(ids.Count);
        }

        public Task PromoteCanonicalOrgWebsiteNotesAsync(long canonicalOrgId, string? website, string? notes, CancellationToken ct)
        {
            var org = Orgs.Single(o => o.Id == canonicalOrgId);
            Promote(org, website, notes);
            return Task.CompletedTask;
        }

        public Task<long> UpsertAliasAsync(
            string rawName,
            string source,
            long? canonicalOrgId,
            int confidence,
            string? classifiedBy,
            string? notes,
            CancellationToken ct)
        {
            var key = (rawName, source);
            if (Aliases.TryGetValue(key, out var existing))
            {
                Aliases[key] = existing with
                {
                    CanonicalOrgId = canonicalOrgId ?? existing.CanonicalOrgId,
                    Confidence = canonicalOrgId.HasValue ? confidence : existing.Confidence,
                    ClassifiedBy = classifiedBy ?? existing.ClassifiedBy,
                    Notes = notes ?? existing.Notes,
                };
                return Task.FromResult(existing.Id);
            }

            var alias = new OrgAliasRow(
                _nextAliasId++,
                canonicalOrgId,
                rawName,
                source,
                confidence,
                classifiedBy,
                canonicalOrgId.HasValue ? DateTimeOffset.UtcNow : null,
                notes,
                DateTimeOffset.UtcNow);
            Aliases[key] = alias;
            return Task.FromResult(alias.Id);
        }

        public Task<OrgAliasRow?> LookupAliasAsync(string rawName, string source, CancellationToken ct)
        {
            if (!Aliases.TryGetValue((rawName, source), out var alias)) return Task.FromResult<OrgAliasRow?>(null);
            if (!alias.CanonicalOrgId.HasValue) return Task.FromResult<OrgAliasRow?>(alias);
            var org = Orgs.SingleOrDefault(o => o.Id == alias.CanonicalOrgId.Value);
            return Task.FromResult(org is { Retired: false } ? alias : null);
        }

        public Task<IReadOnlyList<OrgAliasRow>> ListUnclassifiedAsync(string? source, int batchSize, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<OrgAliasRow>>(Aliases.Values.Where(a => !a.CanonicalOrgId.HasValue).Take(batchSize).ToList());

        public Task<(int Total, int Classified, int Unclassified)> GetAliasCountsAsync(CancellationToken ct)
            => Task.FromResult((Aliases.Count, Aliases.Values.Count(a => a.CanonicalOrgId.HasValue), Aliases.Values.Count(a => !a.CanonicalOrgId.HasValue)));

        private static FakeOrg? SelectSurvivor(IEnumerable<FakeOrg> orgs)
            => orgs.OrderBy(o => o.Kind is OrgKinds.KorStructural or OrgKinds.KorClient ? 0 : 1)
                .ThenBy(o => o.ClendorClientId is not null ? 0 : 1)
                .ThenByDescending(o => o.AffiliationCount + o.EnrichmentCount)
                .ThenBy(o => o.Id)
                .FirstOrDefault();

        private bool HasFuzzyKey(FakeOrg org, string fuzzyKey)
            => Fuzzy(org) == fuzzyKey || (_extraFuzzyKeys.TryGetValue(org.Id, out var keys) && keys.Contains(fuzzyKey));

        private static string Normalize(FakeOrg org) => CanonicalOrgResolver.NormalizeName(org.DisplayName);
        private static string Fuzzy(FakeOrg org) => CanonicalOrgResolver.NormalizeForFuzzyMatch(org.DisplayName);

        private static void Promote(FakeOrg org, string? website, string? notes)
        {
            if (org.Website is null && !string.IsNullOrWhiteSpace(website)) org.Website = website;
            var domain = CanonicalOrgResolver.ExtractWebsiteDomain(website);
            if (org.WebsiteDomain is null && !string.IsNullOrWhiteSpace(domain)) org.WebsiteDomain = domain;
            if (org.Notes is null && !string.IsNullOrWhiteSpace(notes)) org.Notes = notes;
        }

        private static CanonicalOrgRow ToRow(FakeOrg org)
            => new(
                org.Id,
                org.Kind,
                org.DisplayName,
                org.ClendorClientId,
                org.Website,
                org.Notes,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                0,
                null,
                org.Retired ? DateTimeOffset.UtcNow : null);
    }

    private sealed class FakeOrg(
        long id,
        string kind,
        string displayName,
        string? clendorClientId,
        string? website,
        string? websiteDomain,
        string? notes,
        bool retired,
        int affiliationCount,
        int enrichmentCount)
    {
        public long Id { get; } = id;
        public string Kind { get; set; } = kind;
        public string DisplayName { get; set; } = displayName;
        public string? ClendorClientId { get; set; } = clendorClientId;
        public string? Website { get; set; } = website;
        public string? WebsiteDomain { get; set; } = string.IsNullOrWhiteSpace(websiteDomain) ? null : websiteDomain;
        public string? Notes { get; set; } = notes;
        public bool Retired { get; set; } = retired;
        public int AffiliationCount { get; set; } = affiliationCount;
        public int EnrichmentCount { get; set; } = enrichmentCount;
    }
}
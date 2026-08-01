#nullable enable

using Kor.Opportunities.Data.Intel;
using Xunit;

namespace Kor.Operations.Tests;

public sealed class IntelReadServiceTests
{
    [Fact]
    public async Task IntelReadService_orgWithIntel_returnsBundle()
    {
        var connectionString = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var service = new IntelReadService(connectionString);
        var bundle = await service.GetOrgIntelAsync(476, CancellationToken.None);

        Assert.NotEmpty(bundle.People);
        Assert.NotEmpty(bundle.Actions);
        Assert.All(
            bundle.People,
            p => Assert.True(Enum.IsDefined(typeof(IntelFreshness), p.Freshness)));
    }

    [Fact]
    public async Task IntelReadService_unknownOrg_returnsEmpty()
    {
        var connectionString = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var service = new IntelReadService(connectionString);
        var bundle = await service.GetOrgIntelAsync(-1, CancellationToken.None);

        Assert.Equal(
            0,
            bundle.People.Count + bundle.Actions.Count + bundle.Signals.Count
            + bundle.Works.Count + bundle.Risks.Count + bundle.Narratives.Count);
    }

    [Fact]
    public async Task IntelReadService_regionRollup_returnsTopActions()
    {
        var connectionString = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var service = new IntelReadService(connectionString);
        var rollup = await service.GetRegionIntelAsync("AB", "Calgary", CancellationToken.None);

        Assert.NotEmpty(rollup.TopActions);
    }
}

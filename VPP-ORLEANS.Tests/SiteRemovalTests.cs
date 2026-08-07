using VPP_ORLEANS.GrainInterfaces;
using Xunit;

namespace VPP_ORLEANS.Tests;

[Collection("Cluster")]
public class SiteRemovalTests
{
    private readonly ClusterFixture _fixture;

    public SiteRemovalTests(ClusterFixture fixture) => _fixture = fixture;

    private ISiteGrain Site(string title) => _fixture.Cluster.Client.GetGrain<ISiteGrain>(title);

    [Fact]
    public async Task RemoveAsset_UnlinksFromSite()
    {
        var site = Site("site-rem-1");
        await site.Add();

        var assetId = "batt-rem-1";
        await site.RegisterAsset(AssetKind.Battery, assetId);
        Assert.Contains(assetId, await site.GetAssetIds(AssetKind.Battery));

        await site.RemoveAsset(AssetKind.Battery, assetId);

        Assert.DoesNotContain(assetId, await site.GetAssetIds(AssetKind.Battery));
    }

    [Fact]
    public async Task Delete_ClearsSiteAndAssets()
    {
        var site = Site("site-rem-2");
        await site.Add();
        await site.RegisterAsset(AssetKind.Battery, "batt-rem-2a");
        await site.RegisterAsset(AssetKind.Solar, "solar-rem-2b");

        await site.Delete();

        var state = await site.Get();
        Assert.Empty(state.BatteryIds);
        Assert.Empty(state.SolarIds);
    }
}

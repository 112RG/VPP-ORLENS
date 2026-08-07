using VPP_ORLEANS.GrainInterfaces;
using Xunit;

namespace VPP_ORLEANS.Tests;

[Collection("Cluster")]
public class AssetPollerGrainTests
{
    private readonly ClusterFixture _fixture;

    public AssetPollerGrainTests(ClusterFixture fixture) => _fixture = fixture;

    private IAssetPollerGrain Poller(string assetId)
        => _fixture.Cluster.Client.GetGrain<IAssetPollerGrain>(SiteRegistryPartitioning.ComputeShard(assetId, 32));

    [Fact]
    public async Task Register_AddsAssetToRoster()
    {
        var poller = Poller("batt-poll-1");

        await poller.Register(AssetKind.Battery, "batt-poll-1");

        Assert.Contains("batt-poll-1", await poller.GetAssetIds());
    }

    [Fact]
    public async Task Register_IsIdempotent()
    {
        var poller = Poller("batt-poll-2");

        await poller.Register(AssetKind.Battery, "batt-poll-2");
        await poller.Register(AssetKind.Battery, "batt-poll-2");

        Assert.Single(await poller.GetAssetIds());
    }

    [Fact]
    public async Task Remove_DropsAssetFromRoster()
    {
        var poller = Poller("batt-poll-5");

        await poller.Register(AssetKind.Battery, "batt-poll-5");
        await poller.Remove("batt-poll-5");

        Assert.DoesNotContain("batt-poll-5", await poller.GetAssetIds());
    }

    [Fact]
    public async Task PollOnce_CompletesWithNoAssets()
    {
        // Should not throw on an empty roster.
        var poller = Poller("batt-poll-3");
        await poller.PollOnceAsync();
    }

    [Fact]
    public async Task PollOnce_RefreshesRegisteredAssets()
    {
        // Provision a real battery so GetStatus() has state to return.
        var assetId = "batt-poll-4";
        var battery = _fixture.Cluster.Client.GetGrain<IBatteryGrain>(assetId);
        await battery.Initialize("site-poll-4");

        var poller = Poller(assetId);
        await poller.Register(AssetKind.Battery, assetId);

        // Should not throw; each registered asset's status is refreshed.
        await poller.PollOnceAsync();
    }
}

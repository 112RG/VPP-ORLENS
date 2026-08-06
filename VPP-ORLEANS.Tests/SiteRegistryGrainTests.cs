using VPP_ORLEANS.GrainInterfaces;
using Xunit;

namespace VPP_ORLEANS.Tests;

[Collection("Cluster")]
public class SiteRegistryGrainTests
{
    private readonly ClusterFixture _fixture;

    public SiteRegistryGrainTests(ClusterFixture fixture) => _fixture = fixture;

    private ISiteRegistryGrain Registry(int shard) =>
        _fixture.Cluster.Client.GetGrain<ISiteRegistryGrain>(shard);

    [Fact]
    public async Task GetTitles_EmptyByDefault()
    {
        var page = await Registry(1000).GetTitles(0, 50);

        Assert.Empty(page.Titles);
        Assert.Equal(0, page.Total);
    }

    [Fact]
    public async Task Register_AddsTitle()
    {
        var registry = Registry(1001);

        await registry.Register("site-a");

        var page = await registry.GetTitles(0, 50);
        Assert.Equal(["site-a"], page.Titles);
        Assert.Equal(1, page.Total);
    }

    [Fact]
    public async Task Register_SameTitle_DoesNotDuplicate()
    {
        var registry = Registry(1002);

        await registry.Register("site-b");
        await registry.Register("site-b");

        var page = await registry.GetTitles(0, 50);
        Assert.Equal(["site-b"], page.Titles);
        Assert.Equal(1, page.Total);
    }

    [Fact]
    public async Task Register_ShardsIsolate()
    {
        await Registry(1003).Register("site-c");

        Assert.Equal(new[] { "site-c" }, (await Registry(1003).GetTitles(0, 50)).Titles);
        Assert.Empty((await Registry(1004).GetTitles(0, 50)).Titles);
    }

    [Fact]
    public async Task GetTitles_ReturnsSliceOfShard()
    {
        var registry = Registry(1005);
        foreach (var title in new[] { "s1", "s2", "s3", "s4", "s5" })
            await registry.Register(title);

        var page = await registry.GetTitles(1, 3);

        Assert.Equal(new[] { "s2", "s3", "s4" }, page.Titles);
        Assert.Equal(5, page.Total);
    }

    [Fact]
    public async Task GetTitles_SkipsBeyondEnd_ReturnsEmpty()
    {
        var registry = Registry(1006);
        await registry.Register("s1");

        var page = await registry.GetTitles(5, 3);

        Assert.Empty(page.Titles);
        Assert.Equal(1, page.Total);
    }
}
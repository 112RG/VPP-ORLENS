using VPP_ORLEANS.GrainInterfaces;
using Xunit;

namespace VPP_ORLEANS.Tests;

[Collection("Cluster")]
public class SiteGrainTests
{
    private readonly ClusterFixture _fixture;

    public SiteGrainTests(ClusterFixture fixture) => _fixture = fixture;

    private ISiteGrain Site(string title) => _fixture.Cluster.Client.GetGrain<ISiteGrain>(title);

    [Fact]
    public async Task NewGrain_ReturnsEmptyState()
    {
        var state = await Site("never-added").Get();

        Assert.Equal("", state.Title);
        Assert.False(state.IsActive);
    }

    [Fact]
    public async Task Add_SetsTitleAndActivates()
    {
        var grain = Site("solar-a1");

        await grain.Add();

        var state = await grain.Get();
        Assert.Equal("solar-a1", state.Title);
        Assert.True(state.IsActive);
    }

    [Fact]
    public async Task Toggle_FlipsActiveState()
    {
        var grain = Site("solar-b2");
        await grain.Add();

        await grain.Toggle();
        Assert.False((await grain.Get()).IsActive);

        await grain.Toggle();
        Assert.True((await grain.Get()).IsActive);
    }

    [Fact]
    public async Task Add_ThrowsWhenSiteAlreadyExists()
    {
        var grain = Site("solar-c3");
        await grain.Add();

        await Assert.ThrowsAsync<InvalidOperationException>(grain.Add);
    }
}

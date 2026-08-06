using VPP_ORLEANS.GrainInterfaces;
using Xunit;

namespace VPP_ORLEANS.Tests;

[Collection("Cluster")]
public class SiteRegistryGrainTests
{
    private readonly ClusterFixture _fixture;

    public SiteRegistryGrainTests(ClusterFixture fixture) => _fixture = fixture;

    private ISiteRegistryGrain Registry() =>
        _fixture.Cluster.Client.GetGrain<ISiteRegistryGrain>(Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GetAllTitles_EmptyByDefault()
    {
        var titles = await Registry().GetAllTitles();

        Assert.Empty(titles);
    }

    [Fact]
    public async Task Register_AddsTitle()
    {
        var registry = Registry();

        await registry.Register("solar-d4");

        Assert.Equal(["solar-d4"], await registry.GetAllTitles());
    }

    [Fact]
    public async Task Register_SameTitle_DoesNotDuplicate()
    {
        var registry = Registry();

        await registry.Register("solar-e5");
        await registry.Register("solar-e5");

        Assert.Equal(["solar-e5"], await registry.GetAllTitles());
    }

    [Fact]
    public async Task Register_MultipleTitles()
    {
        var registry = Registry();

        await registry.Register("solar-f6");
        await registry.Register("battery-g7");
        await registry.Register("ev-h8");

        var titles = await registry.GetAllTitles();

        Assert.Equal(
            new[] { "solar-f6", "battery-g7", "ev-h8" }.OrderBy(t => t).ToArray(),
            titles.OrderBy(t => t).ToArray());
    }
}
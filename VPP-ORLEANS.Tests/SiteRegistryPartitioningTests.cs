using VPP_ORLEANS.GrainInterfaces;
using Xunit;

namespace VPP_ORLEANS.Tests;

public class SiteRegistryPartitioningTests
{
    [Theory]
    [InlineData("site-a", 64)]
    [InlineData("battery-001", 64)]
    [InlineData("EV-Charging-99", 128)]
    [InlineData("solar/panel/zone-3", 16)]
    public void ComputeShard_Deterministic_AndInRange(string title, int shardCount)
    {
        int a = SiteRegistryPartitioning.ComputeShard(title, shardCount);
        int b = SiteRegistryPartitioning.ComputeShard(title, shardCount);

        Assert.Equal(a, b);
        Assert.InRange(a, 0, shardCount - 1);
    }

    [Fact]
    public void ComputeShard_DistributesAcrossShards()
    {
        var shards = Enumerable.Range(0, 1000)
            .Select(i => SiteRegistryPartitioning.ComputeShard($"der-{i}", 64))
            .ToHashSet();

        // Sanity check the mapping isn't collapsing everything to a single shard.
        Assert.True(shards.Count >= 8);
    }

    [Fact]
    public void ComputeShard_RejectsInvalidShardCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SiteRegistryPartitioning.ComputeShard("x", 0));
    }

    [Fact]
    public void BuildReadPlan_SingleShard()
    {
        var plan = SiteRegistryPartitioning.BuildReadPlan([10], 2, 3);

        var request = Assert.Single(plan);
        Assert.Equal(0, request.ShardId);
        Assert.Equal(2, request.Skip);
        Assert.Equal(3, request.Take);
    }

    [Fact]
    public void BuildReadPlan_SpansMultipleShards()
    {
        // shard0 [0,5), shard1 [5,10); global window [3,8)
        var plan = SiteRegistryPartitioning.BuildReadPlan([5, 5], 3, 5);

        Assert.Collection(plan,
            r => Assert.Equal(new ShardReadRequest(0, 3, 2), r),
            r => Assert.Equal(new ShardReadRequest(1, 0, 3), r));
    }

    [Fact]
    public void BuildReadPlan_SkipsWhollyEmptyShards()
    {
        var plan = SiteRegistryPartitioning.BuildReadPlan([0, 5, 0], 0, 2);

        var request = Assert.Single(plan);
        Assert.Equal(1, request.ShardId);
    }

    [Fact]
    public void BuildReadPlan_TakeZero_ReturnsEmpty()
    {
        Assert.Empty(SiteRegistryPartitioning.BuildReadPlan([5, 5], 0, 0));
    }

    [Fact]
    public void BuildReadPlan_PastEnd_ReturnsEmpty()
    {
        Assert.Empty(SiteRegistryPartitioning.BuildReadPlan([3, 3], 10, 5));
    }
}
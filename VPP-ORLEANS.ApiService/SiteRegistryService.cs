using Microsoft.Extensions.Options;
using VPP_ORLEANS.GrainInterfaces;

namespace VPP_ORLEANS.ApiService;

public sealed class SiteRegistryService(IClusterClient cluster, IOptions<SiteRegistryOptions> options)
{
    private int ShardCount => options.Value.ShardCount;

    public async Task RegisterAsync(string title)
    {
        int shard = SiteRegistryPartitioning.ComputeShard(title, ShardCount);
        await cluster.GetGrain<ISiteRegistryGrain>(shard).Register(title);
    }

    public async Task<(string[] Titles, int Total)> GetTitlesAsync(int page, int pageSize)
    {
        int shardCount = ShardCount;
        int skip = (page - 1) * pageSize;

        var counts = await Task.WhenAll(
            Enumerable.Range(0, shardCount)
                .Select(shard => cluster.GetGrain<ISiteRegistryGrain>(shard).GetTitles(0, 0)));

        int[] totals = counts.Select(c => c.Total).ToArray();
        var plan = SiteRegistryPartitioning.BuildReadPlan(totals, skip, pageSize);

        var parts = await Task.WhenAll(plan.Select(p =>
            cluster.GetGrain<ISiteRegistryGrain>(p.ShardId).GetTitles(p.Skip, p.Take)));

        return (parts.SelectMany(p => p.Titles).ToArray(), totals.Sum());
    }
}
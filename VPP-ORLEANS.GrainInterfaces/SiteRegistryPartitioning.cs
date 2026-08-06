namespace VPP_ORLEANS.GrainInterfaces;

public class SiteRegistryOptions
{
    public const string SectionName = "SiteRegistry";

    public int ShardCount { get; set; } = 64;
}

public readonly record struct ShardReadRequest(int ShardId, int Skip, int Take);

public static class SiteRegistryPartitioning
{
    public static int ComputeShard(string title, int shardCount)
    {
        if (shardCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(shardCount));

        return unchecked((int)(Fnv1a(title) % (ulong)shardCount));
    }

    /// <summary>
    /// Maps a global [skip, skip+take) window onto the shards that own those items.
    /// <paramref name="shardCounts"/> holds the item count of each shard in shard order.
    /// </summary>
    public static IReadOnlyList<ShardReadRequest> BuildReadPlan(int[] shardCounts, int skip, int take)
    {
        if (skip < 0)
            throw new ArgumentOutOfRangeException(nameof(skip));
        if (take < 0)
            throw new ArgumentOutOfRangeException(nameof(take));
        if (take == 0)
            return [];

        var requests = new List<ShardReadRequest>();
        int shardStart = 0;
        for (int shard = 0; shard < shardCounts.Length; shard++)
        {
            int shardCount = shardCounts[shard];
            int shardEnd = shardStart + shardCount;

            if (shardEnd > skip && shardStart < skip + take)
            {
                int localSkip = Math.Max(0, skip - shardStart);
                int localTake = Math.Min(shardCount - localSkip, skip + take - shardStart - localSkip);
                if (localTake > 0)
                    requests.Add(new ShardReadRequest(shard, localSkip, localTake));
            }

            shardStart = shardEnd;
        }

        return requests;
    }

    private static ulong Fnv1a(string text)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        ulong hash = offsetBasis;
        foreach (var c in text)
        {
            hash ^= c;
            hash *= prime;
        }

        return hash;
    }
}
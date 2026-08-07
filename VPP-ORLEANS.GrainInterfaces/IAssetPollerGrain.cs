namespace VPP_ORLEANS.GrainInterfaces;

public interface IAssetPollerGrain : IGrainWithIntegerKey
{
    Task Register(AssetKind kind, string assetId);
    Task Remove(string assetId);
    Task<string[]> GetAssetIds();
    Task PollOnceAsync();
}

[GenerateSerializer]
public record AssetRef
{
    [Id(0)] public AssetKind Kind { get; init; }
    [Id(1)] public string AssetId { get; init; } = "";
}

public class AssetPollerOptions
{
    public const string SectionName = "AssetPoller";

    public int ShardCount { get; set; } = 32;
    public int PollingIntervalSeconds { get; set; } = 30;
    public int MaxConcurrent { get; set; } = 100;
}

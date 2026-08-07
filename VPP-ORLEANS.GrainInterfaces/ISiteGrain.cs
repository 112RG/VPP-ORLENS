namespace VPP_ORLEANS.GrainInterfaces;

public interface ISiteGrain : IGrainWithStringKey
{
    Task Add();
    Task Toggle();
    Task<SiteState> Get();
    Task RegisterAsset(AssetKind kind, string assetId);
    Task<string[]> GetAssetIds(AssetKind kind);
    Task RemoveAsset(AssetKind kind, string assetId);
    Task Delete();
}

public interface ISiteRegistryGrain : IGrainWithIntegerKey
{
    Task Register(string title);
    Task Remove(string title);
    Task<SiteTitlePage> GetTitles(int skip, int take);
}

[GenerateSerializer]
public record SiteTitlePage
{
    [Id(0)] public string[] Titles { get; init; } = [];
    [Id(1)] public int Total { get; init; }
}

[GenerateSerializer]
public record SiteState
{
    [Id(0)] public string Title { get; init; } = "";
    [Id(1)] public bool IsActive { get; init; }
    [Id(2)] public string[] BatteryIds { get; init; } = [];
    [Id(3)] public string[] SolarIds { get; init; } = [];

    public SiteState Toggle() => this with { IsActive = !IsActive };

    public string[] AssetIds(AssetKind kind) =>
        kind switch
        {
            AssetKind.Battery => BatteryIds,
            AssetKind.Solar => SolarIds,
            _ => []
        };

    public SiteState RegisterAsset(AssetKind kind, string assetId) =>
        kind switch
        {
            AssetKind.Battery when !BatteryIds.Contains(assetId) => this with { BatteryIds = [.. BatteryIds, assetId] },
            AssetKind.Solar when !SolarIds.Contains(assetId) => this with { SolarIds = [.. SolarIds, assetId] },
            _ => this
        };

    public SiteState RemoveAsset(AssetKind kind, string assetId) =>
        kind switch
        {
            AssetKind.Battery => this with { BatteryIds = BatteryIds.Where(id => id != assetId).ToArray() },
            AssetKind.Solar => this with { SolarIds = SolarIds.Where(id => id != assetId).ToArray() },
            _ => this
        };
}
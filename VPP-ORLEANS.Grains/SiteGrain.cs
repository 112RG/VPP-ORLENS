using Microsoft.Extensions.Options;
using VPP_ORLEANS.GrainInterfaces;

namespace VPP_ORLEANS.Grains;

public class SiteGrain : Grain, ISiteGrain
{
    private readonly IPersistentState<SiteState> _state;
    private readonly IOptions<AssetPollerOptions> _pollerOptions;

    public SiteGrain(
        [PersistentState("site", "AdoNet")] IPersistentState<SiteState> state,
        IOptions<AssetPollerOptions> pollerOptions)
    {
        _state = state;
        _pollerOptions = pollerOptions;
    }

    public async Task Add()
    {
        if (!string.IsNullOrWhiteSpace(_state.State.Title))
            throw new InvalidOperationException($"Site '{this.GetPrimaryKeyString()}' already exists");

        _state.State = new SiteState { Title = this.GetPrimaryKeyString(), IsActive = true };
        await _state.WriteStateAsync();
    }

    public Task<SiteState> Get() => Task.FromResult(_state.State);

    public async Task RegisterAsset(AssetKind kind, string assetId)
    {
        var next = _state.State.RegisterAsset(kind, assetId);
        if (next != _state.State)
        {
            _state.State = next;
            await _state.WriteStateAsync();
        }
    }

    public Task<string[]> GetAssetIds(AssetKind kind) =>
        Task.FromResult(_state.State.AssetIds(kind));

    public async Task RemoveAsset(AssetKind kind, string assetId)
    {
        var next = _state.State.RemoveAsset(kind, assetId);
        if (next != _state.State)
        {
            _state.State = next;
            await _state.WriteStateAsync();
        }

        await DeleteAssetGrainAsync(kind, assetId);
    }

    public async Task Delete()
    {
        var state = _state.State;
        var kinds = new[] { (AssetKind.Battery, state.BatteryIds), (AssetKind.Solar, state.SolarIds) };

        foreach (var (kind, ids) in kinds)
            foreach (var id in ids)
                await DeleteAssetGrainAsync(kind, id);

        await _state.ClearStateAsync();
        this.DeactivateOnIdle();
    }

    private async Task DeleteAssetGrainAsync(AssetKind kind, string assetId)
    {
        IAssetGrain asset = kind switch
        {
            AssetKind.Battery => GrainFactory.GetGrain<IBatteryGrain>(assetId),
            AssetKind.Solar => GrainFactory.GetGrain<ISolarGrain>(assetId),
            _ => throw new InvalidOperationException($"Unsupported asset kind '{kind}'")
        };
        await asset.Delete();

        int shard = SiteRegistryPartitioning.ComputeShard(assetId, _pollerOptions.Value.ShardCount);
        await GrainFactory.GetGrain<IAssetPollerGrain>(shard).Remove(assetId);
    }

    public async Task Toggle()
    {
        _state.State = _state.State.Toggle();
        await _state.WriteStateAsync();
    }
}
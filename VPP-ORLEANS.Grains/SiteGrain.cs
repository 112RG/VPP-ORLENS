using VPP_ORLEANS.GrainInterfaces;

namespace VPP_ORLEANS.Grains;

public class SiteGrain : Grain, ISiteGrain
{
    private readonly IPersistentState<SiteState> _state;

    public SiteGrain([PersistentState("site", "AdoNet")] IPersistentState<SiteState> state)
    {
        _state = state;
    }

    public async Task Add()
    {
        if (!string.IsNullOrWhiteSpace(_state.State.Title))
            throw new InvalidOperationException($"Site '{this.GetPrimaryKeyString()}' already exists");

        _state.State = new SiteState { Title = this.GetPrimaryKeyString(), IsActive = true };
        await _state.WriteStateAsync();
    }

    public Task<SiteState> Get() => Task.FromResult(_state.State);

    public async Task Toggle()
    {
        _state.State = _state.State.Toggle();
        await _state.WriteStateAsync();
    }
}
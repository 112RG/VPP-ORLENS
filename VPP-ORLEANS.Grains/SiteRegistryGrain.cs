using VPP_ORLEANS.GrainInterfaces;

namespace VPP_ORLEANS.Grains;

public class SiteRegistryGrain : Grain, ISiteRegistryGrain
{
    private readonly IPersistentState<List<string>> _state;

    public SiteRegistryGrain([PersistentState("registry", "AdoNet")] IPersistentState<List<string>> state)
    {
        _state = state;
    }

    public async Task Register(string title)
    {
        if (!_state.State.Contains(title))
        {
            _state.State.Add(title);
            await _state.WriteStateAsync();
        }
    }

    public Task<string[]> GetAllTitles() =>
        Task.FromResult(_state.State.ToArray());
}
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

    public async Task Remove(string title)
    {
        if (_state.State.Remove(title))
            await _state.WriteStateAsync();
    }

    public Task<SiteTitlePage> GetTitles(int skip, int take)
    {
        var titles = _state.State;
        int total = titles.Count;

        if (skip >= total || take <= 0)
            return Task.FromResult(new SiteTitlePage { Total = total });

        int count = Math.Min(take, total - skip);

        return Task.FromResult(new SiteTitlePage
        {
            Titles = titles.Skip(skip).Take(count).ToArray(),
            Total = total
        });
    }
}
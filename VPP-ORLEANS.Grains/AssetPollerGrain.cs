using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VPP_ORLEANS.GrainInterfaces;

namespace VPP_ORLEANS.Grains;

public class AssetPollerGrain : Grain, IAssetPollerGrain
{
    private readonly IPersistentState<List<AssetRef>> _state;
    private readonly IOptions<AssetPollerOptions> _options;
    private readonly ILogger<AssetPollerGrain> _logger;
    private IGrainTimer? _timer;

    public AssetPollerGrain(
        [PersistentState("poller", "AdoNet")] IPersistentState<List<AssetRef>> state,
        IOptions<AssetPollerOptions> options,
        ILogger<AssetPollerGrain> logger)
    {
        _state = state;
        _options = options;
        _logger = logger;
    }

    public override Task OnActivateAsync(CancellationToken ct)
    {
        var options = _options.Value;
        _timer = this.RegisterGrainTimer(
            static (grain, timerCt) => grain.PollOnceAsync(),
            this,
            new GrainTimerCreationOptions
            {
                DueTime = TimeSpan.FromSeconds(options.PollingIntervalSeconds),
                Period = TimeSpan.FromSeconds(options.PollingIntervalSeconds),
                KeepAlive = true
            });

        return Task.CompletedTask;
    }

    public async Task Register(AssetKind kind, string assetId)
    {
        if (!_state.State.Any(a => a.AssetId == assetId))
        {
            _state.State.Add(new AssetRef { Kind = kind, AssetId = assetId });
            await _state.WriteStateAsync();
        }
    }

    public async Task Remove(string assetId)
    {
        if (_state.State.RemoveAll(a => a.AssetId == assetId) > 0)
            await _state.WriteStateAsync();
    }

    public Task<string[]> GetAssetIds() => Task.FromResult(_state.State.Select(a => a.AssetId).ToArray());

    public async Task PollOnceAsync()
    {
        var ids = _state.State;
        if (ids.Count == 0)
            return;

        int maxConcurrent = Math.Max(1, _options.Value.MaxConcurrent);
        int polled = 0;
        int errors = 0;

        using var semaphore = new SemaphoreSlim(maxConcurrent);
        var tasks = ids.Select(async assetRef =>
        {
            await semaphore.WaitAsync();
            try
            {
                IAssetGrain asset = assetRef.Kind switch
                {
                    AssetKind.Battery => GrainFactory.GetGrain<IBatteryGrain>(assetRef.AssetId),
                    AssetKind.Solar => GrainFactory.GetGrain<ISolarGrain>(assetRef.AssetId),
                    _ => throw new InvalidOperationException($"Unsupported asset kind '{assetRef.Kind}'")
                };
                await asset.GetStatus();
                Interlocked.Increment(ref polled);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref errors);
                _logger.LogWarning(ex, "AssetPoller failed to poll asset {AssetId}", assetRef.AssetId);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        if (errors > 0)
            _logger.LogWarning("AssetPoller shard {Shard} polled {Polled} assets with {Errors} errors",
                this.GetPrimaryKeyLong(), polled, errors);
    }
}

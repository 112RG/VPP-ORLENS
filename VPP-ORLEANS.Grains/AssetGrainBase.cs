using Microsoft.Extensions.Options;
using VPP_ORLEANS.GrainInterfaces;

namespace VPP_ORLEANS.Grains;

public abstract class AssetGrainBase<TState> : Grain, IAssetGrain
    where TState : class, IAssetState, new()
{
    private readonly IPersistentState<TState> _state;
    private readonly IProtocolAdapter _protocol;
    private readonly IOptions<AssetOptions> _options;
    private IGrainTimer? _telemetryTimer;

    protected IPersistentState<TState> State => _state;
    protected IProtocolAdapter Protocol => _protocol;

    protected AssetGrainBase(
        IPersistentState<TState> state,
        IProtocolAdapter protocol,
        IOptions<AssetOptions> options)
    {
        _state = state;
        _protocol = protocol;
        _options = options;
    }

    protected abstract AssetKind Kind { get; }

    public override Task OnActivateAsync(CancellationToken ct)
    {
        var asset = _options.Value;
        _telemetryTimer = this.RegisterGrainTimer(
            static (grain, timerCt) => grain.PollHardwareAsync(),
            this,
            new GrainTimerCreationOptions
            {
                DueTime = TimeSpan.FromSeconds(asset.TelemetryIntervalSeconds),
                Period = TimeSpan.FromSeconds(asset.TelemetryIntervalSeconds),
                KeepAlive = true
            });

        return Task.CompletedTask;
    }

    public virtual async Task Initialize(string siteId)
    {
        if (string.IsNullOrWhiteSpace(State.State.SiteId))
        {
            State.State.SiteId = siteId;
            OnInitialized();
            await State.WriteStateAsync();
        }
    }

    protected virtual void OnInitialized()
    {
    }

    public Task<AssetStatus> GetStatus()
    {
        var asset = State.State;
        var online = DateTimeOffset.UtcNow - asset.LastTelemetryUtc
                     <= TimeSpan.FromSeconds(_options.Value.OnlineStalenessSeconds);

        return Task.FromResult(new AssetStatus
        {
            AssetId = this.GetPrimaryKeyString(),
            Kind = Kind,
            SiteId = asset.SiteId,
            CurrentKw = GetCurrentKw(asset),
            IsOnline = online,
            LastTelemetryUtc = asset.LastTelemetryUtc
        });
    }

    protected abstract double GetCurrentKw(TState state);

    private async Task PollHardwareAsync()
    {
        var sample = await ReadHardwareAsync();
        if (sample is null)
            return;

        ApplySample(sample.Value);
        State.State.LastTelemetryUtc = DateTimeOffset.UtcNow;
        await State.WriteStateAsync();
        await ReconcileAsync(sample.Value.PhysicalKw);
    }

    protected abstract Task<HardwareSample?> ReadHardwareAsync();

    protected abstract void ApplySample(HardwareSample sample);

    protected virtual Task ReconcileAsync(double physicalKw) => Task.CompletedTask;
}
using Microsoft.Extensions.Options;
using VPP_ORLEANS.GrainInterfaces;

namespace VPP_ORLEANS.Grains;

public class BatteryGrain : AssetGrainBase<BatteryState>, IBatteryGrain
{
    public BatteryGrain(
        [PersistentState("battery", "AdoNet")] IPersistentState<BatteryState> state,
        IProtocolAdapter protocol,
        IOptions<AssetOptions> options)
        : base(state, protocol, options)
    {
    }

    protected override AssetKind Kind => AssetKind.Battery;

    protected override void OnInitialized()
    {
        if (State.State.SocPercent == 0)
            State.State.SocPercent = 50;
    }

    protected override Task InitializeProtocolAsync()
    {
        var state = State.State;
        return Protocol.SeedBatteryAsync(
            this.GetPrimaryKeyString(),
            state.CapacityKwh > 0 ? state.CapacityKwh : 13.5,
            state.SocPercent > 0 ? state.SocPercent : 50);
    }

    public Task ReportTelemetry(BatteryTelemetry telemetry)
    {
        State.State.SocPercent = telemetry.SocPercent;
        State.State.CurrentKw = telemetry.CurrentKw;
        State.State.LastTelemetryUtc = telemetry.TimestampUtc;
        return State.WriteStateAsync();
    }

    public async Task SetDesiredPowerKw(double kw)
    {
        State.State.DesiredKw = kw;
        await State.WriteStateAsync();
        await Protocol.SendBatteryCommandAsync(this.GetPrimaryKeyString(), kw);
    }

    public Task<double> GetAvailableCapacityKwh() =>
        Task.FromResult(State.State.GetAvailableCapacityKwh());

    public Task<BatteryState> GetState() => Task.FromResult(State.State);

    protected override double GetCurrentKw(BatteryState state) => state.CurrentKw;

    protected override async Task<HardwareSample?> ReadHardwareAsync()
    {
        var hardware = await Protocol.ReadBatteryAsync(this.GetPrimaryKeyString());
        return hardware is null ? null : new HardwareSample(hardware.ActualKw, hardware.SocPercent);
    }

    protected override void ApplySample(HardwareSample sample)
    {
        if (sample.SocPercent is double soc)
            State.State.SocPercent = soc;

        State.State.CurrentKw = sample.PhysicalKw;
    }

    protected override async Task ReconcileAsync(double physicalKw)
    {
        var desired = State.State.DesiredKw;
        if (desired != 0 && Math.Abs(physicalKw - desired) > 0.5)
            await Protocol.SendBatteryCommandAsync(this.GetPrimaryKeyString(), desired);
    }
}
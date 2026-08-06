using Microsoft.Extensions.Options;
using VPP_ORLEANS.GrainInterfaces;

namespace VPP_ORLEANS.Grains;

public class SolarGrain : AssetGrainBase<SolarState>, ISolarGrain
{
    public SolarGrain(
        [PersistentState("solar", "AdoNet")] IPersistentState<SolarState> state,
        IProtocolAdapter protocol,
        IOptions<AssetOptions> options)
        : base(state, protocol, options)
    {
    }

    protected override AssetKind Kind => AssetKind.Solar;

    public Task ReportTelemetry(SolarTelemetry telemetry)
    {
        State.State.GenerationKw = telemetry.GenerationKw;
        State.State.LastTelemetryUtc = telemetry.TimestampUtc;
        return State.WriteStateAsync();
    }

    public Task<SolarState> GetState() => Task.FromResult(State.State);

    protected override double GetCurrentKw(SolarState state) => state.GenerationKw;

    protected override async Task<HardwareSample?> ReadHardwareAsync()
    {
        var hardware = await Protocol.ReadSolarAsync(this.GetPrimaryKeyString());
        return hardware is null ? null : new HardwareSample(hardware.GenerationKw, null);
    }

    protected override void ApplySample(HardwareSample sample)
        => State.State.GenerationKw = sample.PhysicalKw;
}
using System.Collections.Concurrent;

namespace VPP_ORLEANS.Grains;

public sealed class SimulatedProtocolAdapter : IProtocolAdapter
{
    public const double DefaultCapacityKwh = 13.5;
    private const double TimestepHours = 0.0125;

    private readonly ConcurrentDictionary<string, BatterySim> _batteries = new();
    private readonly ConcurrentDictionary<string, SolarSim> _solar = new();
    private readonly double _capacityKwh;

    public SimulatedProtocolAdapter(double capacityKwh = DefaultCapacityKwh) => _capacityKwh = capacityKwh;

    public string Name => "Simulated";

    public Task<BatteryHardware?> ReadBatteryAsync(string assetId)
    {
        var sim = _batteries.GetOrAdd(assetId, static _ => new BatterySim());
        sim.Step(_capacityKwh);
        return Task.FromResult<BatteryHardware?>(new BatteryHardware(sim.Soc, sim.ActualKw));
    }

    public Task<SolarHardware?> ReadSolarAsync(string assetId)
    {
        var sim = _solar.GetOrAdd(assetId, static _ => new SolarSim());
        return Task.FromResult<SolarHardware?>(new SolarHardware(sim.Read()));
    }

    public Task SendBatteryCommandAsync(string assetId, double desiredKw)
    {
        var sim = _batteries.GetOrAdd(assetId, static _ => new BatterySim());
        sim.CommandKw = desiredKw;
        return Task.CompletedTask;
    }

    private sealed class BatterySim
    {
        public double Soc = 50;
        public double CommandKw;
        public double ActualKw;

        public void Step(double capacityKwh)
        {
            ActualKw = CommandKw;

            double energyKwh = Math.Abs(CommandKw) * TimestepHours;
            if (CommandKw > 0.001)
                Soc = Math.Max(5, Soc - energyKwh / capacityKwh * 100);
            else if (CommandKw < -0.001)
                Soc = Math.Min(100, Soc + energyKwh / capacityKwh * 100);
        }
    }

    private sealed class SolarSim
    {
        public const double PeakKw = 5;
        private int _step;

        public double Read()
        {
            double gen = PeakKw * (0.5 + 0.5 * Math.Sin(_step / 12.0));
            _step += 13;
            return Math.Max(0, gen);
        }
    }
}
namespace VPP_ORLEANS.Grains;

public interface IProtocolAdapter
{
    string Name { get; }
    Task<BatteryHardware?> ReadBatteryAsync(string assetId);
    Task<SolarHardware?> ReadSolarAsync(string assetId);
    Task SendBatteryCommandAsync(string assetId, double desiredKw);
    Task SeedBatteryAsync(string assetId, double capacityKwh, double socPercent);
}

public record BatteryHardware(double SocPercent, double ActualKw);

public record SolarHardware(double GenerationKw);

public readonly record struct HardwareSample(double PhysicalKw, double? SocPercent);
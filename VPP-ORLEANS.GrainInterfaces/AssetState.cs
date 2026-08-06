namespace VPP_ORLEANS.GrainInterfaces;

public enum AssetKind
{
    Battery = 0,
    Solar = 1
}

public interface IAssetState
{
    string SiteId { get; set; }
    DateTimeOffset LastTelemetryUtc { get; set; }
}

public class AssetOptions
{
    public const string SectionName = "Asset";

    public int TelemetryIntervalSeconds { get; set; } = 30;
    public int OnlineStalenessSeconds { get; set; } = 60;
}

[GenerateSerializer]
public record AssetStatus
{
    [Id(0)] public string AssetId { get; init; } = "";
    [Id(1)] public AssetKind Kind { get; init; }
    [Id(2)] public string SiteId { get; init; } = "";
    [Id(3)] public double CurrentKw { get; init; }
    [Id(4)] public bool IsOnline { get; init; }
    [Id(5)] public DateTimeOffset LastTelemetryUtc { get; init; }
}

[GenerateSerializer]
public record BatteryTelemetry
{
    [Id(0)] public double SocPercent { get; init; }
    [Id(1)] public double CurrentKw { get; init; }
    [Id(2)] public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}

[GenerateSerializer]
public record SolarTelemetry
{
    [Id(0)] public double GenerationKw { get; init; }
    [Id(1)] public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}

[GenerateSerializer]
public record BatteryState : IAssetState
{
    [Id(0)] public string SiteId { get; set; } = "";
    [Id(1)] public double SocPercent { get; set; }
    [Id(2)] public double CurrentKw { get; set; }
    [Id(3)] public double DesiredKw { get; set; }
    [Id(4)] public double CapacityKwh { get; set; } = 13.5;
    [Id(5)] public double ReserveFloorPercent { get; set; } = 20;
    [Id(6)] public DateTimeOffset LastTelemetryUtc { get; set; }

    public double GetAvailableCapacityKwh() =>
        CapacityKwh * Math.Max(0, SocPercent - ReserveFloorPercent) / 100.0;
}

[GenerateSerializer]
public record SolarState : IAssetState
{
    [Id(0)] public string SiteId { get; set; } = "";
    [Id(1)] public double GenerationKw { get; set; }
    [Id(2)] public DateTimeOffset LastTelemetryUtc { get; set; }
}
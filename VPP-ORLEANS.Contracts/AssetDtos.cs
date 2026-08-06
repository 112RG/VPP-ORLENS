using VPP_ORLEANS.GrainInterfaces;

namespace VPP_ORLEANS.Contracts;

public record AddAssetRequest(AssetKind Kind, string AssetId);

public record DispatchBatteryRequest(double DesiredKw);

public record AssetItem
{
    public string AssetId { get; init; } = "";
    public AssetKind Kind { get; init; }
    public string SiteId { get; init; } = "";
    public double CurrentKw { get; init; }
    public bool IsOnline { get; init; }
    public DateTimeOffset LastTelemetryUtc { get; init; }
}

public record AssetListResponse
{
    public AssetItem[] Assets { get; init; } = [];
}

public record BatteryInfo
{
    public string AssetId { get; init; } = "";
    public string SiteId { get; init; } = "";
    public double SocPercent { get; init; }
    public double CurrentKw { get; init; }
    public double DesiredKw { get; init; }
    public double CapacityKwh { get; init; }
    public double ReserveFloorPercent { get; init; }
    public double AvailableCapacityKwh { get; init; }
    public bool IsOnline { get; init; }
    public DateTimeOffset LastTelemetryUtc { get; init; }
}

public record SolarInfo
{
    public string AssetId { get; init; } = "";
    public string SiteId { get; init; } = "";
    public double GenerationKw { get; init; }
    public bool IsOnline { get; init; }
    public DateTimeOffset LastTelemetryUtc { get; init; }
}
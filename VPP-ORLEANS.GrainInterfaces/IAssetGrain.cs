namespace VPP_ORLEANS.GrainInterfaces;

public interface IAssetGrain : IGrainWithStringKey
{
    Task Initialize(string siteId);
    Task<AssetStatus> GetStatus();
    Task Delete();
}

public interface IBatteryGrain : IAssetGrain
{
    Task ReportTelemetry(BatteryTelemetry telemetry);
    Task SetDesiredPowerKw(double kw);
    Task<double> GetAvailableCapacityKwh();
    Task<BatteryState> GetState();
}

public interface ISolarGrain : IAssetGrain
{
    Task ReportTelemetry(SolarTelemetry telemetry);
    Task<SolarState> GetState();
}
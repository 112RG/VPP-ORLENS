using Microsoft.Extensions.Options;
using VPP_ORLEANS.ApiService;
using VPP_ORLEANS.Contracts;
using VPP_ORLEANS.GrainInterfaces;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();

builder.UseOrleansClient(client =>
{
    var connectionString = builder.Configuration.GetConnectionString("orleans-db")
        ?? throw new InvalidOperationException("Connection string 'orleans-db' not found");

    client.UseAdoNetClustering(options =>
    {
        options.Invariant = "Npgsql";
        options.ConnectionString = connectionString;
    });
});

builder.Services.Configure<SiteRegistryOptions>(
    builder.Configuration.GetSection(SiteRegistryOptions.SectionName));
builder.Services.AddSingleton<SiteRegistryService>();

builder.Services.Configure<AssetPollerOptions>(
    builder.Configuration.GetSection(AssetPollerOptions.SectionName));

var app = builder.Build();

app.UseExceptionHandler();

app.MapGet("/", () => "API service is running.");

app.MapGet("/site", async (int page, int pageSize, IClusterClient cluster, SiteRegistryService registry) =>
{
    page = page < 1 ? 1 : page;
    pageSize = pageSize is < 1 or > 200 ? 50 : pageSize;

    var (titles, total) = await registry.GetTitlesAsync(page, pageSize);

    var states = await Task.WhenAll(titles.Select(async title =>
    {
        var grain = cluster.GetGrain<ISiteGrain>(title);
        return await grain.Get();
    }));

    return new SiteResponse
    {
        Sites = states
            .Where(s => !string.IsNullOrWhiteSpace(s.Title))
            .Select(s => new SiteItem { Title = s.Title, IsActive = s.IsActive })
            .ToArray(),
        Total = total,
        Page = page,
        PageSize = pageSize
    };
});

app.MapPost("/site", async (AddSiteRequest req, IClusterClient cluster, SiteRegistryService registry) =>
{
    await registry.RegisterAsync(req.Title);

    var grain = cluster.GetGrain<ISiteGrain>(req.Title);
    await grain.Add();

    var state = await grain.Get();
    return new SiteResponse
    {
        Sites = [new SiteItem { Title = state.Title, IsActive = state.IsActive }]
    };
});

app.MapPut("/site/{title}/toggle", async (string title, IClusterClient cluster) =>
{
    var grain = cluster.GetGrain<ISiteGrain>(title);
    await grain.Toggle();

    var state = await grain.Get();
    return new SiteResponse
    {
        Sites = [new SiteItem { Title = state.Title, IsActive = state.IsActive }]
    };
});

app.MapDelete("/site/{title}", async (string title, IClusterClient cluster, SiteRegistryService registry) =>
{
    await cluster.GetGrain<ISiteGrain>(title).Delete();
    await registry.RemoveAsync(title);
    return Results.NoContent();
});

app.MapDelete("/site/{site}/assets/{kind}/{assetId}", async (string site, AssetKind kind, string assetId, IClusterClient cluster) =>
{
    var siteGrain = cluster.GetGrain<ISiteGrain>(site);
    await siteGrain.RemoveAsset(kind, assetId);
    return Results.NoContent();
});

app.MapPost("/site/{site}/assets", async (string site, AddAssetRequest req, IClusterClient cluster, IOptions<AssetPollerOptions> pollerOptions) =>
{
    await cluster.GetGrain<ISiteGrain>(site).RegisterAsset(req.Kind, req.AssetId);

    IAssetGrain asset = ResolveAsset(req.Kind, req.AssetId, cluster);
    await asset.Initialize(site);

    int shard = SiteRegistryPartitioning.ComputeShard(req.AssetId, pollerOptions.Value.ShardCount);
    await cluster.GetGrain<IAssetPollerGrain>(shard).Register(req.Kind, req.AssetId);

    var status = await asset.GetStatus();
    return ToItem(status);
});

app.MapGet("/site/{site}/assets", async (string site, IClusterClient cluster) =>
{
    var siteGrain = cluster.GetGrain<ISiteGrain>(site);
    var batteryIds = await siteGrain.GetAssetIds(AssetKind.Battery);
    var solarIds = await siteGrain.GetAssetIds(AssetKind.Solar);

    var ids = batteryIds
        .Select(id => (Kind: AssetKind.Battery, Id: id))
        .Concat(solarIds.Select(id => (Kind: AssetKind.Solar, Id: id)))
        .ToArray();

    var statuses = await Task.WhenAll(ids.Select(k =>
        ResolveAsset(k.Kind, k.Id, cluster).GetStatus()));

    return new AssetListResponse { Assets = statuses.Select(ToItem).ToArray() };
});

app.MapGet("/assets/battery/{id}", async (string id, IClusterClient cluster) =>
{
    var battery = cluster.GetGrain<IBatteryGrain>(id);
    var state = await battery.GetState();
    var status = await battery.GetStatus();

    return new BatteryInfo
    {
        AssetId = id,
        SiteId = state.SiteId,
        SocPercent = state.SocPercent,
        CurrentKw = state.CurrentKw,
        DesiredKw = state.DesiredKw,
        CapacityKwh = state.CapacityKwh,
        ReserveFloorPercent = state.ReserveFloorPercent,
        AvailableCapacityKwh = state.GetAvailableCapacityKwh(),
        IsOnline = status.IsOnline,
        LastTelemetryUtc = status.LastTelemetryUtc
    };
});

app.MapGet("/assets/solar/{id}", async (string id, IClusterClient cluster) =>
{
    var solar = cluster.GetGrain<ISolarGrain>(id);
    var state = await solar.GetState();
    var status = await solar.GetStatus();

    return new SolarInfo
    {
        AssetId = id,
        SiteId = state.SiteId,
        GenerationKw = state.GenerationKw,
        IsOnline = status.IsOnline,
        LastTelemetryUtc = status.LastTelemetryUtc
    };
});

app.MapPost("/assets/battery/{id}/dispatch", async (string id, DispatchBatteryRequest req, IClusterClient cluster) =>
{
    var battery = cluster.GetGrain<IBatteryGrain>(id);
    await battery.SetDesiredPowerKw(req.DesiredKw);

    var state = await battery.GetState();
    var status = await battery.GetStatus();

    return new BatteryInfo
    {
        AssetId = id,
        SiteId = state.SiteId,
        SocPercent = state.SocPercent,
        CurrentKw = state.CurrentKw,
        DesiredKw = state.DesiredKw,
        CapacityKwh = state.CapacityKwh,
        ReserveFloorPercent = state.ReserveFloorPercent,
        AvailableCapacityKwh = state.GetAvailableCapacityKwh(),
        IsOnline = status.IsOnline,
        LastTelemetryUtc = status.LastTelemetryUtc
    };
});

app.MapDefaultEndpoints();

static IAssetGrain ResolveAsset(AssetKind kind, string assetId, IClusterClient cluster) =>
    kind switch
    {
        AssetKind.Battery => cluster.GetGrain<IBatteryGrain>(assetId),
        AssetKind.Solar => cluster.GetGrain<ISolarGrain>(assetId),
        _ => throw new InvalidOperationException($"Unsupported asset kind '{kind}'")
    };

static AssetItem ToItem(AssetStatus status) => new()
{
    AssetId = status.AssetId,
    Kind = status.Kind,
    SiteId = status.SiteId,
    CurrentKw = status.CurrentKw,
    IsOnline = status.IsOnline,
    LastTelemetryUtc = status.LastTelemetryUtc
};

app.Run();
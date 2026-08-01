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

var app = builder.Build();

app.UseExceptionHandler();

app.MapGet("/", () => "API service is running.");

app.MapGet("/site", async (IClusterClient cluster) =>
{
    var registry = cluster.GetGrain<ISiteRegistryGrain>("default");
    var titles = await registry.GetAllTitles();

    var grains = titles.Select(t => cluster.GetGrain<ISiteGrain>(t));
    var states = await Task.WhenAll(grains.Select(g => g.Get()));

    return new SiteResponse
    {
        Sites = states
            .Where(s => !string.IsNullOrWhiteSpace(s.Title))
            .Select(s => new SiteItem { Title = s.Title, IsActive = s.IsActive })
            .ToArray()
    };
});

app.MapPost("/site", async (AddSiteRequest req, IClusterClient cluster) =>
{
    var registry = cluster.GetGrain<ISiteRegistryGrain>("default");
    await registry.Register(req.Title);

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

app.MapDefaultEndpoints();

app.Run();
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

app.MapDefaultEndpoints();

app.Run();
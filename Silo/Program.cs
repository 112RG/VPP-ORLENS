using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using System.Reflection;
using VPP_ORLEANS.GrainInterfaces;
using VPP_ORLEANS.Grains;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddSingleton<IProtocolAdapter, SimulatedProtocolAdapter>();
builder.Services.AddOptions<AssetOptions>().BindConfiguration(AssetOptions.SectionName);
builder.Services.AddOptions<AssetPollerOptions>().BindConfiguration(AssetPollerOptions.SectionName);

var connectionString = builder.Configuration.GetConnectionString("orleans-db")
    ?? throw new InvalidOperationException("Connection string 'orleans-db' not found");

await BootstrapDatabaseAsync(connectionString);

builder.UseOrleans(silo =>
{
    silo.UseAdoNetClustering(options =>
    {
        options.Invariant = "Npgsql";
        options.ConnectionString = connectionString;
    });

    silo.AddAdoNetGrainStorage("AdoNet", options =>
    {
        options.Invariant = "Npgsql";
        options.ConnectionString = connectionString;
    });
});

using IHost host = builder.Build();

await host.RunAsync();

static async Task BootstrapDatabaseAsync(string connectionString)
{
    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();

    await using var check = conn.CreateCommand();
    check.CommandText = "SELECT EXISTS (SELECT FROM pg_tables WHERE tablename = 'orleansquery')";
    var hasClustering = (bool)(await check.ExecuteScalarAsync() ?? false);

    if (!hasClustering)
        await ExecuteScriptAsync(conn, "VPP_ORLEANS.Silo.sql.Clustering.sql");

    check.CommandText = "SELECT EXISTS (SELECT FROM pg_tables WHERE tablename = 'orleansstorage')";
    var hasStorage = (bool)(await check.ExecuteScalarAsync() ?? false);

    if (!hasStorage)
        await ExecuteScriptAsync(conn, "VPP_ORLEANS.Silo.sql.Storage.sql");
}

static async Task ExecuteScriptAsync(NpgsqlConnection conn, string resourceName)
{
    await using var stream = Assembly.GetExecutingAssembly()
        .GetManifestResourceStream(resourceName)
        ?? throw new InvalidOperationException($"Embedded SQL resource '{resourceName}' not found");

    using var reader = new StreamReader(stream);
    var script = await reader.ReadToEndAsync();

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = script;
    await cmd.ExecuteNonQueryAsync();
}

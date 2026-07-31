var builder = DistributedApplication.CreateBuilder(args);

var pg = builder.AddPostgres("pg")
    .WithDataVolume()
    .AddDatabase("orleans-db");

var apiService = builder.AddProject<Projects.VPP_ORLEANS_ApiService>("apiservice")
    .WithEnvironment("Orleans__EnableDistributedTracing", "true")
    .WithReference(pg)
    .WaitFor(pg)
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.VPP_ORLEANS_Silo>("silo")
    .WithEnvironment("Orleans__EnableDistributedTracing", "true")
    .WithReference(pg)
    .WaitFor(pg);

builder.AddProject<Projects.VPP_ORLEANS_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
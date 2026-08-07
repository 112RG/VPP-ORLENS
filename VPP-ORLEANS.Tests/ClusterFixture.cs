using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Hosting;
using Orleans.TestingHost;
using VPP_ORLEANS.GrainInterfaces;
using VPP_ORLEANS.Grains;
using Xunit;

namespace VPP_ORLEANS.Tests;

[CollectionDefinition("Cluster", DisableParallelization = true)]
public sealed class ClusterCollection : ICollectionFixture<ClusterFixture>;

public class TestSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder.AddMemoryGrainStorage("AdoNet");
        siloBuilder.ConfigureServices(services =>
        {
            services.AddSingleton<IProtocolAdapter, SimulatedProtocolAdapter>();
            services.AddOptions<AssetOptions>();
            services.AddOptions<AssetPollerOptions>();
        });
    }
}

public sealed class ClusterFixture : IDisposable
{
    public TestCluster Cluster { get; }

    public ClusterFixture()
    {
        Cluster = new TestClusterBuilder(1)
            .AddSiloBuilderConfigurator<TestSiloConfigurator>()
            .Build();

        Cluster.Deploy();
    }

    public void Dispose()
        => Cluster.StopAllSilos();
}

using Orleans.Hosting;
using Orleans.TestingHost;
using Xunit;

namespace VPP_ORLEANS.Tests;

[CollectionDefinition("Cluster", DisableParallelization = true)]
public sealed class ClusterCollection : ICollectionFixture<ClusterFixture>;

public class TestSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
        => siloBuilder.AddMemoryGrainStorage("AdoNet");
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

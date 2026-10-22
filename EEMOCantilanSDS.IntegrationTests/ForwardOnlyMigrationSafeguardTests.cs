using EEMOCantilanSDS.Infrastructure.Migrations;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>The NPM evidence schema is forward-only: rolling it back needs an approved forward recovery plan, so its Down must keep refusing.</summary>
public sealed class ForwardOnlyMigrationSafeguardTests
{
    [Fact]
    public void TheNpmEvidenceFoundationRefusesToBeRolledBack()
    {
        var down = Assert.Throws<NotSupportedException>(() => new AddNpmRentMonthFoundation().DownOperations);
        Assert.Contains("forward recovery plan", down.Message);
    }
}

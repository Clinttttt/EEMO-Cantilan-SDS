using System.Reflection;

namespace EEMOCantilanSDS.ComponentTests;

/// <summary>
/// The dependency direction on the BROWSER side of the wire.
///
/// <para>
/// The solution's architecture tests live in the unit-test project, which targets a different framework and does not
/// reference the portal — so the portal's own direction was never asserted anywhere. Only the server half was: that
/// Infrastructure does not reach into the Api or the UI. The reverse, that the UI does not reach into the server, was left to
/// habit.
/// </para>
///
/// <para>
/// It matters more here than the symmetry suggests. The portal is a Blazor Server app, so a reference to Infrastructure would
/// compile and run: a page could open a DbContext and query the office's data directly, bypassing the API, its authorization,
/// and the tenant filter that keeps one municipality's figures out of another's screens. Nothing would look wrong until it
/// was.
/// </para>
/// </summary>
public class PortalDependencyDirectionTests
{
    private static readonly Assembly Portal = typeof(EEMOCantilanSDS.Client.Services.BrandingState).Assembly;
    private static readonly Assembly ApiClients = typeof(EEMOCantilanSDS.HttpClients.ApiClients.AdminsApiClient).Assembly;

    private static string[] ReferencedNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(n => n.Name!).ToArray();

    [Fact]
    public void ThePortalDoesNotReachPastTheApi()
    {
        // It talks to the office through HTTP, so it has no business holding the server's assemblies. EF Core is named as
        // well as Infrastructure: a direct DbContext in a page is the specific mistake this prevents, and it would arrive as
        // a package reference before it arrived as a using.
        var refs = ReferencedNames(Portal);

        Assert.DoesNotContain("EEMOCantilanSDS.Infrastructure", refs);
        Assert.DoesNotContain("EEMOCantilanSDS.Api", refs);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", refs);
        Assert.DoesNotContain("Npgsql", refs);
    }

    [Fact]
    public void TheApiClientsDoNotReachPastTheApiEither()
    {
        // The typed clients are the portal's only door to the office. If they could see Infrastructure, the door would have a
        // second one beside it.
        var refs = ReferencedNames(ApiClients);

        Assert.DoesNotContain("EEMOCantilanSDS.Infrastructure", refs);
        Assert.DoesNotContain("EEMOCantilanSDS.Api", refs);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", refs);
        Assert.DoesNotContain("Npgsql", refs);
    }

    [Fact]
    public void TheChecksAreLookingAtTheRightAssemblies()
    {
        // Guards the two above from passing vacuously: an assembly that resolved to something unexpected would contain
        // neither the forbidden names nor the ones that must be there.
        Assert.Contains("EEMOCantilanSDS.HttpClients", ReferencedNames(Portal));
        Assert.Contains("EEMOCantilanSDS.Application", ReferencedNames(ApiClients));
    }
}

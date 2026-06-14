using System.Linq;
using System.Reflection;
using Xunit;

namespace EEMOCantilanSDS.Testing.Architecture;

/// <summary>
/// Architecture guardrails: assert the Clean Architecture dependency direction holds at the assembly
/// reference level (Domain depends on nothing in the solution; Application never references
/// Infrastructure/Api; Infrastructure never references the Api/UI projects). These are dependency-free
/// reflection checks — they read each assembly's direct references, so a future accidental violation
/// (e.g. Application using an Infrastructure type) will fail the build instead of silently leaking.
/// Note: the known, accepted pragmatic leaks (Domain uses PasswordHasher; Application's IAppDbContext
/// exposes EF Core DbSet) are intentionally NOT asserted here, matching the approved review.
/// </summary>
public class ArchitectureDependencyTests
{
    private static readonly Assembly Domain =
        typeof(EEMOCantilanSDS.Domain.Common.BaseEntity).Assembly;
    private static readonly Assembly Application =
        typeof(EEMOCantilanSDS.Application.Common.Interface.Persistence.IUnitOfWork).Assembly;
    private static readonly Assembly Infrastructure =
        typeof(EEMOCantilanSDS.Infrastructure.Persistence.UnitOfWork).Assembly;

    private static string[] ReferencedNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(n => n.Name!).ToArray();

    [Fact]
    public void Domain_DoesNotDependOn_SolutionLayers_Or_EfCore()
    {
        var refs = ReferencedNames(Domain);
        Assert.DoesNotContain("EEMOCantilanSDS.Application", refs);
        Assert.DoesNotContain("EEMOCantilanSDS.Infrastructure", refs);
        Assert.DoesNotContain("EEMOCantilanSDS.Api", refs);
        Assert.DoesNotContain("EEMOCantilanSDS.Client", refs);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", refs);
    }

    [Fact]
    public void Application_DoesNotDependOn_Infrastructure_Or_Api()
    {
        var refs = ReferencedNames(Application);
        Assert.DoesNotContain("EEMOCantilanSDS.Infrastructure", refs);
        Assert.DoesNotContain("EEMOCantilanSDS.Api", refs);
        Assert.DoesNotContain("EEMOCantilanSDS.Client", refs);
        Assert.DoesNotContain("EEMOCantilanSDS.Mobile", refs);
    }

    [Fact]
    public void Infrastructure_DoesNotDependOn_Api_Or_Ui()
    {
        var refs = ReferencedNames(Infrastructure);
        Assert.DoesNotContain("EEMOCantilanSDS.Api", refs);
        Assert.DoesNotContain("EEMOCantilanSDS.Client", refs);
        Assert.DoesNotContain("EEMOCantilanSDS.Mobile", refs);
    }
}

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
/// Note: one of the two documented pragmatic leaks is now closed — Application no longer references ASP.NET
/// Identity, since password hashing moved behind a port. Domain still hashes inline (eight call sites in the
/// user entities) and Application's IAppDbContext still exposes EF Core DbSets; both remain unasserted on
/// purpose, and both are recorded in .kiro/knowledge/OUTSTANDING_WORK.md with what closing them would touch.
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

        // Domain states the rules; it does not dispatch requests. MediatR belongs to Application's pipeline, and a domain
        // that could publish through it would let entity code reach back into handlers.
        Assert.DoesNotContain("MediatR", refs);
        Assert.DoesNotContain("MediatR.Contracts", refs);
    }

    [Fact]
    public void Application_DoesNotDependOn_AspNetIdentity()
    {
        // Earned, not aspirational: six handlers used to construct ASP.NET Identity's PasswordHasher inline, so
        // Application referenced an identity package to answer "is this password right". That decision now lives behind
        // IPasswordHasher and is implemented in Infrastructure, and the package reference is gone.
        //
        // Asserted so it cannot creep back one convenient using at a time. If a handler needs to hash or check a
        // password, it takes the port; if the port is missing something, the port grows.
        var refs = ReferencedNames(Application);
        Assert.DoesNotContain("Microsoft.Extensions.Identity.Core", refs);
        Assert.DoesNotContain("Microsoft.AspNetCore.Identity", refs);
    }

    [Fact]
    public void Application_DoesNotDependOn_AutoMapper()
    {
        // Removed rather than tidied: the profile was empty and there was not one IMapper, .Map<> or CreateMap call in the
        // whole solution, so it was a package, a registration and a class that did nothing. Asserted because a dead mapper
        // is the kind of thing that gets re-added "for consistency" and then quietly becomes load-bearing.
        Assert.DoesNotContain("AutoMapper", ReferencedNames(Application));
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

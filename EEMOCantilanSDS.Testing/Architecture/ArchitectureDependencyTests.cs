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
///
/// <para>
/// Both of the leaks this file used to record as open are now closed. Domain no longer hashes passwords inline — the user
/// entities take a <c>HashedPassword</c> produced by <c>IPasswordHasher</c> — and Domain no longer names HTTP outcomes.
/// What remains open is Application's <c>IAppDbContext</c>, which still exposes EF Core <c>DbSet</c>s, so "Application
/// free of EF" is deliberately unasserted; it is recorded in <c>.kiro/knowledge/OUTSTANDING_WORK.md</c> with what closing
/// it would touch.
/// </para>
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

        // And it no longer hashes passwords. The user entities used to construct ASP.NET Identity's hasher inline, which is
        // what put an identity package in the domain; they now take a HashedPassword produced by IPasswordHasher. Asserting
        // the reference is gone, rather than that the code is absent, because the package coming back is the first step to
        // the code coming back.
        Assert.DoesNotContain("Microsoft.Extensions.Identity.Core", refs);
        Assert.DoesNotContain("Microsoft.AspNetCore.Identity", refs);
    }


    [Fact]
    public void Domain_KnowsNothingAboutHttp()
    {
        // Result<T> used to live in Domain carrying status codes and names like Unauthorized, Conflict and NoContent —
        // facts about a web API, not about a market, a stall or a receipt. It moved to Application, along with
        // CursorPagedResult<T>, which is a paging contract rather than a domain concept.
        //
        // Asserted by looking for the types rather than for the numbers: a domain type NAMED after an HTTP outcome is the
        // shape of the mistake. Domain never referenced either of these, so nothing in the rules relied on them.
        var domainTypes = Domain.GetTypes().Select(t => t.FullName ?? t.Name).ToList();

        Assert.DoesNotContain(domainTypes, n => n.Contains("Domain.Common.Result", StringComparison.Ordinal));
        Assert.DoesNotContain(domainTypes, n => n.Contains("CursorPagedResult", StringComparison.Ordinal));

        // And they really do exist where they now belong, so this cannot pass by their having been deleted outright.
        var applicationTypes = Application.GetTypes().Select(t => t.FullName ?? t.Name).ToList();
        Assert.Contains(applicationTypes, n => n.Contains("Application.Common.Result", StringComparison.Ordinal));
        Assert.Contains(applicationTypes, n => n.Contains("Application.Common.CursorPagedResult", StringComparison.Ordinal));
    }

    [Fact]
    public void Domain_CarriesNoPackagesAtAll()
    {
        // The strongest form of the rule, and it is currently TRUE: the domain project has no package reference and no
        // project reference, so its whole surface is the framework. Every other assertion in this file names a specific
        // package that must stay out; this one says nothing may come in without a deliberate decision.
        //
        // Why assert the whole set rather than another deny-list: the deny-lists were each written after a package had
        // already got in — Identity to hash a password, MediatR to publish, EF to hold a DbSet. A list of the mistakes
        // already made cannot catch the next one.
        var framework = new[] { "System", "netstandard", "mscorlib", "Microsoft.CSharp" };

        var foreign = ReferencedNames(Domain)
            .Where(name => !framework.Any(f => name == f || name.StartsWith(f + ".", StringComparison.Ordinal)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(foreign.Count == 0,
            "the domain must depend on nothing but the framework, and now depends on: " + string.Join(", ", foreign));
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

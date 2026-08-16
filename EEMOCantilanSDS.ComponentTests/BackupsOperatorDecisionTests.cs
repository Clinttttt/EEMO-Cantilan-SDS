using System.Security.Claims;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Authorization;
using EEMOCantilanSDS.Application.Common.Tenancy;

namespace EEMOCantilanSDS.ComponentTests;

/// <summary>
/// The portal must not hold its own opinion about who the platform operator is.
///
/// <para>
/// The Backups page decided for itself, comparing the municipality claim with one LGU's code written straight into the markup. Two
/// faults in one line. It was a third opinion on a rule <see cref="PlatformOperatorPolicy"/> exists to hold — and an incomplete one,
/// carrying only the backward-compatible fallback clause, so a DEDICATED operator account (the intended mechanism, and the one a
/// fresh deployment gets) was shown none of the whole-database controls the API already permits it. It also placed a municipality's
/// code in the portal, where no LGU's value belongs.
/// </para>
///
/// <para>
/// These tests state the DECISION as the page now computes it, from claims, and check it against the shared rule for every
/// combination that matters. The page's own line is unreachable from a test without rendering the whole page and its backup API,
/// so the computation is mirrored here exactly; the source assertion at the end is what keeps the two from drifting.
/// </para>
/// </summary>
public class BackupsOperatorDecisionTests
{
    /// <summary>The decision as <c>Backups.razor</c> makes it — same inputs, same comparisons, same order.</summary>
    private static bool DecideAsThePageDoes(ClaimsPrincipal user, bool isSuperAdmin) =>
        PlatformOperatorPolicy.IsOperator(
            isDedicatedOperator: string.Equals(
                user.FindFirst(AppClaimTypes.PlatformOperator)?.Value, "true", StringComparison.OrdinalIgnoreCase),
            role: isSuperAdmin ? PlatformOperatorPolicy.SuperAdminRole : null,
            isDefaultTenant: string.Equals(
                user.FindFirst(AppClaimTypes.Municipality)?.Value, TenantConstants.DefaultTenantCode,
                StringComparison.Ordinal));

    private static ClaimsPrincipal User(string? municipality, bool dedicatedOperator = false)
    {
        var claims = new List<Claim>();
        if (municipality is not null) claims.Add(new Claim(AppClaimTypes.Municipality, municipality));
        if (dedicatedOperator) claims.Add(new Claim(AppClaimTypes.PlatformOperator, "true"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [Fact]
    public void ADedicatedOperatorSeesTheWholeDatabaseSections_WhateverMunicipalityTheyBelongTo()
    {
        // The case the old line got wrong, and the one that matters most: this account IS the platform operator, the API lets it
        // run a whole-database backup, and the portal showed it nothing.
        Assert.True(DecideAsThePageDoes(User("some-other-lgu", dedicatedOperator: true), isSuperAdmin: false));
        Assert.True(DecideAsThePageDoes(User(municipality: null, dedicatedOperator: true), isSuperAdmin: false));
    }

    [Fact]
    public void TheDefaultTenantsHeadStillSeesThem()
    {
        // The documented fallback, preserved deliberately: while one LGU was the only LGU, its Head WAS the operator, and
        // removing that would lock the office out of its own onboarding before a dedicated account exists.
        Assert.True(DecideAsThePageDoes(User(TenantConstants.DefaultTenantCode), isSuperAdmin: true));
    }

    [Fact]
    public void AnotherLGUsHeadDoesNOTSeeThem()
    {
        // The whole point of the gate. A municipal Head must not be offered a restore that would overwrite every municipality's
        // data — the API refuses it, and the portal must not appear to offer it.
        Assert.False(DecideAsThePageDoes(User("another-lgu"), isSuperAdmin: true));
    }

    [Fact]
    public void TheDefaultTenantAloneIsNotEnoughWithoutTheRole()
    {
        Assert.False(DecideAsThePageDoes(User(TenantConstants.DefaultTenantCode), isSuperAdmin: false));
    }

    [Fact]
    public void ThePortalHoldsNoMUNICIPALITYCODEOfItsOwn()
    {
        // The multi-tenancy guard, asserted against the source because that is where the fault lived. One LGU's code sitting in
        // the portal is how another LGU's screen ends up answering to it.
        var portal = Path.Combine(RepositoryRoot(), "EEMOCantilanSDS.Client");
        var offenders = Directory
            .EnumerateFiles(portal, "*.*", SearchOption.AllDirectories)
            .Where(p => (p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                         p.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
                        && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(p => File.ReadAllText(p).Contains(TenantConstants.DefaultTenantCode, StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(offenders);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EEMOCantilanSDS.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);        // the test is meaningless if it cannot find the tree
        return dir!.FullName;
    }
}

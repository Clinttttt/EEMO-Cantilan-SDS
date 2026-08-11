using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Infrastructure.Tenancy;

namespace EEMOCantilanSDS.Testing.Infrastructure.Tenancy;

/// <summary>
/// How a request's tenant is resolved.
///
/// <para>
/// The order used to end in the DEFAULT municipality for everyone, authenticated or not. So a user of any LGU whose
/// municipality claim was missing or malformed read CANTILAN's data and was told it was their own — quietly, and with no
/// error anywhere. That fallback is now for token-less callers only, which are the paths that legitimately have no user
/// to resolve: login (which bypasses the filter anyway), activation, webhooks, background work and startup.
/// </para>
/// </summary>
public class CurrentMunicipalityAccessorTests
{
    private sealed class Caller(bool authenticated, Guid? municipalityId) : ICurrentUserService
    {
        public bool IsAuthenticated => authenticated;
        public Guid? MunicipalityId => municipalityId;
        public Guid? UserId => authenticated ? Guid.NewGuid() : null;
        public string? Username => authenticated ? "head" : null;
        public string? Role => authenticated ? "SuperAdmin" : null;
        public Guid? CollectorId => null;
        public string? MunicipalityCode => null;

        public EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser.AdminUserDto? GetCurrentUser() => null;
    }

    private sealed class NoOverride : IRequestTenantScope
    {
        public Guid? MunicipalityId => null;
        public string? TenantCode => null;
        public void Use(Guid municipalityId, string tenantCode) { }
    }

    private sealed class PinnedTo(Guid id) : IRequestTenantScope
    {
        public Guid? MunicipalityId => id;
        public string? TenantCode => "pinned";
        public void Use(Guid municipalityId, string tenantCode) { }
    }

    private static DefaultMunicipalityStore StoreWith(Guid id)
    {
        var store = new DefaultMunicipalityStore();
        store.Set(id);
        return store;
    }

    [Fact]
    public void AnAuthenticatedCallerGetsTheirOwnMunicipality()
    {
        var theirs = Guid.NewGuid();
        var accessor = new CurrentMunicipalityAccessor(
            new Caller(authenticated: true, theirs), StoreWith(Guid.NewGuid()), new NoOverride());

        Assert.Equal(theirs, accessor.MunicipalityId);
    }

    [Fact]
    public void AnAuthenticatedCallerWithNoMunicipalityGetsNOTHING_notTheDefault()
    {
        // The hazard this removes. Falling through to the default meant reading another LGU's data - Cantilan's - and
        // being told it was your own. Unresolved now, which the query filter answers with no rows.
        var defaultMunicipality = Guid.NewGuid();
        var accessor = new CurrentMunicipalityAccessor(
            new Caller(authenticated: true, municipalityId: null), StoreWith(defaultMunicipality), new NoOverride());

        Assert.Equal(Guid.Empty, accessor.MunicipalityId);
        Assert.NotEqual(defaultMunicipality, accessor.MunicipalityId);
    }

    [Fact]
    public void ATokenLessCallerStillGetsTheDefault()
    {
        // Login, activation, the anonymous webhook without a pin, background work and startup have no user to resolve.
        // The default is the right answer for them and is deliberately kept.
        var defaultMunicipality = Guid.NewGuid();
        var accessor = new CurrentMunicipalityAccessor(
            new Caller(authenticated: false, municipalityId: null), StoreWith(defaultMunicipality), new NoOverride());

        Assert.Equal(defaultMunicipality, accessor.MunicipalityId);
    }

    [Fact]
    public void AnExplicitPinBeatsEverything()
    {
        // The PayMongo webhook carries no token but must settle its payment under the TRANSACTION's LGU, not the default.
        var pinned = Guid.NewGuid();
        var accessor = new CurrentMunicipalityAccessor(
            new Caller(authenticated: true, Guid.NewGuid()), StoreWith(Guid.NewGuid()), new PinnedTo(pinned));

        Assert.Equal(pinned, accessor.MunicipalityId);
    }
}

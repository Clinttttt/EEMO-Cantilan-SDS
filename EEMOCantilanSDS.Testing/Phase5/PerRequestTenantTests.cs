using System;
using System.Linq;
using System.Security.Claims;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Interceptors;
using EEMOCantilanSDS.Infrastructure.Services;
using EEMOCantilanSDS.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EEMOCantilanSDS.Testing.Phase5;

/// <summary>
/// Phase 5 (increment 1) — proves tenant isolation is resolved PER-REQUEST instead of a process-wide
/// startup default:
///  • the write-stamp interceptor stamps new rows with the CURRENT context's municipality,
///  • an unresolved (empty) context leaves rows unstamped (single-tenant / test path unchanged),
///  • <see cref="CurrentUserService"/> resolves the municipality from the JWT <c>municipality_id</c> claim,
///  • the scoped accessor falls back to the default municipality for token-less flows (Cantilan byte-for-byte).
/// </summary>
public class PerRequestTenantTests
{
    private sealed class FixedMunicipality(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private static DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new MunicipalityStampInterceptor())
            .Options;

    // ---- Stamp interceptor: uses the current context's municipality --------------------------------

    [Fact]
    public void Stamp_UsesCurrentContextMunicipality_NotADefault()
    {
        var munB = Guid.NewGuid();
        using var ctx = new AppDbContext(Options(), new FixedMunicipality(munB));

        // Add a tenant-owned row WITHOUT setting MunicipalityId — the interceptor must stamp it.
        var facility = Facility.Create(FacilityCode.NPM, "Carmen NPM", "NPM");
        ctx.Facilities.Add(facility);
        ctx.SaveChanges();

        var saved = ctx.Facilities.IgnoreQueryFilters().Single();
        Assert.Equal(munB, saved.MunicipalityId);
    }

    [Fact]
    public void Stamp_UnresolvedContext_LeavesRowUnstamped()
    {
        using var ctx = new AppDbContext(Options(), new FixedMunicipality(Guid.Empty));

        var facility = Facility.Create(FacilityCode.NPM, "Cantilan NPM", "NPM");
        ctx.Facilities.Add(facility);
        ctx.SaveChanges();

        var saved = ctx.Facilities.IgnoreQueryFilters().Single();
        Assert.Equal(Guid.Empty, saved.MunicipalityId);
    }

    // ---- CurrentUserService: reads the municipality_id claim ---------------------------------------

    private sealed class FixedHttpContextAccessor(ClaimsPrincipal principal) : IHttpContextAccessor
    {
        private readonly DefaultHttpContext _ctx = new() { User = principal };
        public HttpContext? HttpContext { get => _ctx; set { } }
    }

    [Fact]
    public void CurrentUserService_ParsesMunicipalityIdClaim()
    {
        var municipalityId = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(AppClaimTypes.MunicipalityId, municipalityId.ToString()) },
            authenticationType: "Test"));

        var sut = new CurrentUserService(new FixedHttpContextAccessor(principal));

        Assert.Equal(municipalityId, sut.MunicipalityId);
    }

    [Fact]
    public void CurrentUserService_NoClaim_ReturnsNull()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        var sut = new CurrentUserService(new FixedHttpContextAccessor(principal));

        Assert.Null(sut.MunicipalityId);
    }

    // ---- Scoped accessor: per-request resolution with default fallback -----------------------------

    private sealed class StubCurrentUser(Guid? municipalityId) : EEMOCantilanSDS.Application.Common.Interface.Services.ICurrentUserService
    {
        public bool IsAuthenticated => municipalityId is not null;
        public EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser.AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => null;
        public string? Username => null;
        public string? Role => null;
        public Guid? CollectorId => null;
        public string? MunicipalityCode => null;
        public Guid? MunicipalityId => municipalityId;
    }

    [Fact]
    public void Accessor_UsesUserMunicipality_WhenPresent()
    {
        var userMunicipality = Guid.NewGuid();
        var store = new DefaultMunicipalityStore();
        store.Set(Guid.NewGuid()); // some other default

        var accessor = new CurrentMunicipalityAccessor(new StubCurrentUser(userMunicipality), store);

        Assert.Equal(userMunicipality, accessor.MunicipalityId);
    }

    [Fact]
    public void Accessor_FallsBackToDefault_WhenTokenLess()
    {
        var cantilan = Guid.NewGuid();
        var store = new DefaultMunicipalityStore();
        store.Set(cantilan); // populated once at startup

        var accessor = new CurrentMunicipalityAccessor(new StubCurrentUser(null), store);

        Assert.Equal(cantilan, accessor.MunicipalityId);
    }

    [Fact]
    public void Accessor_SetDelegatesToStore()
    {
        var store = new DefaultMunicipalityStore();
        var accessor = new CurrentMunicipalityAccessor(new StubCurrentUser(null), store);

        var cantilan = Guid.NewGuid();
        accessor.Set(cantilan); // startup Set path

        Assert.Equal(cantilan, store.Default);
        Assert.Equal(cantilan, accessor.MunicipalityId);
    }
}

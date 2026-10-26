using EEMOCantilanSDS.Application.Command.Stalls.CreateStall;
using EEMOCantilanSDS.Application.Command.Stalls.RenewStallContract;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// A Custom Space Rental facility may record a stall contract in months as well as years. Through the real handlers, repositories,
/// tenant filters and PostgreSQL: the term, expiry, billing months, arrears, renewal and tenant isolation, with yearly contracts and
/// every other facility unchanged.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CustomSpaceRentalMonthTermTests(PostgresFixture db)
{
    private sealed class QuietCache : IEemoCacheInvalidator
    {
        public Task InvalidateRegionAsync(string region, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidatePeriodAsync(string t, int y, int m, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidateFacilityPeriodAsync(string t, FacilityCode f, int y, int m, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidatePaymentAffectedViewsAsync(string t, FacilityCode? f, int y, int m, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidateReferenceDataAsync(string t, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidateTenantAsync(string t, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class Tenant : ITenantContext { public string TenantCode => "month-term-test"; }

    private static readonly DateOnly Today = PhilippineTime.Today;

    private async Task<Guid> SeedTenantAsync(string name, FacilityCode code, CustomFacilityCategory category)
    {
        var municipality = Municipality.Create($"MT-{Guid.NewGuid():N}"[..24], name, "Province", MunicipalityStatus.Active,
            tenantCode: $"mt-{Guid.NewGuid():N}"[..32]);
        await using (var setup = db.CreateContext(Guid.Empty)) { setup.Municipalities.Add(municipality); await setup.SaveChangesAsync(); }
        await using var ctx = db.CreateContext(municipality.Id);
        ctx.Facilities.Add(Facility.Create(code, $"{name} Cold Storage", "CS", municipalityId: municipality.Id, customCategory: category));
        await ctx.SaveChangesAsync();
        return municipality.Id;
    }

    private static CreateStallCommandHandler Create(AppDbContext ctx) => new(
        new StallRepository(ctx), new FacilityRepository(ctx), new PayorRepository(ctx, null!), new UnitOfWork(ctx),
        new QuietCache(), new Tenant(), new SystemClock(), ctx);

    private sealed class Head : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser.AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => Guid.NewGuid();
        public string? Username => "head";
        public string? Role => "Admin";
        public Guid? CollectorId => null;
        public string? MunicipalityCode => "month-term-test";
        public Guid? MunicipalityId => null;
    }

    private static RenewStallContractCommandHandler Renew(AppDbContext ctx) =>
        new(new StallRepository(ctx), new PayorRepository(ctx, null!), new Head(), new UnitOfWork(ctx), new QuietCache(), new Tenant());

    private static CreateStallCommand Command(FacilityCode code, string no, DateTime start, int years, int? months, decimal rate = 5000m) =>
        new(code, no, rate, ApplicableFees.BaseRental, null, null, null, null, null, "Cold Holder", "Cold Holder", start, years, ContractMonths: months);

    [SkippableFact]
    public async Task ASixMonthTermIsRecordedExpiresOnTheRightDayAndBillsExactlySixMonths()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await SeedTenantAsync("Alpha", FacilityCode.Custom1, CustomFacilityCategory.SpaceRental);
        await using (var ctx = db.CreateContext(tenant))
        {
            var result = await Create(ctx).Handle(Command(FacilityCode.Custom1, "CS-1", new DateTime(Today.Year, Today.Month, 1).AddMonths(-2), 0, 6), default);
            Assert.True(result.IsSuccess, result.Error);
            Assert.Equal((0, (int?)6), (result.Value!.ContractYears, result.Value.ContractMonths));
        }
        await using (var ctx = db.CreateContext(tenant))
        {
            var contract = await ctx.Contracts.SingleAsync();
            Assert.Equal((0, (int?)6), (contract.DurationYears, contract.DurationMonths));
            var first = contract.EffectivityDate;
            Assert.Equal(first.AddMonths(6).AddDays(-1), contract.ExpiryDate);
            var billed = Enumerable.Range(-3, 20).Select(i => new DateOnly(first.Year, first.Month, 1).AddMonths(i)).Count(m => contract.BillsCalendarMonth(m.Year, m.Month));
            Assert.Equal(6, billed);

            // The register the facility page reads still lists it as a current vendor until the term really ends.
            var stalls = await new StallRepository(ctx).GetStallsByFacilityAsync(FacilityCode.Custom1, null, default);
            var dto = Assert.Single(stalls);
            Assert.Equal(6, dto.ContractMonths);
            Assert.True(StallContractStatus.IsCurrentVendor(dto, Today));
            Assert.False(StallContractStatus.IsCurrentVendor(dto, contract.ExpiryDate.AddDays(1)));
        }
    }

    [SkippableFact]
    public async Task MonthsAreRefusedOutsideACustomSpaceRentalFacility_AndNothingIsSaved()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await SeedTenantAsync("Alpha", FacilityCode.Custom2, CustomFacilityCategory.StallRent);
        await using var ctx = db.CreateContext(tenant);

        var result = await Create(ctx).Handle(Command(FacilityCode.Custom2, "S-1", new DateTime(2026, 1, 1), 1, 6), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Empty(await ctx.Stalls.ToListAsync());
        Assert.Empty(await ctx.Contracts.ToListAsync());
    }

    [SkippableFact]
    public async Task AYearlyContractOnASpaceRentalFacilityIsStoredAndExpiresExactlyAsBefore()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await SeedTenantAsync("Alpha", FacilityCode.Custom1, CustomFacilityCategory.SpaceRental);
        await using var ctx = db.CreateContext(tenant);

        var result = await Create(ctx).Handle(Command(FacilityCode.Custom1, "CS-2", new DateTime(2028, 2, 29), 3, null), default);

        Assert.True(result.IsSuccess, result.Error);
        var contract = await ctx.Contracts.SingleAsync();
        Assert.Null(contract.DurationMonths);
        Assert.Equal(DomainRules.TermLastDay(new DateOnly(2028, 2, 29), 3), contract.ExpiryDate);
    }

    [SkippableFact]
    public async Task RenewingForMonthsStartsANewTermWithoutBackBillingOrOverlap()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await SeedTenantAsync("Alpha", FacilityCode.Custom1, CustomFacilityCategory.SpaceRental);
        Guid stallId;
        await using (var ctx = db.CreateContext(tenant))
        {
            var created = await Create(ctx).Handle(Command(FacilityCode.Custom1, "CS-3", new DateTime(2025, 1, 15), 0, 6), default);
            Assert.True(created.IsSuccess, created.Error);
            stallId = created.Value!.Id;
        }
        await using (var ctx = db.CreateContext(tenant))
        {
            var renewed = await Renew(ctx).Handle(new RenewStallContractCommand(stallId, new DateOnly(2026, 1, 1), 1, "Cold Holder", "Cold Holder", DurationMonths: 6), default);
            Assert.True(renewed.IsSuccess, renewed.Error);
        }
        await using (var ctx = db.CreateContext(tenant))
        {
            var contracts = await ctx.Contracts.OrderBy(c => c.EffectivityDate).ToListAsync();
            Assert.Equal(2, contracts.Count);
            Assert.False(contracts[0].IsActive);
            Assert.Equal(new DateOnly(2025, 7, 14), contracts[0].EndedOn);                    // clamped to its own expiry, not the day before the renewal
            Assert.Equal((1, (int?)6), (contracts[1].DurationYears, contracts[1].DurationMonths));
            Assert.Equal(new DateOnly(2027, 6, 30), contracts[1].ExpiryDate);
            Assert.Empty(await ctx.PaymentRecords.ToListAsync());                              // renewal moves no money
        }
    }

    [SkippableFact]
    public async Task RenewalMonthsAreRefusedForAStallRentFacility()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await SeedTenantAsync("Alpha", FacilityCode.Custom3, CustomFacilityCategory.StallRent);
        Guid stallId;
        await using (var ctx = db.CreateContext(tenant))
            stallId = (await Create(ctx).Handle(Command(FacilityCode.Custom3, "S-9", new DateTime(2024, 1, 1), 1, null), default)).Value!.Id;
        await using (var ctx = db.CreateContext(tenant))
        {
            var renewed = await Renew(ctx).Handle(new RenewStallContractCommand(stallId, new DateOnly(2026, 1, 1), 1, "Cold Holder", "Cold Holder", DurationMonths: 3), default);
            Assert.False(renewed.IsSuccess);
        }
        await using (var ctx = db.CreateContext(tenant))
            Assert.Single(await ctx.Contracts.ToListAsync());                                 // the old term was not touched
    }

    [SkippableFact]
    public async Task AnExpiredMonthTermKeepsExactlyItsSixMonthsInArrears_AndTenantsDoNotSeeEachOther()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await SeedTenantAsync("Alpha", FacilityCode.Custom1, CustomFacilityCategory.SpaceRental);
        var other = await SeedTenantAsync("Beta", FacilityCode.Custom1, CustomFacilityCategory.SpaceRental);
        await using (var ctx = db.CreateContext(tenant))
        {
            var created = await Create(ctx).Handle(Command(FacilityCode.Custom1, "CS-4", new DateTime(2025, 1, 15), 0, 6, 4000m), default);
            Assert.True(created.IsSuccess, created.Error);
        }
        await using (var ctx = db.CreateContext(tenant))
        {
            var account = Assert.Single(await new StallRepository(ctx).GetClosedStallAccountsAsync(default));
            Assert.Equal(new DateOnly(2025, 7, 14), account.ExpiryDate);
            Assert.Equal(6, account.DurationMonths);
            Assert.Equal(6, account.MonthsUnpaid);                                              // Jan-Jun 2025: no seventh month, none missing
            Assert.Equal(24_000m, account.Uncollected);
        }
        await using var otherCtx = db.CreateContext(other);
        Assert.Empty(await otherCtx.Contracts.ToListAsync());
        Assert.Empty(await new StallRepository(otherCtx).GetClosedStallAccountsAsync(default));
    }
}

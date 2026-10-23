using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Testing.Rates;

public sealed class IndependentWeighingRateTests
{
    private sealed class Tenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private static DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    [Fact]
    public void Versions_require_a_valid_enabled_rate_and_disable_explicitly()
    {
        var id = Guid.NewGuid();
        var date = new DateOnly(2026, 10, 23);
        Assert.Throws<ArgumentException>(() => WeighingRateVersion.Create(id, FishMeatVendorType.Meat, true, null, date, "Head"));
        Assert.Throws<ArgumentException>(() => WeighingRateVersion.Create(id, FishMeatVendorType.Meat, true, 0m, date, "Head"));
        Assert.Throws<ArgumentException>(() => WeighingRateVersion.Create(id, FishMeatVendorType.Meat, true, 60.001m, date, "Head"));
        Assert.Throws<ArgumentException>(() => WeighingRateVersion.Create(id, FishMeatVendorType.Meat, false, 60m, date, "Head"));
        var off = WeighingRateVersion.Create(id, FishMeatVendorType.Fish, false, null, date, "Head");
        Assert.False(off.IsEnabled);
        Assert.Null(off.RatePerKilo);
        Assert.Equal(id, off.MunicipalityId);
    }

    [Fact]
    public async Task Fish_only_configuration_stays_tenant_scoped_and_keeps_legacy_before_adoption()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var before = new DateOnly(2026, 10, 23);
        var since = before.AddDays(1);
        var opt = Options();
        using (var seed = new AppDbContext(opt, new Tenant(a)))
        {
            seed.FacilityRates.Add(FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmMeatPerKilo, 66m,
                new DateOnly(2026, 1, 1), a));
            seed.WeighingRateVersions.Add(WeighingRateVersion.Create(a, FishMeatVendorType.Fish, true, 2.50m, since, "Head"));
            seed.WeighingRateVersions.Add(WeighingRateVersion.Create(a, FishMeatVendorType.Meat, false, null, since, "Head"));
            await seed.SaveChangesAsync();
        }
        using (var ctx = new AppDbContext(opt, new Tenant(a)))
        {
            var rates = new FeeRateResolver(ctx);
            Assert.Equal(66m, (await WeighingRateSchedule.ResolveAsync(ctx, rates, a, FishMeatVendorType.Meat, before, default))?.RatePerKilo);
            Assert.Null(await WeighingRateSchedule.ResolveAsync(ctx, rates, a, FishMeatVendorType.Meat, since, default));
            Assert.Equal(2.50m, (await WeighingRateSchedule.ResolveAsync(ctx, rates, a, FishMeatVendorType.Fish, since, default))?.RatePerKilo);
            Assert.True(await WeighingRateSchedule.HasAvailableTypeAsync(ctx, a, since, default));
        }
        using (var other = new AppDbContext(opt, new Tenant(b)))
        {
            Assert.Null(await WeighingRateSchedule.ResolveAsync(other, new FeeRateResolver(other), b, FishMeatVendorType.Meat, since, default));
            Assert.False(await WeighingRateSchedule.HasAvailableTypeAsync(other, b, since, default));
        }
    }

    [Fact]
    public async Task New_rate_is_effective_only_on_its_business_date_and_never_mutates_old_version()
    {
        var id = Guid.NewGuid();
        var first = new DateOnly(2026, 10, 23);
        var change = first.AddDays(1);
        var opt = Options();
        using var ctx = new AppDbContext(opt, new Tenant(id));
        var original = WeighingRateVersion.Create(id, FishMeatVendorType.Meat, true, 66m, first, "Head");
        ctx.WeighingRateVersions.Add(original);
        await ctx.SaveChangesAsync();
        ctx.WeighingRateVersions.Add(WeighingRateVersion.Create(id, FishMeatVendorType.Meat, true, 60m, change, "Head"));
        await ctx.SaveChangesAsync();
        var resolver = new FeeRateResolver(ctx);
        Assert.Equal(66m, (await WeighingRateSchedule.ResolveAsync(ctx, resolver, id, FishMeatVendorType.Meat, first, default))?.RatePerKilo);
        Assert.Equal(60m, (await WeighingRateSchedule.ResolveAsync(ctx, resolver, id, FishMeatVendorType.Meat, change, default))?.RatePerKilo);
        Assert.Equal(66m, original.RatePerKilo);
    }
}

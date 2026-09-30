using EEMOCantilanSDS.Application.Queries.Mobile.GetNpmMeatWeighingRateQuote;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Application.Requests.Mobile;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Testing.Rates;

public sealed class NpmMeatWeighingRateQuoteTests
{
    private sealed class FixedMunicipality(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private static DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    [Fact]
    public async Task Quote_resolves_the_latest_rate_effective_on_the_requested_business_date()
    {
        var tenant = Guid.NewGuid();
        var options = Options();
        using (var seed = new AppDbContext(options, new FixedMunicipality(tenant)))
        {
            seed.FacilityRates.Add(FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmMeatPerKilo,
                60m, new DateOnly(2026, 9, 1), tenant));
            seed.FacilityRates.Add(FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmMeatPerKilo,
                66m, new DateOnly(2026, 9, 29), tenant));
            await seed.SaveChangesAsync();
        }

        using var context = new AppDbContext(options, new FixedMunicipality(tenant));
        var handler = new GetNpmMeatWeighingRateQuoteQueryHandler(new FeeRateResolver(context),
            new FixedClock(new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc)));

        var beforeChange = (await handler.Handle(
            new GetNpmMeatWeighingRateQuoteQuery(new DateOnly(2026, 9, 28)), default)).Value!;
        var afterChange = (await handler.Handle(
            new GetNpmMeatWeighingRateQuoteQuery(new DateOnly(2026, 9, 29)), default)).Value!;

        Assert.True(beforeChange.IsAvailable);
        Assert.Equal(60m, beforeChange.RatePerKilo);
        Assert.Equal(new DateOnly(2026, 9, 1), beforeChange.EffectiveDate);
        Assert.Equal(66m, afterChange.RatePerKilo);
        Assert.Equal(new DateOnly(2026, 9, 29), afterChange.EffectiveDate);
    }

    [Fact]
    public async Task Quote_reports_unavailable_without_borrowing_another_tenant_rate()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var options = Options();
        using (var seed = new AppDbContext(options, new FixedMunicipality(tenantB)))
        {
            seed.FacilityRates.Add(FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmMeatPerKilo,
                66m, new DateOnly(2026, 9, 1), tenantB));
            await seed.SaveChangesAsync();
        }

        using var context = new AppDbContext(options, new FixedMunicipality(tenantA));
        var handler = new GetNpmMeatWeighingRateQuoteQueryHandler(new FeeRateResolver(context),
            new FixedClock(new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc)));
        var result = await handler.Handle(
            new GetNpmMeatWeighingRateQuoteQuery(new DateOnly(2026, 9, 30)), default);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsAvailable);
        Assert.Null(result.Value.RatePerKilo);
        Assert.Null(result.Value.EffectiveDate);
    }

    [Fact]
    public async Task Quote_rejects_a_future_collection_business_date()
    {
        var handler = new GetNpmMeatWeighingRateQuoteQueryHandler(
            new FeeRateResolver(new AppDbContext(Options(), new FixedMunicipality(Guid.NewGuid()))),
            new FixedClock(new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc)));

        var result = await handler.Handle(
            new GetNpmMeatWeighingRateQuoteQuery(new DateOnly(2026, 9, 30)), default);

        Assert.Equal(ResultStatus.Invalid, result.Status);
    }

    [Fact]
    public void Mobile_daily_collection_request_accepts_quantity_but_no_client_computed_meat_charge()
    {
        var propertyNames = typeof(RecordMobileNpmCollectionRequest).GetProperties()
            .Select(property => property.Name).ToArray();

        Assert.Contains(nameof(RecordMobileNpmCollectionRequest.MeatKilos), propertyNames);
        Assert.DoesNotContain(propertyNames, name => name.Contains("MeatFee", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("MeatAmount", StringComparison.OrdinalIgnoreCase));
    }
}

using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Queries.Reports.GetFinancialReport;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Testing.Support;

namespace EEMOCantilanSDS.Testing.Application.Reports;

/// <summary>
/// Regression: the Financial Report must show each facility's TENANT-STORED name, including the
/// paid-on-service facilities (SLH/TRM/TPM). Previously those rows used a hardcoded default
/// ("Transport Terminal"/"Tabo-an Public Market"), so an LGU that renamed them during onboarding
/// (e.g. Carmen → "Carmen Transport"/"Carmen Weekly") saw the wrong label. Cantilan is unaffected
/// because its seeded names equal the defaults (covered by the Phase-0 baseline).
/// </summary>
public class FinancialReportServiceFacilityNameTests : RepositoryTestBase
{
    [Fact]
    public async Task ServiceFacilityRows_UseTenantStoredNames_NotHardcodedDefaults()
    {
        var context = NewContext();

        // A tenant that renamed its service facilities during onboarding.
        var slh = Facility.Create(FacilityCode.SLH, "Carmen Abattoir", "CA");
        var trm = Facility.Create(FacilityCode.TRM, "Carmen Transport", "CT");
        var tpm = Facility.Create(FacilityCode.TPM, "Carmen Weekly", "CW");
        context.AddRange(slh, trm, tpm);
        await context.SaveChangesAsync();

        var handler = new GetFinancialReportQueryHandler(
            new FacilityReportsRepository(context),
            new SlaughterRepository(context),
            new TrmRepository(context),
            new TpmRepository(context, CacheTestDoubles.TpmMarketDay),
            new TransactionFeedRepository(context),
            new FacilityRepository(context),
            new StallRepository(context),
            CacheTestDoubles.FeeRateResolver,
            CacheTestDoubles.PassthroughCache,
            CacheTestDoubles.Tenant,
            new EemoCacheOptions());

        var result = await handler.Handle(
            new GetFinancialReportQuery(ReportPeriod.Monthly, 2024, 6, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var r = result.Value!;

        Assert.Equal("Carmen Abattoir", r.Facilities.Single(f => f.Code == FacilityCode.SLH).Name);
        Assert.Equal("Carmen Transport", r.Facilities.Single(f => f.Code == FacilityCode.TRM).Name);
        Assert.Equal("Carmen Weekly", r.Facilities.Single(f => f.Code == FacilityCode.TPM).Name);
    }
}

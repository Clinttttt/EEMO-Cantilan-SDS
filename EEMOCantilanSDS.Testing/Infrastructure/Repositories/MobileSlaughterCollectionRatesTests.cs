using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Testing.Infrastructure.Repositories;

/// <summary>
/// The collector's device is told the rates the COLLECTOR's OWN office charges.
///
/// Found in the audit of 2026-08-23: the mobile slaughterhouse payload was built from
/// <c>FeeRates.SlhHogTotalPerHead</c> and <c>FeeRates.SlhLargeTotalPerHead</c> — the reference municipality's ordinance —
/// and handed to every LGU. A Carrascal collector recording a hog was shown Cantilan's ₱250 while its own ordinance says
/// ₱400, and the day's total they reconcile against was struck at another municipality's figures. The stored amount was
/// safe, because the recording handler resolves the rate itself and refuses one the office has not stated, so this was a
/// wrong figure on a screen rather than a wrong receipt — which is still the office's own paper.
///
/// The existing handler suite could not catch it: it mocks the repository, so the constants never ran.
/// </summary>
public class MobileSlaughterCollectionRatesTests : RepositoryTestBase
{
    private static readonly DateOnly Day = new(2026, 8, 24);

    /// <summary>An office with NO rate rows at all — which the shared harness deliberately never gives you.</summary>
    private static AppDbContext BareContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task ThePayloadCarriesThisOfficesOwnPerHeadRates()
    {
        // Stated later than the harness's own rows, so precedence is explicit rather than a matter of insertion order.
        var stated = new DateOnly(2026, 1, 1);
        var context = NewContext();
        context.AddRange(
            Facility.Create(FacilityCode.SLH, "Slaughterhouse", "SLH"),
            FacilityRate.Create(FacilityCode.SLH, FeeRateKey.SlhHogPerHead, 400m, stated, Guid.Empty),
            FacilityRate.Create(FacilityCode.SLH, FeeRateKey.SlhLargePerHead, 500m, stated, Guid.Empty));
        await context.SaveChangesAsync();

        var dto = await new SlaughterRepository(context).GetMobileSlaughterCollectionAsync(Day, CancellationToken.None);

        Assert.Equal(400m, dto.HogRatePerHead);
        Assert.Equal(500m, dto.LargeAnimalRatePerHead);

        // Emphatically not the reference municipality's ordinance.
        Assert.NotEqual(FeeRates.SlhHogTotalPerHead, dto.HogRatePerHead);
        Assert.NotEqual(FeeRates.SlhLargeTotalPerHead, dto.LargeAnimalRatePerHead);
    }

    [Fact]
    public async Task AnOfficeThatHasStatedNoPerHeadRateIsSentNobodyElses()
    {
        // Zero, not ₱250: as far as the platform knows the office charges nothing under this head, and the recording
        // handler refuses a transaction whose rate is unstated. Sending another LGU's figure would have the collector
        // quote it at the counter.
        var context = BareContext();
        context.Add(Facility.Create(FacilityCode.SLH, "Slaughterhouse", "SLH"));
        await context.SaveChangesAsync();

        var dto = await new SlaughterRepository(context).GetMobileSlaughterCollectionAsync(Day, CancellationToken.None);

        Assert.Equal(0m, dto.HogRatePerHead);
        Assert.Equal(0m, dto.LargeAnimalRatePerHead);
    }

    [Fact]
    public async Task ARateIsReadAsOfTheDayBeingCollected()
    {
        // A raise applies from the day it was stated; a day before it is still answered with the figure in force then.
        var first = new DateOnly(2026, 1, 1);
        var raised = new DateOnly(2026, 8, 20);
        var context = BareContext();
        context.AddRange(
            Facility.Create(FacilityCode.SLH, "Slaughterhouse", "SLH"),
            FacilityRate.Create(FacilityCode.SLH, FeeRateKey.SlhHogPerHead, 400m, first, Guid.Empty),
            FacilityRate.Create(FacilityCode.SLH, FeeRateKey.SlhHogPerHead, 450m, raised, Guid.Empty));
        await context.SaveChangesAsync();

        var repo = new SlaughterRepository(context);

        Assert.Equal(400m, (await repo.GetMobileSlaughterCollectionAsync(raised.AddDays(-1), CancellationToken.None)).HogRatePerHead);
        Assert.Equal(450m, (await repo.GetMobileSlaughterCollectionAsync(raised, CancellationToken.None)).HogRatePerHead);
    }
}

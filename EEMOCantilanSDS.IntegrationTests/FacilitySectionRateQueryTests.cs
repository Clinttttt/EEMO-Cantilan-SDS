using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// A market section priced by the office itself, as PostgreSQL holds it and as the fee resolver reads it back.
///
/// <para>
/// The rule that picks a stall's daily fee is unit-tested against a snapshot built in memory. What only a real database
/// can answer is whether the rows we WRITE come back as that snapshot: the money column's precision, the effective date
/// as a date rather than a timestamp, and above all the tenant filter — one LGU's section rate must never price another
/// LGU's market, and the two can share a section name because each office names its own market.
/// </para>
///
/// <para>Runs against a throwaway container (see <see cref="PostgresFixture"/>). Skips, stating why, when there is no
/// container runtime.</para>
/// </summary>
[Collection(PostgresCollection.Name)]
public class FacilitySectionRateQueryTests(PostgresFixture db)
{
    /// <summary>Seeds one LGU with a market and a stall in a section of the office's own naming.</summary>
    private async Task<(Guid MunicipalityId, Guid StallId)> SeedAsync(string code, string name, string section)
    {
        var municipality = Municipality.Create(code, name, "Surigao del Sur", MunicipalityStatus.Active,
            tenantCode: code.ToLowerInvariant());

        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Municipalities.Add(municipality);
            await setup.SaveChangesAsync();
        }

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM", municipalityId: municipality.Id);
        facility.AddCustomSection(section);
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental,
            municipalityId: municipality.Id, customSectionName: section);

        await using (var tenant = db.CreateContext(municipality.Id))
        {
            tenant.Facilities.Add(facility);
            tenant.Stalls.Add(stall);
            await tenant.SaveChangesAsync();
        }

        return (municipality.Id, stall.Id);
    }

    [SkippableFact]
    public async Task AStatedSectionFeeIsReadBackAndPricesThatSectionsStall()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();

        var (municipalityId, stallId) = await SeedAsync("SDS-S1", "Section Municipality One", "Sari-sari Area");

        await using (var write = db.CreateContext(municipalityId))
        {
            // The market's own rate, and the section priced apart from it from the 20th.
            write.FacilityRates.Add(FacilityRate.Create(
                FacilityCode.NPM, FeeRateKey.NpmDailyStall, 30m, new DateOnly(2026, 1, 1), municipalityId));
            write.FacilitySectionRates.Add(FacilitySectionRate.Create(
                FacilityCode.NPM, "Sari-sari Area", 25.50m, new DateOnly(2026, 8, 20), municipalityId));
            await write.SaveChangesAsync();
        }

        await using var read = db.CreateContext(municipalityId);
        var snapshot = await new FeeRateResolver(read).GetSnapshotAsync();
        var stall = await read.Stalls.FirstAsync(s => s.Id == stallId);

        // Read back as the office stated it, to the centavo: numeric(18,2), not a rounded double.
        Assert.Equal(25.50m, snapshot.ResolveSectionOrNull(FacilityCode.NPM, "Sari-sari Area", new DateOnly(2026, 8, 20)));

        // And the day before it was stated is still the market's rate — the office's ruling, through the real database.
        Assert.Equal(30m, NpmDailyFee.ForStall(stall, snapshot, new DateOnly(2026, 8, 19)));
        Assert.Equal(25.50m, NpmDailyFee.ForStall(stall, snapshot, new DateOnly(2026, 8, 20)));
    }

    [SkippableFact]
    public async Task OneOfficesSectionRateNeverPricesAnothersMarket()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();

        // Both offices happen to call a section by the same name, which they are entitled to do.
        var (first, firstStall) = await SeedAsync("SDS-S2", "Section Municipality Two", "Sari-sari Area");
        var (second, secondStall) = await SeedAsync("SDS-S3", "Section Municipality Three", "Sari-sari Area");

        await using (var write = db.CreateContext(first))
        {
            write.FacilityRates.Add(FacilityRate.Create(
                FacilityCode.NPM, FeeRateKey.NpmDailyStall, 30m, new DateOnly(2026, 1, 1), first));
            write.FacilitySectionRates.Add(FacilitySectionRate.Create(
                FacilityCode.NPM, "Sari-sari Area", 25m, new DateOnly(2026, 8, 1), first));
            await write.SaveChangesAsync();
        }

        await using (var write = db.CreateContext(second))
        {
            write.FacilityRates.Add(FacilityRate.Create(
                FacilityCode.NPM, FeeRateKey.NpmDailyStall, 40m, new DateOnly(2026, 1, 1), second));
            await write.SaveChangesAsync();
        }

        await using (var read = db.CreateContext(first))
        {
            var snapshot = await new FeeRateResolver(read).GetSnapshotAsync();
            var stall = await read.Stalls.FirstAsync(s => s.Id == firstStall);
            Assert.Equal(25m, NpmDailyFee.ForStall(stall, snapshot, new DateOnly(2026, 8, 29)));
        }

        await using (var read = db.CreateContext(second))
        {
            var snapshot = await new FeeRateResolver(read).GetSnapshotAsync();
            var stall = await read.Stalls.FirstAsync(s => s.Id == secondStall);

            // The other office has stated nothing for its own section of that name, so its market's rate answers. It
            // must never inherit the first office's figure.
            Assert.Null(snapshot.ResolveSectionOrNull(FacilityCode.NPM, "Sari-sari Area", new DateOnly(2026, 8, 29)));
            Assert.Equal(40m, NpmDailyFee.ForStall(stall, snapshot, new DateOnly(2026, 8, 29)));
        }
    }

    [SkippableFact]
    public async Task OneSectionCannotHoldTwoRatesForOneDay()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();

        var (municipalityId, _) = await SeedAsync("SDS-S4", "Section Municipality Four", "Sari-sari Area");

        await using var write = db.CreateContext(municipalityId);
        write.FacilitySectionRates.Add(FacilitySectionRate.Create(
            FacilityCode.NPM, "Sari-sari Area", 25m, new DateOnly(2026, 8, 20), municipalityId));
        write.FacilitySectionRates.Add(FacilitySectionRate.Create(
            FacilityCode.NPM, "Sari-sari Area", 28m, new DateOnly(2026, 8, 20), municipalityId));

        // The database refuses it, so an edit landing on today's row must adjust that row rather than add a second — which
        // is what the handler does, and this is the guard behind it.
        await Assert.ThrowsAsync<DbUpdateException>(() => write.SaveChangesAsync());
    }

    [SkippableFact]
    public async Task ASectionsMeteringDefaultRoundTrips_AndBelongsToOneOfficeOnly()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();

        // Two offices, each entitled to a section of the same name.
        var (first, _) = await SeedAsync("SDS-U1", "Utility Municipality One", "Sari-sari Area");
        var (second, _) = await SeedAsync("SDS-U2", "Utility Municipality Two", "Sari-sari Area");

        await using (var write = db.CreateContext(first))
        {
            write.FacilitySectionUtilities.Add(FacilitySectionUtilities.Create(
                FacilityCode.NPM, "Sari-sari Area", electricity: true, water: false, municipalityId: first));
            await write.SaveChangesAsync();
        }

        await using (var read = db.CreateContext(first))
        {
            var row = Assert.Single(await read.FacilitySectionUtilities.ToListAsync());
            Assert.Equal("Sari-sari Area", row.SectionName);
            Assert.True(row.Electricity);
            Assert.False(row.Water);
        }

        await using (var read = db.CreateContext(second))
        {
            // The other office has said nothing about its own section of that name, and must not inherit this answer.
            Assert.Empty(await read.FacilitySectionUtilities.ToListAsync());
        }
    }

    [SkippableFact]
    public async Task OneSectionHoldsOneMeteringAnswer()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();

        var (municipalityId, _) = await SeedAsync("SDS-U3", "Utility Municipality Three", "Sari-sari Area");

        await using var write = db.CreateContext(municipalityId);
        write.FacilitySectionUtilities.Add(FacilitySectionUtilities.Create(
            FacilityCode.NPM, "Sari-sari Area", true, false, municipalityId));
        write.FacilitySectionUtilities.Add(FacilitySectionUtilities.Create(
            FacilityCode.NPM, "Sari-sari Area", false, true, municipalityId));

        // The database refuses a second answer for one section, which is why the handler sets the row it finds rather than
        // adding another. A default has no history to keep.
        await Assert.ThrowsAsync<DbUpdateException>(() => write.SaveChangesAsync());
    }

    [SkippableFact]
    public async Task AMeteringDefaultChangesNoStallAndNoFee()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();

        var (municipalityId, stallId) = await SeedAsync("SDS-U4", "Utility Municipality Four", "Sari-sari Area");

        await using (var write = db.CreateContext(municipalityId))
        {
            write.FacilityRates.Add(FacilityRate.Create(
                FacilityCode.NPM, FeeRateKey.NpmDailyStall, 30m, new DateOnly(2026, 1, 1), municipalityId));
            write.FacilitySectionUtilities.Add(FacilitySectionUtilities.Create(
                FacilityCode.NPM, "Sari-sari Area", true, true, municipalityId));
            await write.SaveChangesAsync();
        }

        await using var read = db.CreateContext(municipalityId);
        var stall = await read.Stalls.FirstAsync(s => s.Id == stallId);
        var snapshot = await new FeeRateResolver(read).GetSnapshotAsync();

        // The stall in that section keeps exactly the fees its own record carries, and its daily fee is untouched: the
        // meters belong to the space, and a default bills nothing.
        Assert.Equal(ApplicableFees.DailyRental, stall.Fees);
        Assert.Equal(30m, NpmDailyFee.ForStall(stall, snapshot, new DateOnly(2026, 8, 29)));
    }
}

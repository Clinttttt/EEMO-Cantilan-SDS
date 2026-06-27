using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Stall Holder List (List of Stallholders) — a base-rental record. Locks the reviewed fixes:
///   • "No. of Years" is the contract TERM (DurationYears), not years elapsed since effectivity.
///   • Monetary totals count ACTIVE stalls only (consistent with the active-stall count); closed
///     stalls are listed but excluded from the totals.
///   • It carries base rental only — no fish/electricity/water is folded into the figures.
/// </summary>
public class StallHoldersListTests : RepositoryTestBase
{
    [Fact]
    public async Task HoldersList_UsesContractTerm_ActiveOnlyTotals_AndBaseRentalOnly()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");

        // Effectivity this year with a 3-year TERM: years-elapsed (0) ≠ term (3) — proves the fix.
        var s1 = Stall.Create(facility.Id, "1", 2_760m, ApplicableFees.BaseRental);
        var s2 = Stall.Create(facility.Id, "2", 2_400m, ApplicableFees.BaseRental);
        var s3 = Stall.Create(facility.Id, "3", 2_400m, ApplicableFees.BaseRental);
        s3.Close(new DateOnly(2026, 1, 1));   // closed → listed, but excluded from monetary totals

        var c1 = Contract.Create(s1.Id, "Joseph Quinones", "Joseph Quinones", new DateOnly(2026, 1, 1), 3, 2_760m);
        var c2 = Contract.Create(s2.Id, "Marlex Dumagay", "Marlex Dumagay", new DateOnly(2026, 1, 1), 3, 2_400m);
        var c3 = Contract.Create(s3.Id, "Closed Lessee", "Closed Lessee", new DateOnly(2026, 1, 1), 3, 2_400m);

        context.AddRange(facility, s1, s2, s3, c1, c2, c3);
        await context.SaveChangesAsync();

        var repo = new StallRepository(context);
        var dto = await repo.GetStallHoldersListAsync(FacilityCode.TCC, null, null, CancellationToken.None);

        Assert.Equal(3, dto.TotalStalls);
        Assert.Equal(2, dto.GrandTotalActiveStalls);
        Assert.Equal(5_160m, dto.GrandTotalMonthlyRate);        // 2760 + 2400 (closed 2400 excluded)
        Assert.Equal(61_920m, dto.GrandTotalWholeYearRental);   // 5160 × 12

        var section = Assert.Single(dto.Sections);              // TCC has no market sections → one "All Stalls" block
        Assert.Equal("All Stalls", section.SectionName);
        Assert.Equal(5_160m, section.SectionMonthlyTotal);      // active-only
        Assert.Equal(0m, section.SectionFishFeeTotal);          // base rental only

        var row1 = section.Rows.Single(r => r.StallNo == "1");
        Assert.Equal(3, row1.DurationYears);                    // contract TERM, not (Today.Year − 2026) = 0
        Assert.Equal(2_760m, row1.MonthlyRentalRate);
        Assert.Equal(33_120m, row1.WholeYearRental);
        Assert.Null(row1.FishFeeTotal);                         // no additional fees folded in
    }
}

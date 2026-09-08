using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Stall Holder List (List of Stallholders) — a base-rental record. Locks the reviewed fixes:
///   • "No. of Years" is the contract TERM (DurationYears), not years elapsed since effectivity.
///   • Monetary totals count ACTIVE stalls only (consistent with the active-stall count); closed
///     accounts are excluded from the roster entirely (they remain in the transaction history).
///   • It carries base rental only — no fish/electricity/water is folded into the figures.
/// </summary>
public class StallHoldersListTests : RepositoryTestBase
{
    /// <summary>
    /// Reading an earlier year names who held the stall THEN, including a stall since closed.
    /// </summary>
    /// <remarks>
    /// Asked for 2026-09-06 so the office can answer "who held the stalls in 2019?" at a panel. It could not: the roster
    /// lists current holders and drops closed and expired accounts, so filtering the rows it returns would never bring back
    /// somebody who has left. The year is therefore an AS-OF point read against the contract history, not a filter.
    ///
    /// <para>Both directions are asserted, because the risk in this change is not the new answer but the old one: the year
    /// defaults to null and the current year means today, so the register the office opens every day must be exactly what it
    /// was. A first lessee who left, a second who followed, and a stall closed since - the 2019 read names the first, and
    /// today's read names neither.</para>
    /// </remarks>
    [Fact]
    public async Task HoldersList_AnEarlierYear_NamesWhoHeldTheStallThen()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");

        // One space, let twice: the first lessee through 2019, the second from 2021. The stall is CLOSED now, so today's
        // roster excludes it altogether - which is why the 2019 question cannot be answered by filtering.
        var stall = Stall.Create(facility.Id, "1", 2_400m, ApplicableFees.BaseRental);
        var first = Contract.Create(stall.Id, "Nineteen Lessee", "Nineteen Lessee", new DateOnly(2018, 1, 1), 3, 2_400m);
        var second = Contract.Create(stall.Id, "Later Lessee", "Later Lessee", new DateOnly(2021, 1, 1), 3, 2_400m);
        stall.Close(new DateOnly(2025, 6, 30));

        context.AddRange(facility, stall, first, second);
        await context.SaveChangesAsync();

        var repo = new StallRepository(context);

        var y2019 = await repo.GetStallHoldersListAsync(FacilityCode.TCC, null, null, 2019, CancellationToken.None);
        var occupants2019 = y2019.Sections.SelectMany(s => s.Rows).Select(r => r.ActualOccupant).ToList();
        Assert.Contains("Nineteen Lessee", occupants2019);
        Assert.DoesNotContain("Later Lessee", occupants2019);

        // Today is unchanged: a closed stall is still off the roster, exactly as before this feature existed.
        var current = await repo.GetStallHoldersListAsync(FacilityCode.TCC, null, null, null, CancellationToken.None);
        Assert.Empty(current.Sections.SelectMany(s => s.Rows));
    }

    /// <summary>
    /// A stall that changed hands during the year names whoever held it at the year's CLOSE, and a stall vacant by then is absent.
    /// </summary>
    /// <remarks>
    /// Found by audit 2026-09-07, in the commit that added this feature. The as-of read used a whole-year window and took the first
    /// match, and occupancy windows are ordered OLDEST first - so a stall re-let in April 2019 was reported under the lessee who
    /// left in March, and a stall let only in January and vacant afterwards still appeared on a 31 December roster. Both are the
    /// same mistake: a snapshot was being answered with a range.
    /// </remarks>
    [Fact]
    public async Task HoldersList_AnEarlierYear_NamesTheHolderAtTheYearsClose()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");

        // Changed hands mid-2019: the first lessee's term is cut short by the second's effectivity.
        var reLet = Stall.Create(facility.Id, "1", 2_400m, ApplicableFees.BaseRental);
        var left = Contract.Create(reLet.Id, "Left In March", "Left In March", new DateOnly(2018, 1, 1), 5, 2_400m);
        var sitting = Contract.Create(reLet.Id, "Held At Year End", "Held At Year End", new DateOnly(2019, 4, 1), 5, 2_400m);

        // Let for one year only, so it was standing empty by 31 December 2019.
        var vacated = Stall.Create(facility.Id, "2", 2_400m, ApplicableFees.BaseRental);
        var briefly = Contract.Create(vacated.Id, "Gone By June", "Gone By June", new DateOnly(2018, 1, 1), 1, 2_400m);

        context.AddRange(facility, reLet, left, sitting, vacated, briefly);
        await context.SaveChangesAsync();

        var repo = new StallRepository(context);
        var y2019 = await repo.GetStallHoldersListAsync(FacilityCode.TCC, null, null, 2019, CancellationToken.None);
        var occupants = y2019.Sections.SelectMany(s => s.Rows).Select(r => r.ActualOccupant).ToList();

        Assert.Contains("Held At Year End", occupants);
        Assert.DoesNotContain("Left In March", occupants);
        Assert.DoesNotContain("Gone By June", occupants);
    }

    /// <summary>
    /// A same-day handover names the incoming lessee, not the one who left that morning.
    /// </summary>
    /// <remarks>
    /// Found by a second audit 2026-09-08, in the fix from the first. <c>Stall.Occupancies</c> clamps a window that would otherwise end
    /// before it starts, so where one term ends the very day the next begins the outgoing window's end lands on the incoming window's
    /// start and the two overlap on exactly that day. The as-of read took the FIRST of them, and windows come oldest first — so on that
    /// one day the roster named the departing lessee.
    ///
    /// <para>My own comment on the earlier fix asserted these windows never overlap. They can, and this is the case.</para>
    /// </remarks>
    [Fact]
    public async Task HoldersList_OnASameDayHandover_NamesTheIncomingLessee()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");

        // BOTH terms dated the same day, which is the case Stall.Occupancies calls "bad data, or a same-day handover". That is what
        // makes the clamp fire: the outgoing window is trimmed to the day before the next term, which is before its own start, so it
        // is pushed back onto its start — the same day the incoming window begins. Only then do two windows cover one day.
        //
        // My first two drafts dated the terms two years apart and proved nothing: the outgoing window simply ended the day before, so
        // one window covered the as-of date and either end of the list gave the right answer.
        var handover = new DateOnly(2020, 12, 31);
        var stall = Stall.Create(facility.Id, "1", 2_400m, ApplicableFees.BaseRental);
        var outgoing = Contract.Create(stall.Id, "Left That Morning", "Left That Morning", handover, 5, 2_400m);
        var incoming = Contract.Create(stall.Id, "Took Over", "Took Over", handover, 5, 2_400m);

        context.AddRange(facility, stall, outgoing, incoming);
        await context.SaveChangesAsync();

        var repo = new StallRepository(context);

        // Read as of the close of 2020 — the handover day, where both windows cover the stall.
        var y2020 = await repo.GetStallHoldersListAsync(FacilityCode.TCC, null, null, 2020, CancellationToken.None);
        var occupants = y2020.Sections.SelectMany(s => s.Rows).Select(r => r.ActualOccupant).ToList();

        Assert.Contains("Took Over", occupants);
        Assert.DoesNotContain("Left That Morning", occupants);
    }

    /// <summary>The current year means today, so the everyday view cannot drift from the no-year one.</summary>
    [Fact]
    public async Task HoldersList_TheCurrentYear_ReadsTheSameAsNoYearAtAll()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");

        var stall = Stall.Create(facility.Id, "1", 2_400m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Sitting Lessee", "Sitting Lessee", new DateOnly(2026, 1, 1), 3, 2_400m);

        context.AddRange(facility, stall, contract);
        await context.SaveChangesAsync();

        var repo = new StallRepository(context);
        var thisYear = PhilippineTime.Today.Year;

        var withYear = await repo.GetStallHoldersListAsync(FacilityCode.TCC, null, null, thisYear, CancellationToken.None);
        var withoutYear = await repo.GetStallHoldersListAsync(FacilityCode.TCC, null, null, null, CancellationToken.None);

        Assert.Equal(withoutYear.TotalStalls, withYear.TotalStalls);
        Assert.Equal(withoutYear.GrandTotalMonthlyRate, withYear.GrandTotalMonthlyRate);
        Assert.Equal(
            withoutYear.Sections.SelectMany(s => s.Rows).Select(r => r.ActualOccupant),
            withYear.Sections.SelectMany(s => s.Rows).Select(r => r.ActualOccupant));
    }

    [Fact]
    public async Task HoldersList_UsesContractTerm_ActiveOnlyTotals_AndBaseRentalOnly()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");

        // Effectivity this year with a 3-year TERM: years-elapsed (0) ≠ term (3) — proves the fix.
        var s1 = Stall.Create(facility.Id, "1", 2_760m, ApplicableFees.BaseRental);
        var s2 = Stall.Create(facility.Id, "2", 2_400m, ApplicableFees.BaseRental);
        var s3 = Stall.Create(facility.Id, "3", 2_400m, ApplicableFees.BaseRental);
        s3.Close(new DateOnly(2026, 1, 1));   // closed → excluded from the roster entirely

        var c1 = Contract.Create(s1.Id, "Joseph Quinones", "Joseph Quinones", new DateOnly(2026, 1, 1), 3, 2_760m);
        var c2 = Contract.Create(s2.Id, "Marlex Dumagay", "Marlex Dumagay", new DateOnly(2026, 1, 1), 3, 2_400m);
        var c3 = Contract.Create(s3.Id, "Closed Lessee", "Closed Lessee", new DateOnly(2026, 1, 1), 3, 2_400m);

        context.AddRange(facility, s1, s2, s3, c1, c2, c3);
        await context.SaveChangesAsync();

        var repo = new StallRepository(context);
        var dto = await repo.GetStallHoldersListAsync(FacilityCode.TCC, null, null, null, CancellationToken.None);

        Assert.Equal(2, dto.TotalStalls);                      // closed stall excluded from the roster
        Assert.Equal(2, dto.GrandTotalActiveStalls);
        Assert.Equal(5_160m, dto.GrandTotalMonthlyRate);        // 2760 + 2400 (closed 2400 excluded)
        Assert.Equal(61_920m, dto.GrandTotalWholeYearRental);   // 5160 × 12

        var section = Assert.Single(dto.Sections);              // TCC has no market sections → one "All Stalls" block
        Assert.Equal("All Stalls", section.SectionName);
        Assert.DoesNotContain(section.Rows, r => r.StallNo == "3");   // closed account is not listed
        Assert.Equal(5_160m, section.SectionMonthlyTotal);      // active-only
        Assert.Equal(0m, section.SectionFishFeeTotal);          // base rental only

        var row1 = section.Rows.Single(r => r.StallNo == "1");
        Assert.Equal(3, row1.DurationYears);                    // contract TERM, not (Today.Year − 2026) = 0
        Assert.Equal(2_760m, row1.MonthlyRentalRate);
        Assert.Equal(33_120m, row1.WholeYearRental);
        Assert.Null(row1.FishFeeTotal);                         // no additional fees folded in
    }

    [Fact]
    public async Task HoldersList_ExcludesExpiredContractStalls_KeepsCoveredAndFutureDated()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");

        var covered = Stall.Create(facility.Id, "1", 2_400m, ApplicableFees.BaseRental);
        var expired = Stall.Create(facility.Id, "2", 2_400m, ApplicableFees.BaseRental);
        var future = Stall.Create(facility.Id, "3", 2_400m, ApplicableFees.BaseRental);

        // Covered: 2-yr-ago start with a 5-yr term → still effective today → kept.
        var cCovered = Contract.Create(covered.Id, "Active Lessee", "Active Lessee", new DateOnly(2024, 1, 1), 5, 2_400m);
        // Expired: 2022 start, 3-yr term → ended 2025-01-01 (before today) → excluded.
        var cExpired = Contract.Create(expired.Id, "Expired Lessee", "Expired Lessee", new DateOnly(2022, 1, 1), 3, 2_400m);
        // Future-dated: starts far in the future → NOT expired (term hasn't ended) → kept.
        var cFuture = Contract.Create(future.Id, "Future Lessee", "Future Lessee", new DateOnly(2099, 1, 1), 3, 2_400m);

        context.AddRange(facility, covered, expired, future, cCovered, cExpired, cFuture);
        await context.SaveChangesAsync();

        var repo = new StallRepository(context);
        var dto = await repo.GetStallHoldersListAsync(FacilityCode.TCC, null, null, null, CancellationToken.None);

        var section = Assert.Single(dto.Sections);
        Assert.DoesNotContain(section.Rows, r => r.StallNo == "2");   // expired contract → excluded
        Assert.Contains(section.Rows, r => r.StallNo == "1");         // covered → kept
        Assert.Contains(section.Rows, r => r.StallNo == "3");         // future-dated → kept (not expired)
        Assert.Equal(2, dto.TotalStalls);
        Assert.Equal(4_800m, dto.GrandTotalMonthlyRate);              // covered + future only
    }
}

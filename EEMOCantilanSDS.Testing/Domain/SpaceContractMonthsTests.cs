using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing;

/// <summary>The one month-aware term rule: start + (years x 12 + months) months, minus one day. Yearly terms must not move.</summary>
public sealed class SpaceContractMonthsTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Theory]
    [InlineData(2026, 10, 1, 1, 2026, 10, 31)]
    [InlineData(2026, 10, 1, 3, 2026, 12, 31)]
    [InlineData(2026, 10, 1, 6, 2027, 3, 31)]
    [InlineData(2026, 10, 1, 12, 2027, 9, 30)]
    [InlineData(2026, 1, 1, 2, 2026, 2, 28)]
    [InlineData(2028, 1, 1, 2, 2028, 2, 29)]              // leap February is the last day inside a two-month term
    [InlineData(2026, 10, 15, 6, 2027, 4, 14)]
    [InlineData(2026, 1, 31, 1, 2026, 2, 27)]            // Jan 31 + 1 month clamps to Feb 28, so the last day inside the term is Feb 27
    [InlineData(2028, 1, 31, 1, 2028, 2, 28)]            // ... and to Feb 29 in a leap year
    [InlineData(2026, 8, 31, 6, 2027, 2, 27)]            // Aug 31 + 6 months clamps to Feb 28
    [InlineData(2027, 8, 31, 6, 2028, 2, 28)]            // ... Feb 29 in a leap year
    [InlineData(2026, 8, 30, 6, 2027, 2, 27)]
    [InlineData(2028, 2, 29, 12, 2029, 2, 27)]           // a Feb 29 start, 12 months: anniversary clamps to Feb 28 2029
    [InlineData(2027, 2, 28, 12, 2028, 2, 27)]
    [InlineData(2027, 3, 1, 12, 2028, 2, 29)]            // 12 months across a leap year ends on Feb 29
    [InlineData(2027, 12, 31, 3, 2028, 3, 30)]
    public void MonthsOnlyTermLastDayIsStartPlusMonthsMinusOneDay(int y, int m, int d, int months, int ey, int em, int ed)
        => Assert.Equal(D(ey, em, ed), DomainRules.TermLastDay(D(y, m, d), 0, months));

    [Fact]
    public void YearsAndMonthsCombine()
    {
        Assert.Equal(D(2028, 3, 31), DomainRules.TermLastDay(D(2026, 10, 1), 1, 6));   // 18 months
        Assert.Equal(D(2029, 4, 14), DomainRules.TermLastDay(D(2026, 10, 15), 2, 6));  // 30 months
        Assert.Equal(DomainRules.TermLastDay(D(2026, 10, 1), 0, 18), DomainRules.TermLastDay(D(2026, 10, 1), 1, 6));
    }

    [Fact]
    public void YearsOnlyEqualsTheOldResultForEveryDateOfATable()
    {
        // Every month-end, leap day and ordinary day, for every legal yearly term: months null (and a 12 x N months term) never moves the expiry.
        var starts = new List<DateOnly>();
        for (var year = 2020; year <= 2032; year++)
            foreach (var month in Enumerable.Range(1, 12))
                foreach (var day in new[] { 1, 15, 28, 29, 30, 31 })
                    if (day <= DateTime.DaysInMonth(year, month)) starts.Add(D(year, month, day));
        Assert.True(starts.Count > 800);
        foreach (var start in starts)
            foreach (var years in new[] { 1, 2, 3, 5, 10, 25, 50 })
            {
                var old = start.AddYears(years).AddDays(-1);                          // the formula in force before months existed
                Assert.Equal(old, DomainRules.TermLastDay(start, years));
                Assert.Equal(old, DomainRules.TermLastDay(start, years, null));
                Assert.Equal(old, DomainRules.TermLastDay(start, years, 0));
                Assert.Equal(old, DomainRules.TermLastDay(start, 0, years * 12));      // N years == N x 12 months, exactly
            }
    }

    [Fact]
    public void MonthAwareDateTimeOverloadAgreesWithTheDateOnlyOne()
        => Assert.Equal(new DateTime(2027, 3, 31), DomainRules.TermLastDay(new DateTime(2026, 10, 1, 13, 0, 0), 0, 6));

    [Theory]
    [InlineData(null, null, null, null)]
    [InlineData(3, null, 3, null)]
    [InlineData(null, 6, 0, 6)]
    [InlineData(0, 6, 0, 6)]
    [InlineData(null, 12, 1, null)]                       // 12 months is one year, one canonical record
    [InlineData(1, 6, 1, 6)]
    [InlineData(1, 12, 2, null)]
    [InlineData(null, 18, 1, 6)]
    [InlineData(50, null, 50, null)]
    [InlineData(null, 600, 50, null)]
    public void NormalizeGivesOneCanonicalShape(int? years, int? months, int? expectedYears, int? expectedMonths)
        => Assert.Equal((expectedYears, expectedMonths), DomainRules.NormalizeSpaceTerm(years, months));

    [Theory]
    [InlineData(0, null)]                                 // the legacy rejection of 0 years stays
    [InlineData(51, null)]
    [InlineData(-1, null)]
    [InlineData(null, 0)]                                 // a zero-length term
    [InlineData(0, 0)]
    [InlineData(null, -1)]
    [InlineData(50, 1)]                                   // longer than 50 years
    [InlineData(null, 601)]
    public void NormalizeRejectsInvalidTerms(int? years, int? months)
        => Assert.Throws<ArgumentException>(() => DomainRules.NormalizeSpaceTerm(years, months));

    [Theory]
    [InlineData(3, null, "3 years")]
    [InlineData(1, null, "1 year")]
    [InlineData(0, 6, "6 months")]
    [InlineData(0, 1, "1 month")]
    [InlineData(1, 6, "1 year 6 months")]
    [InlineData(null, null, null)]
    public void DescribeTermIsPlainWording(int? years, int? months, string? expected)
        => Assert.Equal(expected, DomainRules.DescribeTerm(years, months));

    private static ObligationAccount Signed(string subject = "1")
    {
        var start = new DateOnly(2026, 10, 1);
        var account = ObligationAccount.Create(Guid.NewGuid(), ObligationKind.KanmanggaySpaceRental,
            Guid.Empty, null, subject, null, null, start, "Head", actualOccupant: "Holder");
        account.SetOccupancyBasis(OccupancyArrangement.SignedContract, "Ref");
        return account;
    }

    [Fact]
    public void AccountRecordsAMonthsOnlyTermAndLeavesYearlyOnesNull()
    {
        var months = Signed();
        months.SetLeaseDetails("Lessee", new DateOnly(2026, 10, 1), null, null, null, null, 6);
        Assert.Equal(0, months.ContractYears);
        Assert.Equal(6, months.ContractMonths);

        var yearly = Signed();
        yearly.SetLeaseDetails("Lessee", new DateOnly(2026, 10, 1), 3, null, null, null);
        Assert.Equal(3, yearly.ContractYears);
        Assert.Null(yearly.ContractMonths);

        var twelve = Signed();
        twelve.SetLeaseDetails("Lessee", new DateOnly(2026, 10, 1), null, null, null, null, 12);
        Assert.Equal(1, twelve.ContractYears);
        Assert.Null(twelve.ContractMonths);
    }

    [Fact]
    public void AccountRejectsAZeroOrOverlongTermAndMonthsWithoutASignedContract()
    {
        var a = Signed();
        Assert.Throws<ArgumentException>(() => a.SetLeaseDetails("L", new DateOnly(2026, 10, 1), 0, null, null, null, 0));
        Assert.Throws<ArgumentException>(() => a.SetLeaseDetails("L", new DateOnly(2026, 10, 1), 50, null, null, null, 1));
        Assert.Throws<ArgumentException>(() => a.SetLeaseDetails("L", new DateOnly(2026, 10, 1), null, null, null, null, -2));

        var spaceOnly = ObligationAccount.Create(Guid.NewGuid(), ObligationKind.KanmanggaySpaceRental,
            Guid.Empty, null, "2", null, null, new DateOnly(2026, 10, 1), "Head", actualOccupant: "Holder");
        spaceOnly.SetOccupancyBasis(OccupancyArrangement.SpaceOnly, null);
        Assert.Throws<ArgumentException>(() => spaceOnly.SetLeaseDetails(null, null, null, null, null, null, 6));
    }

    [Fact]
    public void LockedTermComparesInCanonicalFormAfterAssessment()
    {
        var a = Signed();
        a.SetLeaseDetails("L", new DateOnly(2026, 10, 1), 1, null, null, null);
        // "12 months" is the same recorded term as "1 year": allowed even after assessment. A different term is refused.
        a.CorrectHolderEvidence("Holder", OccupancyArrangement.SignedContract, "Ref", "L", new DateOnly(2026, 10, 1), null, null, null, hasAssessedPeriods: true, months: 12);
        Assert.Throws<ArgumentException>(() => a.CorrectHolderEvidence("Holder", OccupancyArrangement.SignedContract, "Ref", "L", new DateOnly(2026, 10, 1), null, null, null, hasAssessedPeriods: true, months: 6));
    }

    [Fact]
    public void AppendedTermLastDayIsMonthAware()
    {
        var term = SpaceContractTerm.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, new DateOnly(2027, 4, 1), 0,
            "L", "R", "Resolution 1", Guid.NewGuid(), Guid.NewGuid(), "Head", DateTime.UtcNow, 6);
        Assert.Equal(0, term.Years);
        Assert.Equal(6, term.Months);
        Assert.Equal(new DateOnly(2027, 9, 30), term.LastDay);

        var yearly = SpaceContractTerm.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, new DateOnly(2027, 4, 1), 3,
            "L", "R", "Resolution 1", Guid.NewGuid(), Guid.NewGuid(), "Head", DateTime.UtcNow);
        Assert.Null(yearly.Months);
        Assert.Equal(new DateOnly(2030, 3, 31), yearly.LastDay);

        Assert.Throws<ArgumentException>(() => SpaceContractTerm.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, new DateOnly(2027, 4, 1), 0,
            "L", "R", "Resolution 1", Guid.NewGuid(), Guid.NewGuid(), "Head", DateTime.UtcNow));
    }
}

using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Money paid for a month stays with that month, even when the office excuses it.
/// </summary>
/// <remarks>
/// Ruled on by the office 2026-09-07, from its own worked example: September owes ₱900, ₱500 has been paid, ₱400 remains, and the
/// office then excuses September. The ₱500 belongs to September. Only the ₱400 that REMAINS is excused, and October must not be
/// touched.
///
/// <para>What the code did instead: an excused month was skipped entirely when the obligation was totalled, while the payment
/// against it stayed in the amount-paid total for the same period. The obligation lost ₱900 and kept ₱500 of credit, so the
/// surplus came off whatever else the payor owed - the ₱500 reduced October. That is money crossing periods in a government
/// ledger, which no report will show as an error and no office can find afterwards.</para>
///
/// <para>The rule is now expressed by billing an excused month exactly what it was paid, so obligation and payment cancel inside
/// their own month and no surplus exists to travel.</para>
/// </remarks>
public class ExcusedMonthKeepsItsOwnMoneyTests : RepositoryTestBase
{
    private const decimal Rent = 900m;

    /// <summary>Builds a TCC stall let from January, with a payment record per month named.</summary>
    private static (Facility facility, Stall stall, Contract contract) Account()
    {
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "1", Rent, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Marlex Dumagay", "Marlex Dumagay", new DateOnly(2026, 1, 1), 3, Rent);
        return (facility, stall, contract);
    }

    private static PaymentRecord PartPaid(Guid stallId, int year, int month, decimal paid)
    {
        var record = PaymentRecord.Create(stallId, year, month, Rent);
        record.UpdateStatus(PaymentStatus.Partial, paid);
        return record;
    }

    /// <summary>
    /// The office's own example: excusing a part-paid September leaves October exactly as it was.
    /// </summary>
    /// <remarks>
    /// Asserted as a COMPARISON against the same account without the excusal, because the number that matters is not September's
    /// balance in isolation - it is that October's did not move. An absolute figure would have passed even while the money drifted.
    /// </remarks>
    [Fact]
    public async Task ExcusingAPartPaidMonth_DoesNotReduceWhatAnotherMonthOwes()
    {
        // ── Without the excusal ──
        var plain = NewContext();
        var (f1, s1, c1) = Account();
        plain.AddRange(f1, s1, c1, PartPaid(s1.Id, 2026, 9, 500m));
        await plain.SaveChangesAsync();

        var before = await new FacilityReportsRepository(plain).GetFacilityReportsAsync(
            FacilityCode.TCC, ReportPeriod.Yearly, 2026, null, null, CancellationToken.None);

        // ── The same account, with September excused ──
        var excused = NewContext();
        var (f2, s2, c2) = Account();
        excused.AddRange(f2, s2, c2, PartPaid(s2.Id, 2026, 9, 500m));
        excused.StallMonthlyExceptions.Add(StallMonthlyException.Create(
            s2.Id, 2026, 9, MonthlyExceptionReason.TemporaryClosure, "Excused by the office", "tester"));
        await excused.SaveChangesAsync();

        var after = await new FacilityReportsRepository(excused).GetFacilityReportsAsync(
            FacilityCode.TCC, ReportPeriod.Yearly, 2026, null, null, CancellationToken.None);

        // Excusing September forgives the ₱400 that remained on it, and NOTHING else.
        var forgiven = before.PendingPaymentAmount - after.PendingPaymentAmount;

        Assert.Equal(400m, forgiven);
    }

    /// <summary>
    /// An excused month with nothing paid on it is still forgiven in full, as it always was.
    /// </summary>
    /// <remarks>
    /// The other half of the rule, and the reason the fix is not simply "stop skipping excused months". An excusal has to excuse
    /// something; a month nobody paid toward owes nothing after it, and this is the ordinary case - a closure, or an office
    /// waiving a month outright.
    /// </remarks>
    [Fact]
    public async Task ExcusingAMonthWithNothingPaidStillForgivesTheWholeMonth()
    {
        var plain = NewContext();
        var (f1, s1, c1) = Account();
        plain.AddRange(f1, s1, c1);
        await plain.SaveChangesAsync();

        var before = await new FacilityReportsRepository(plain).GetFacilityReportsAsync(
            FacilityCode.TCC, ReportPeriod.Yearly, 2026, null, null, CancellationToken.None);

        var excused = NewContext();
        var (f2, s2, c2) = Account();
        excused.AddRange(f2, s2, c2);
        excused.StallMonthlyExceptions.Add(StallMonthlyException.Create(
            s2.Id, 2026, 9, MonthlyExceptionReason.TemporaryClosure, "Excused by the office", "tester"));
        await excused.SaveChangesAsync();

        var after = await new FacilityReportsRepository(excused).GetFacilityReportsAsync(
            FacilityCode.TCC, ReportPeriod.Yearly, 2026, null, null, CancellationToken.None);

        // The whole month's rent, because none of it had been paid.
        Assert.Equal(Rent, before.PendingPaymentAmount - after.PendingPaymentAmount);
    }

    /// <summary>
    /// A part payment is credited to rent first, so excusing the rent does not also forgive a light or water bill.
    /// </summary>
    /// <remarks>
    /// A rent exception excuses rent. Crediting the payment to utilities first would have made both settle and quietly written off
    /// a bill the office never waived - which is the same class of mistake as the drift, in the opposite direction.
    /// </remarks>
    /// <summary>
    /// Excusing a month forgives its electricity and water along with its rent.
    /// </summary>
    /// <remarks>
    /// The office's ruling, 2026-09-08: a payor excused for a month is not billed for that month — utilities included. Where they do
    /// owe a light or water bill they can still pay it through the collector or the office; what an excusal removes is the OBLIGATION,
    /// not the ability to settle.
    ///
    /// <para>THIS TEST PREVIOUSLY ASSERTED THE OPPOSITE, on my own reasoning that a rent exception should not forgive a light bill. An
    /// audit then found that three of the four readers of the excused set already forgave the utilities and only the report did not, so
    /// the office's screens disagreed with each other. Asked to rule, the office chose the majority behaviour. The figure changed from
    /// ₱400 to ₱600 for that reason and no other.</para>
    ///
    /// <para>Still asserted as a COMPARISON, because the number that matters is not this month's balance but that no OTHER month moved:
    /// dropping a month's bill while its payment stays in the period total is how money drifts, and it would drift here for the
    /// utilities exactly as it once did for the rent.</para>
    /// </remarks>
    [Fact]
    public async Task ExcusingAMonthForgivesItsUtilitiesToo()
    {
        // ── Without the excusal ──
        var plain = NewContext();
        var (f1, s1, c1) = Account();
        plain.AddRange(f1, s1, c1, PartPaidWithElectricity(s1.Id));
        await plain.SaveChangesAsync();

        var before = await new FacilityReportsRepository(plain).GetFacilityReportsAsync(
            FacilityCode.TCC, ReportPeriod.Yearly, 2026, null, null, CancellationToken.None);

        // ── The same account, September excused ──
        var excused = NewContext();
        var (f2, s2, c2) = Account();
        excused.AddRange(f2, s2, c2, PartPaidWithElectricity(s2.Id));
        excused.StallMonthlyExceptions.Add(StallMonthlyException.Create(
            s2.Id, 2026, 9, MonthlyExceptionReason.TemporaryClosure, "Excused by the office", "tester"));
        await excused.SaveChangesAsync();

        var after = await new FacilityReportsRepository(excused).GetFacilityReportsAsync(
            FacilityCode.TCC, ReportPeriod.Yearly, 2026, null, null, CancellationToken.None);

        // ₱400 of rent still owed on that month, plus its ₱200 of electricity. The ₱500 already paid stays with September and reduces
        // nothing else — if it drifted, this difference would exceed ₱600.
        Assert.Equal(600m, before.PendingPaymentAmount - after.PendingPaymentAmount);
    }

    /// <summary>₱500 taken against ₱900 rent, on a month that also carries ₱200 of electricity.</summary>
    private static PaymentRecord PartPaidWithElectricity(Guid stallId)
    {
        var record = PaymentRecord.Create(stallId, 2026, 9, Rent);
        record.RecordPayment(
            orNumber: "OR-9",
            collectorId: Guid.NewGuid(),
            status: PaymentStatus.Partial,
            partialAmount: 500m,
            elecReading: null,
            elecAmount: 200m,
            updatedBy: "tester");
        return record;
    }
}

using EEMOCantilanSDS.Application.Command.Payments.BulkImportPaymentHistory;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Payments;

/// <summary>
/// Recording an office's existing payment history, so it can adopt the system mid-contract without waiting for its
/// terms to run out.
///
/// <para>
/// Importing the history rather than an opening balance keeps arrears DERIVED - what a payor owes goes on being
/// worked out from the term and the payments against it - so there is no second figure to trust and every peso
/// traces to a receipt. These tests hold the four things that would otherwise turn a history import into a
/// thousand small errors.
/// </para>
/// </summary>
public class BulkImportPaymentHistoryTests
{
    private static readonly Guid StallId = Guid.NewGuid();

    private static ImportPaymentRow Row(
        int n, string stallNo, int year, int month, decimal amount, string? or = "OR-1001") =>
        new(n, stallNo, "Joseph Villamor", year, month, amount, or, new DateTime(year, month, 5));

    /// <summary>A TCC space let at 2,400 a month on a three-year term from January 2024.</summary>
    private static Stall MonthlyStall(string stallNo = "1", decimal rent = 2_400m)
    {
        var stall = Stall.Create(Guid.NewGuid(), stallNo, rent, ApplicableFees.BaseRental);
        typeof(Stall).GetProperty(nameof(Stall.Id))!.SetValue(stall, StallId);

        var contract = Contract.Create(
            stall.Id, "Joseph Villamor", "Joseph Villamor",
            new DateOnly(2024, 1, 1), durationYears: 3, monthlyRate: rent);
        stall.Contracts.Add(contract);
        return stall;
    }

    private static (BulkImportPaymentHistoryCommandHandler Handler, List<PaymentRecord> Added) Build(
        Stall? stall = null,
        PaymentRecordDtoStub? existing = null)
    {
        var theStall = stall ?? MonthlyStall();

        var stalls = new Mock<IStallRepository>();
        stalls.Setup(s => s.GetStallsWithContractsByFacilityAsync(
                It.IsAny<FacilityCode>(), It.IsAny<MarketSection?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { theStall });

        var added = new List<PaymentRecord>();
        var payments = new Mock<IPaymentRepository>();
        payments.Setup(p => p.GetPaymentRecordAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, int y, int m, CancellationToken _) =>
                existing is not null && existing.Year == y && existing.Month == m ? existing.Dto : null);
        payments.Setup(p => p.AddAsync(It.IsAny<PaymentRecord>(), It.IsAny<CancellationToken>()))
            .Callback<PaymentRecord, CancellationToken>((r, _) => added.Add(r))
            .Returns(Task.CompletedTask);

        var facilities = new Mock<IFacilityRepository>();
        facilities.Setup(f => f.GetByCodeAsync(It.IsAny<FacilityCode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC"));

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        return (new BulkImportPaymentHistoryCommandHandler(
            stalls.Object, payments.Object, facilities.Object, uow.Object,
            CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant), added);
    }

    private static BulkImportPaymentHistoryCommand Command(params ImportPaymentRow[] rows) =>
        new(FacilityCode.TCC, null, rows);

    [Fact]
    public async Task AFullMonthsRentIsRecordedAsPaid()
    {
        var (handler, added) = Build();

        var result = await handler.Handle(Command(Row(1, "1", 2024, 3, 2_400m)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.RecordedCount);
        var record = Assert.Single(added);
        Assert.Equal(PaymentStatus.Paid, record.Status);
        Assert.Equal(2_400m, record.BaseRentalAmount);
        Assert.Equal(2024, record.BillingYear);
        Assert.Equal(3, record.BillingMonth);
    }

    [Fact]
    public async Task AShortPaymentStaysOutstandingForTheRemainder()
    {
        // The defect this guards against has already happened once in this system: a single day's fee marked a whole
        // month settled. An import that recorded every row as Paid would repeat it across an entire history at once.
        var (handler, added) = Build();

        var result = await handler.Handle(Command(Row(1, "1", 2024, 3, 900m)), CancellationToken.None);

        Assert.Equal(0, result.Value!.RecordedCount);
        Assert.Equal(1, result.Value!.PartialCount);

        var record = Assert.Single(added);
        Assert.Equal(PaymentStatus.Partial, record.Status);
        Assert.Equal(900m, record.PartialAmount);
        Assert.Equal(2_400m, record.BaseRentalAmount);
        Assert.Equal(1_500m, record.BalanceDue);   // the month is still owed the difference
    }

    [Fact]
    public async Task ReImportingTheSameHistoryDoesNotDoubleTheMonth()
    {
        var alreadyThere = new PaymentRecordDtoStub(2024, 3);
        var (handler, added) = Build(existing: alreadyThere);

        var result = await handler.Handle(Command(Row(1, "1", 2024, 3, 2_400m)), CancellationToken.None);

        Assert.Empty(added);
        Assert.Equal(1, result.Value!.AlreadyRecordedCount);
        Assert.Contains("already recorded", result.Value!.Results[0].Error);
    }

    [Fact]
    public async Task TheSameSpaceAndMonthTwiceInOneFileIsRecordedOnce()
    {
        // The database check cannot catch this: neither row exists yet when the file is read.
        var (handler, added) = Build();

        var result = await handler.Handle(
            Command(Row(1, "1", 2024, 3, 2_400m), Row(2, "1", 2024, 3, 2_400m)), CancellationToken.None);

        Assert.Single(added);
        Assert.Equal(1, result.Value!.RecordedCount);
        Assert.Equal(1, result.Value!.AlreadyRecordedCount);
    }

    [Fact]
    public async Task APaymentForAMonthTheTermDoesNotBillIsRejected()
    {
        // Before the term started. Recording it would put money against a month that never existed, and the arrears
        // engine would then disagree with the ledger for reasons nobody could trace.
        var (handler, added) = Build();

        var result = await handler.Handle(Command(Row(1, "1", 2023, 6, 2_400m)), CancellationToken.None);

        Assert.Empty(added);
        Assert.Equal(1, result.Value!.RejectedCount);
        Assert.Contains("answers for 2023-06", result.Value!.Results[0].Error);
    }

    [Fact]
    public async Task APaymentWithoutAnOrNumberIsRejected()
    {
        // Every real payment has a receipt, and the follow-up lists treat one without as needing a receipt chased -
        // so importing a history without OR numbers would raise a false alarm on every single row.
        var (handler, added) = Build();

        var result = await handler.Handle(Command(Row(1, "1", 2024, 3, 2_400m, or: null)), CancellationToken.None);

        Assert.Empty(added);
        Assert.Equal(1, result.Value!.RejectedCount);
        Assert.Contains("OR number", result.Value!.Results[0].Error);
    }

    [Fact]
    public async Task APaymentForAnUnknownSpaceIsRejected_NotAttachedToTheWrongOne()
    {
        var (handler, added) = Build();

        var result = await handler.Handle(Command(Row(1, "99", 2024, 3, 2_400m)), CancellationToken.None);

        Assert.Empty(added);
        Assert.Equal(1, result.Value!.RejectedCount);
        Assert.Contains("No stall 99", result.Value!.Results[0].Error);
    }

    [Fact]
    public async Task AGoodRowIsStillRecordedWhenAnotherRowInTheFileIsBad()
    {
        // A history is long and hand-kept; one bad line must not cost the office the other nine hundred.
        var (handler, added) = Build();

        var result = await handler.Handle(
            Command(
                Row(1, "1", 2024, 3, 2_400m),
                Row(2, "99", 2024, 4, 2_400m),      // unknown space
                Row(3, "1", 2024, 5, 2_400m)),
            CancellationToken.None);

        Assert.Equal(2, added.Count);
        Assert.Equal(2, result.Value!.RecordedCount);
        Assert.Equal(1, result.Value!.RejectedCount);
    }

    [Fact]
    public async Task EveryRowCarriesItsOutcomeSoTheOfficeCanSeeWhatHappened()
    {
        var (handler, _) = Build();

        var result = await handler.Handle(
            Command(
                Row(1, "1", 2024, 3, 2_400m),
                Row(2, "1", 2024, 4, 900m),
                Row(3, "99", 2024, 5, 2_400m)),
            CancellationToken.None);

        Assert.Equal(3, result.Value!.TotalRows);
        Assert.Equal(3, result.Value!.Results.Count);
        Assert.Equal(new[] { 1, 2, 3 }, result.Value!.Results.Select(r => r.RowNumber));
        Assert.Equal(ImportPaymentOutcome.RecordedPaid, result.Value!.Results[0].Outcome);
        Assert.Equal(ImportPaymentOutcome.RecordedPartial, result.Value!.Results[1].Outcome);
        Assert.Equal(ImportPaymentOutcome.Rejected, result.Value!.Results[2].Outcome);
    }

    [Fact]
    public async Task AnImportedPaymentSaysItCameFromTheOfficesRecords()
    {
        // An audit must be able to tell a historical entry from a collection taken in the system today.
        var (handler, added) = Build();

        await handler.Handle(Command(Row(1, "1", 2024, 3, 2_400m)), CancellationToken.None);

        var record = Assert.Single(added);
        Assert.Contains("Imported from office records", record.Remarks);
        Assert.Equal("HistoryImport", record.UpdatedBy);
    }

    [Fact]
    public async Task APaymentGoesToTheLesseeWhoHeldTheSpaceThatMonth_NotTheOneWhoHoldsItNow()
    {
        // A stall outlives its lessees. The outgoing lessee held it to the end of March 2024 and the incoming one
        // from April, both under their own rent. A payment for March belongs to the outgoing lessee - and, just as
        // importantly, must be judged settled against THEIR rent, not the new one's.
        var stall = Stall.Create(Guid.NewGuid(), "3", 2_400m, ApplicableFees.BaseRental);
        typeof(Stall).GetProperty(nameof(Stall.Id))!.SetValue(stall, StallId);

        var outgoing = Contract.Create(stall.Id, "Wilma Cruz", "Wilma Cruz",
            new DateOnly(2023, 1, 1), durationYears: 3, monthlyRate: 900m);
        outgoing.Terminate("head", new DateOnly(2024, 3, 31));

        var incoming = Contract.Create(stall.Id, "Teofila Reyes", "Teofila Reyes",
            new DateOnly(2024, 4, 1), durationYears: 3, monthlyRate: 2_400m);

        // Added outgoing-first ON PURPOSE, and the telling month is one AFTER the handover. Wilma's TERM still spans
        // June 2024 on paper - she was terminated in March, which BillsCalendarMonth cannot see - so a rule that
        // takes the first term spanning the month picks the lessee who had already left, and judges the payment
        // against her 900 instead of Teofila's 2,400.
        stall.Contracts.Add(outgoing);
        stall.Contracts.Add(incoming);

        var (handler, added) = Build(stall);

        // March belongs to Wilma: 900 is her whole month.
        var march = await handler.Handle(Command(Row(1, "3", 2024, 3, 900m)), CancellationToken.None);
        Assert.Equal(1, march.Value!.RecordedCount);
        Assert.Equal(900m, added[0].BaseRentalAmount);
        Assert.Equal(PaymentStatus.Paid, added[0].Status);

        // June belongs to Teofila: her rent is 2,400, so 900 is a PART payment and the month stays outstanding for
        // the rest. Attributed to Wilma it would have read as fully settled and quietly written off 1,500.
        var june = await handler.Handle(Command(Row(1, "3", 2024, 6, 900m)), CancellationToken.None);
        Assert.Equal(1, june.Value!.PartialCount);
        Assert.Equal(2_400m, added[1].BaseRentalAmount);
        Assert.Equal(PaymentStatus.Partial, added[1].Status);
        Assert.Equal(1_500m, added[1].BalanceDue);
    }

    [Fact]
    public async Task AMonthNoOccupancyAnswersFor_IsRejected()
    {
        // Terminated in March with nobody after. April falls inside the term on paper, so a rule that only asks
        // whether the month is within the term would accept money for a space the lessee had already left.
        var stall = Stall.Create(Guid.NewGuid(), "4", 2_400m, ApplicableFees.BaseRental);
        typeof(Stall).GetProperty(nameof(Stall.Id))!.SetValue(stall, StallId);

        var ended = Contract.Create(stall.Id, "Wilma Cruz", "Wilma Cruz",
            new DateOnly(2023, 1, 1), durationYears: 3, monthlyRate: 900m);
        ended.Terminate("head", new DateOnly(2024, 3, 31));
        stall.Contracts.Add(ended);

        var (handler, added) = Build(stall);

        var result = await handler.Handle(Command(Row(1, "4", 2024, 6, 900m)), CancellationToken.None);

        Assert.Empty(added);
        Assert.Equal(1, result.Value!.RejectedCount);
        Assert.Contains("answers for", result.Value!.Results[0].Error);
    }

    [Fact]
    public async Task AnImportedPaymentIsDatedFromTheOfficesBooks_NotFromTheMomentOfImport()
    {
        var (handler, added) = Build();

        await handler.Handle(Command(Row(1, "1", 2024, 3, 2_400m)), CancellationToken.None);

        var record = Assert.Single(added);

        // Left at the moment of import, every row would appear in the transaction feed and the dashboard's recent
        // collections as though it had been taken today. The period reports would still be right, which is what makes
        // it easy to miss.
        Assert.NotNull(record.PaidAt);
        Assert.Equal(2024, record.PaidAt!.Value.Year);
        Assert.Equal(3, record.PaidAt!.Value.Month);
        Assert.True(record.PaidAt < DateTime.UtcNow.AddYears(-1));
    }

    [Fact]
    public async Task AnImportedPaymentNamesNoCollector()
    {
        var (handler, added) = Build();

        await handler.Handle(Command(Row(1, "1", 2024, 3, 2_400m)), CancellationToken.None);

        var record = Assert.Single(added);

        // Nobody in the system collected it. The column is nullable so that can be said; a zero GUID is not
        // "nobody" and the transaction feed renders one as though a collector were named.
        Assert.Null(record.CollectorId);
    }

    /// <summary>Stands in for a payment already on record for a given month.</summary>
    public sealed record PaymentRecordDtoStub(int Year, int Month)
    {
        public PaymentRecordDto Dto { get; } = new(
            Id: Guid.NewGuid(),
            Status: PaymentStatus.Paid,
            ORNumber: "OR-EXISTING",
            MonthlyRental: 2_400m,
            ElecAmount: null,
            WaterAmount: null,
            FishFeeAmount: null,
            TotalPaid: 2_400m,
            BalanceDue: 0m);
    }
}

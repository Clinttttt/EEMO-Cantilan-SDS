using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Records;

namespace EEMOCantilanSDS.Testing.Mobile;

/// <summary>
/// The Records feed shows ONE entry per payor at one stall for the day, whatever number of receipts they were given, and
/// the entry states what the payor handed over. These pin the arithmetic a collector reconciles cash against, and the
/// three cases that must never be merged.
/// </summary>
public class CollectorRecordGroupingTests
{
    private static readonly DateTime Morning = new(2026, 8, 24, 9, 15, 0);
    private static readonly DateTime Evening = new(2026, 8, 24, 19, 53, 0);

    private static MobileCollectorRecordDto Daily(
        string payor, string or, decimal amount, DateTime at, DateOnly feeDay,
        string stallNo = "1", bool admin = false, bool absent = false) =>
        new(or, payor, FacilityCode.NPM, "New Public Market", stallNo,
            absent ? "Absent / Excused" : "Daily Fee", amount, amount, false, at,
            MarketSection.VegetableArea, null, admin, absent, null, null, null, feeDay);

    [Fact]
    public void OnePayorTwoReceipts_IsOneEntryStatingTheWholeAmount()
    {
        // The office cleared three owed days on one receipt and today's fee on another. Two cards carried the same name
        // and the collector had to add ₱90 and ₱30 by eye.
        var records = new[]
        {
            Daily("Kim Chui", "5656565", 30m, Evening, new DateOnly(2026, 8, 22)),
            Daily("Kim Chui", "5656565", 30m, Evening, new DateOnly(2026, 8, 23)),
            Daily("Kim Chui", "5656565", 30m, Evening, new DateOnly(2026, 8, 24)),
            Daily("Kim Chui", "5656595", 30m, Morning, new DateOnly(2026, 8, 21)),
        };

        var entry = Assert.Single(CollectorRecordGrouping.Build(records));

        Assert.Equal("Kim Chui", entry.Primary.PayorName);
        Assert.Equal(120m, entry.TotalAmount);          // what the payor actually handed over
        Assert.Equal(4, entry.Count);
        Assert.Equal(2, entry.ReceiptCount);            // and both receipts survive the merge
        Assert.Equal(Evening, entry.LatestAt);
        Assert.False(entry.AnyPartial);
    }

    [Fact]
    public void EachReceiptKeepsItsOwnNumberTimeAndPayments()
    {
        // A receipt number is what the office answers for, so it is never summed away: the detail lists each receipt,
        // earliest first, with the days it covered underneath.
        var records = new[]
        {
            Daily("Kim Chui", "5656565", 30m, Evening, new DateOnly(2026, 8, 23)),
            Daily("Kim Chui", "5656565", 30m, Evening, new DateOnly(2026, 8, 24)),
            Daily("Kim Chui", "5656595", 30m, Morning, new DateOnly(2026, 8, 21)),
        };

        var receipts = Assert.Single(CollectorRecordGrouping.Build(records)).Receipts;

        Assert.Equal(2, receipts.Count);
        Assert.Equal("5656595", receipts[0].Key);       // the morning receipt first
        Assert.Equal(30m, receipts[0].Sum(i => i.Amount));
        Assert.Equal("5656565", receipts[1].Key);
        Assert.Equal(60m, receipts[1].Sum(i => i.Amount));
        Assert.Equal(
            new[] { new DateOnly(2026, 8, 23), new DateOnly(2026, 8, 24) },
            receipts[1].Select(i => i.FeeDate!.Value).OrderBy(d => d));
    }

    [Fact]
    public void AnAbsenceIsNeverMergedIntoAPayment()
    {
        // ₱0 excused days are statements, not money: merging one into a receipt would put a day nobody owes inside a
        // total the collector remits.
        var records = new[]
        {
            Daily("Pedro", "OR-1", 30m, Morning, new DateOnly(2026, 8, 24)),
            Daily("Pedro", "—", 0m, Evening, new DateOnly(2026, 8, 20), absent: true),
        };

        var entries = CollectorRecordGrouping.Build(records);

        Assert.Equal(2, entries.Count);
        Assert.Equal(30m, entries.Single(e => !e.Primary.IsAbsent).TotalAmount);
        Assert.True(entries.Single(e => e.Primary.IsAbsent).Primary.IsAbsent);
    }

    [Fact]
    public void OfficeRecordedEntriesStayApartFromTheCollectorsOwn()
    {
        // Attribution has to stay plain: the office's own entry carries the "Office" tag, and a merged card could only
        // carry one of the two.
        var records = new[]
        {
            Daily("Maria", "OR-2", 30m, Morning, new DateOnly(2026, 8, 24)),
            Daily("Maria", "OR-3", 30m, Evening, new DateOnly(2026, 8, 23), admin: true),
        };

        var entries = CollectorRecordGrouping.Build(records);

        Assert.Equal(2, entries.Count);
        Assert.Single(entries, e => e.Primary.IsAdminRecorded);
    }

    [Fact]
    public void TwoStallsOfOnePayorStayApart()
    {
        // The office reads a stall. One payor holding two of them owes two lines, not one.
        var records = new[]
        {
            Daily("Karmilita Log", "OR-4", 30m, Morning, new DateOnly(2026, 8, 24), stallNo: "7"),
            Daily("Karmilita Log", "OR-5", 30m, Evening, new DateOnly(2026, 8, 24), stallNo: "8"),
        };

        var entries = CollectorRecordGrouping.Build(records);

        Assert.Equal(2, entries.Count);
        Assert.Equal(new[] { "7", "8" }, entries.Select(e => e.Primary.StallNo).OrderBy(s => s));
    }

    [Fact]
    public void APartialPaymentIsCarriedThroughTheEntry()
    {
        // A monthly rental settled in part must still show a balance on the merged entry, or the card would read Paid.
        var full = new MobileCollectorRecordDto(
            "OR-6", "Juan Cruz", FacilityCode.TCC, "Tampak Commercial Center", "B-1",
            "Stall Rental", 2400m, 2400m, false, Morning, null, null, false, false, null, null, null, null, "Jul 2026");
        var partial = new MobileCollectorRecordDto(
            "OR-7", "Juan Cruz", FacilityCode.TCC, "Tampak Commercial Center", "B-1",
            "Stall Rental", 2400m, 1000m, true, Evening, null, null, false, false, null, null, null, null, "Aug 2026");

        var entry = Assert.Single(CollectorRecordGrouping.Build(new[] { full, partial }));

        Assert.Equal(4800m, entry.TotalAmount);
        Assert.Equal(3400m, entry.TotalPaid);
        Assert.True(entry.AnyPartial);
        Assert.Equal(new[] { "Jul 2026", "Aug 2026" }, entry.Items.Select(i => i.PeriodLabel));
    }
}

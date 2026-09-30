using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Records;

namespace EEMOCantilanSDS.UnitTest.Mobile;

public class OperationRecordGroupingTests
{
    private static CollectionRegisterRowDto Row(Guid collection, string? doc, string classification, decimal amount, decimal correction = 0m, string status = "Posted") =>
        new(collection, new DateOnly(2026, 9, 30), doc, RevenueInstrumentType.OfficialReceipt, "Payor", Guid.NewGuid(), "Ana",
            Guid.NewGuid(), classification, "Governed", amount, correction, status);

    [Fact]
    public void An_itemized_Official_Receipt_is_one_record_with_its_lines_not_several_documents()
    {
        var id = Guid.NewGuid();
        var records = OperationRecordGrouping.Group([Row(id, "OR-1", "Market Fees", 30m), Row(id, "OR-1", "Landing", 50m)]);

        var record = Assert.Single(records);
        Assert.Equal("OR-1", record.DocumentNumber);
        Assert.Equal(80m, record.Total);
        Assert.Equal(2, record.Lines.Count);
    }

    [Fact]
    public void Each_collection_appears_once_and_a_correction_is_shown_not_hidden()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var records = OperationRecordGrouping.Group([
            Row(a, "CT-1", "Market Fees", 30m), Row(b, "CT-2", "Landing", 50m, correction: -50m, status: "Reversed")]);

        Assert.Equal(2, records.Count);
        var reversed = records.Single(x => x.DocumentNumber == "CT-2");
        Assert.Equal("Reversed", reversed.Status);
        Assert.Equal(0m, reversed.Total);
        Assert.Equal("Posted", records.Single(x => x.DocumentNumber == "CT-1").Status);
    }
}

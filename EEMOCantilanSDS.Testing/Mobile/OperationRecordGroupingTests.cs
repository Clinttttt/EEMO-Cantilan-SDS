using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Records;

namespace EEMOCantilanSDS.UnitTest.Mobile;

public class OperationRecordGroupingTests
{
    private static CollectionRegisterRowDto Row(Guid collection, string src, string classification, decimal amount, decimal correction = 0m, string status = "Posted") =>
        new(collection, new DateOnly(2026, 9, 30), src, null, RevenueInstrumentType.OfficialReceipt, "Payor", Guid.NewGuid(), "Ana",
            Guid.NewGuid(), classification, "Governed", amount, correction, status);

    [Fact]
    public void An_itemized_Official_Receipt_is_one_record_with_its_lines_not_several_documents()
    {
        var id = Guid.NewGuid();
        var records = OperationRecordGrouping.Group([Row(id, "SRC-2026-000001", "Market Fees", 30m), Row(id, "SRC-2026-000001", "Landing", 50m)]);

        var record = Assert.Single(records);
        Assert.Equal("SRC-2026-000001", record.ReferenceCode);
        Assert.Equal(80m, record.Total);
        Assert.Equal(2, record.Lines.Count);
    }

    [Fact]
    public void Each_collection_appears_once_and_a_correction_is_shown_not_hidden()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var records = OperationRecordGrouping.Group([
            Row(a, "SRC-2026-000002", "Market Fees", 30m), Row(b, "SRC-2026-000003", "Landing", 50m, correction: -50m, status: "Reversed")]);

        Assert.Equal(2, records.Count);
        var reversed = records.Single(x => x.ReferenceCode == "SRC-2026-000003");
        Assert.Equal("Reversed", reversed.Status);
        Assert.Equal(0m, reversed.Total);
        Assert.Equal("Posted", records.Single(x => x.ReferenceCode == "SRC-2026-000002").Status);
    }
}

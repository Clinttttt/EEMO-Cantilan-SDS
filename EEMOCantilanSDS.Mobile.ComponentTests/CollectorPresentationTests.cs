using Bunit;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Components.Shared;
using EEMOCantilanSDS.Mobile.Records;

namespace EEMOCantilanSDS.Mobile.ComponentTests;

public sealed class CollectorPresentationTests : TestContext
{
    [Theory]
    [InlineData("VEGETABLE_FRUIT_SPACE_RENTAL", "Vegetable / Fruit Space Rental")]
    [InlineData("FISH_MEAT_VENDOR_FEE", "Fish / Meat Vendor Fee")]
    [InlineData("WEIGHT_AND_MEASURE", "Weight & Measure")]
    public void Canonical_codes_use_human_readable_names_without_changing_identifiers(string code, string expected)
        => Assert.Equal(expected, CollectorRecordLabels.Display(code));

    [Fact]
    public void Receipt_chips_derive_from_authoritative_instrument()
    {
        Assert.Equal("OR", CollectorRecordLabels.InstrumentCode(RevenueInstrumentType.OfficialReceipt));
        Assert.Equal("CT", CollectorRecordLabels.InstrumentCode(RevenueInstrumentType.CashTicket));
        Assert.Null(CollectorRecordLabels.InstrumentCode(null));
    }

    [Fact]
    public void Posted_session_summary_shows_each_actual_SRC_and_closes_without_mutating_records()
    {
        var closed = false;
        var refs = new[] { "SRC-2026-000021", "SRC-2026-000022" };
        var sheet = RenderComponent<CollectionRecordedSheet>(p => p
            .Add(x => x.Amount, 340m)
            .Add(x => x.References, refs)
            .Add(x => x.OnClose, () => closed = true));

        var dialog = sheet.Find("[role='dialog']");
        Assert.Contains("₱340.00", dialog.TextContent);
        Assert.Contains(refs[0], dialog.TextContent);
        Assert.Contains(refs[1], dialog.TextContent);
        Assert.Equal(2, dialog.QuerySelectorAll(".detail-row .detail-val").Length - 1);
        dialog.QuerySelector(".btn-close")!.Click();
        Assert.True(closed);
    }
}

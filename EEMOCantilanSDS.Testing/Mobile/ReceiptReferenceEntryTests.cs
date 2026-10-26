using EEMOCantilanSDS.Mobile.Collections;

namespace EEMOCantilanSDS.UnitTest.Mobile;

public class ReceiptReferenceEntryTests
{
    [Fact]
    public void A_reference_is_sent_only_when_the_field_is_offered_and_something_was_typed()
    {
        Assert.Equal("365363", ReceiptReferenceEntry.Number(true, "  365363 "));
        Assert.Null(ReceiptReferenceEntry.Number(false, "365363"));         // Cash Ticket / non-OR checkout: never sent
        Assert.Null(ReceiptReferenceEntry.Number(true, "   "));              // blank = the ordinary collection, unchanged
        Assert.Null(ReceiptReferenceEntry.Number(true, null));
        Assert.Null(ReceiptReferenceEntry.Number(true, new string('9', ReceiptReferenceEntry.MaxLength + 1)));
        Assert.NotNull(ReceiptReferenceEntry.Number(true, new string('9', ReceiptReferenceEntry.MaxLength)));
    }

    [Fact]
    public void A_saved_checkout_shows_its_reference_whichever_way_it_was_stored()
    {
        Assert.Equal("A1", ReceiptReferenceEntry.Restore("A1", "B2"));
        Assert.Equal("B2", ReceiptReferenceEntry.Restore(null, "B2"));
        Assert.Equal("", ReceiptReferenceEntry.Restore(null, null));
    }
}

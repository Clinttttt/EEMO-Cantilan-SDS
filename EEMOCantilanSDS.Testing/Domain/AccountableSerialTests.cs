using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.UnitTest.Domain;

/// <summary>
/// The exact printed AF No. 51 serial is the identity. The suffix is literal text (IA-059): never interpreted, never stripped,
/// never confused with the form variant, and a range is counted only when it can be counted without guessing.
/// </summary>
public sealed class AccountableSerialTests
{
    [Fact]
    public void ThePrintedSerial_IsPreservedExactly_IncludingItsSuffixAndSpacing()
    {
        Assert.True(AccountableSerial.TryParse("2315601 A", out var serial));
        Assert.Equal("2315601 A", serial.Raw);
        Assert.Equal((string.Empty, 2315601L, 7, " A"), (serial.Prefix, serial.Number, serial.Digits, serial.Suffix));
        Assert.Equal("2315601 A", serial.Format(2315601));
        Assert.Equal("2315650 A", serial.Format(2315650));
    }

    [Theory]
    [InlineData("2315601 A", "2315601A")]
    [InlineData("2315601 A", "2315601 a")]
    [InlineData(" 2315601  A ", "2315601A")]
    public void HarmlessPresentationDifferences_NormalizeToTheSameLookupKey(string first, string second) =>
        Assert.Equal(AccountableSerial.Normalize(first), AccountableSerial.Normalize(second));

    [Fact]
    public void TheSuffixIsNotStripped_AndADifferentSuffixIsADifferentSerial()
    {
        Assert.NotEqual(AccountableSerial.Normalize("2315601 A"), AccountableSerial.Normalize("2315601"));
        Assert.NotEqual(AccountableSerial.Normalize("2315601 A"), AccountableSerial.Normalize("2315601 C"));
    }

    [Fact]
    public void ADeterministicRange_IsCountedByItsFirstAndLastSerial_WithTheSuffixRepeatedLiterally()
    {
        Assert.True(AccountableSerial.TryParseRange("2315601 A", "2315650 A", out var start, out var last, out var error), error);
        Assert.Equal(2315650, last);
        Assert.Equal(50, last - start.Number + 1);
        // Another suffix proves nothing is hard-coded to A or to a booklet of 50.
        Assert.True(AccountableSerial.TryParseRange("9448651 L", "9448700 L", out _, out var last2, out _));
        Assert.True(AccountableSerial.TryParseRange("0000101", "0000107", out var zero, out var last3, out _));
        Assert.Equal("0000103", zero.Format(103));
        Assert.Equal(7, last3 - zero.Number + 1);
    }

    [Theory]
    [InlineData("2315601 A", "2315650 B")]   // a different suffix is a different series, not counted across
    [InlineData("2315601 A", "2315650")]
    [InlineData("OR-2315601", "AF-2315650")]
    [InlineData("2315650 A", "2315601 A")]   // backwards
    [InlineData("2315601 A", "231565 A")]    // does not follow the first one's numbering
    [InlineData("23-15601", "23-15650")]     // more than one run of digits is not enumerable
    [InlineData("", "2315650 A")]
    public void ARangeThatCannotBeCounted_IsRefusedNotGuessed(string first, string last)
    {
        Assert.False(AccountableSerial.TryParseRange(first, last, out _, out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void TheSuffixIsStoredAsLiteralText_NeverAsTheFormVariant()
    {
        var book = AccountableFormBook.Receive(Guid.NewGuid(), RevenueInstrumentType.OfficialReceipt, "AF No. 51", string.Empty, " A", string.Empty,
            2315601, 2315605, 7, DateTime.UtcNow, null, "Municipal Treasurer", null, "actor", "actor");
        Assert.Equal(string.Empty, book.FormVariant);
        Assert.Equal(" A", book.NumberSuffix);
        Assert.Equal("2315601 A", book.FormatNumber(2315601));
        Assert.Equal(5, book.Quantity);

        var variant = AccountableFormBook.Receive(Guid.NewGuid(), RevenueInstrumentType.OfficialReceipt, "AF No. 51-A", string.Empty, string.Empty, "51-A",
            7000001, 7000003, 7, DateTime.UtcNow, null, null, null, "actor", "actor");
        Assert.Equal("51-A", variant.FormVariant);
        Assert.Equal(string.Empty, variant.NumberSuffix);
    }

    [Fact]
    public void ARegisteredUnit_KeepsItsPrintedNumber_AndGetsANormalizedKey()
    {
        var book = AccountableFormBook.Receive(Guid.NewGuid(), RevenueInstrumentType.OfficialReceipt, "AF No. 51", string.Empty, " A", string.Empty,
            2315601, 2315605, 7, DateTime.UtcNow, null, null, null, "actor", "actor");
        var unit = AccountableDocument.Register(book, 2315602, "actor");
        Assert.Equal("2315602 A", unit.DocumentNumber);
        Assert.Equal("2315602A", unit.NormalizedNumber);
    }

    [Fact]
    public void ALostUnit_IsBlockedForGood_AndOnlyABlankUnitCanBeBlocked()
    {
        var book = AccountableFormBook.Receive(Guid.NewGuid(), RevenueInstrumentType.OfficialReceipt, "AF No. 51", string.Empty, " A", string.Empty,
            1, 3, 7, DateTime.UtcNow, null, null, null, "actor", "actor");
        var blank = AccountableDocument.Register(book, 1, "actor");
        blank.MarkLost("actor");
        Assert.Equal(AccountableDocumentState.Lost, blank.State);
        Assert.Throws<InvalidOperationException>(() => blank.AssignTo(Guid.NewGuid(), "actor"));
        Assert.Throws<InvalidOperationException>(() => blank.ReturnToOffice("actor"));
        Assert.Throws<InvalidOperationException>(() => blank.Consume(null, Guid.NewGuid(), DateTime.UtcNow, "actor"));
        Assert.Throws<InvalidOperationException>(() => blank.Void("actor"));

        var issued = AccountableDocument.Register(book, 2, "actor");
        issued.Consume(null, Guid.NewGuid(), DateTime.UtcNow, "actor");
        Assert.Throws<InvalidOperationException>(() => issued.MarkLost("actor"));
        Assert.Equal(AccountableDocumentState.Consumed, issued.State);
    }

    [Fact]
    public void ALossReport_NeedsACopyScope_ANarrative_AndASaneDate()
    {
        var book = AccountableFormBook.Receive(Guid.NewGuid(), RevenueInstrumentType.OfficialReceipt, "AF No. 51", string.Empty, " A", string.Empty,
            1, 3, 7, DateTime.UtcNow, null, null, null, "actor", "actor");
        var unit = AccountableDocument.Register(book, 1, "actor");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        AccountableFormLossReport Report(AccountableFormCopies copies, string narrative, DateOnly lostOn) =>
            AccountableFormLossReport.Record(unit, null, copies, lostOn, "Market", narrative, today, true, "a", "A", DateTime.UtcNow);

        Assert.Equal(AccountableFormCopies.Duplicate, Report(AccountableFormCopies.Duplicate, "Duplicate not in the booklet", today).CopiesLost);
        Assert.Throws<ArgumentException>(() => Report(0, "x", today));
        Assert.Throws<ArgumentException>(() => Report(AccountableFormCopies.WholeSet, " ", today));
        Assert.Throws<ArgumentException>(() => Report(AccountableFormCopies.WholeSet, "x", today.AddDays(1)));
    }
}

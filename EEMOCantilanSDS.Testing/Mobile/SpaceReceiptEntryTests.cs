using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Mobile.Collections;

namespace EEMOCantilanSDS.Testing.Mobile;

/// <summary>One physical OR is offered, and sent, only when every item is a month of the same space account.</summary>
public sealed class SpaceReceiptEntryTests
{
    private static readonly Guid Account = Guid.NewGuid();
    private static CollectionSessionItemIntent Month(Guid account, int month) =>
        new(Guid.NewGuid(), CollectionSessionItemKind.Obligation, 100m, Obligation: new(account, 2026, month));

    [Fact]
    public void SeveralMonthsOfOneAccount_OfferTheReceipt_AndSendItTrimmed()
    {
        var items = new[] { Month(Account, 4), Month(Account, 5), Month(Account, 6) };
        Assert.True(SpaceReceiptEntry.Applies(items));
        Assert.Equal("OR-0001", SpaceReceiptEntry.Number(items, "  OR-0001 "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankReceipt_IsTheSrcOnlyPath(string? typed) =>
        Assert.Null(SpaceReceiptEntry.Number([Month(Account, 4), Month(Account, 5)], typed));

    [Fact]
    public void MonthsOfDifferentAccounts_OrOtherSources_NeverCarryAReceipt()
    {
        var mixed = new[] { Month(Account, 4), Month(Guid.NewGuid(), 4) };
        Assert.False(SpaceReceiptEntry.Applies(mixed));
        Assert.Null(SpaceReceiptEntry.Number(mixed, "OR-0001"));
        var other = new[] { Month(Account, 4), new CollectionSessionItemIntent(Guid.NewGuid(), CollectionSessionItemKind.Water, 50m, Water: new(Guid.NewGuid(), 2026, 4)) };
        Assert.Null(SpaceReceiptEntry.Number(other, "OR-0001"));
        Assert.False(SpaceReceiptEntry.Applies([]));
    }
}

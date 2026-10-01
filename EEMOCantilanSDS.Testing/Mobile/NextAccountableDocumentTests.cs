using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Presentation;

namespace EEMOCantilanSDS.UnitTest.Mobile;

public class NextAccountableDocumentTests
{
    private static CashTicketDocumentDto Ct(long serial) =>
        new(Guid.NewGuid(), $"CT{serial:000000}", AccountableDocumentState.Assigned, Guid.NewGuid(), serial);

    [Fact]
    public void TheNextTicket_IsTheLowestAssignedSerial_WhateverOrderTheServerSent()
    {
        var usable = NextAccountableDocument.Usable([Ct(105), Ct(101), Ct(103)], new HashSet<Guid>());

        Assert.Equal("CT000101", NextAccountableDocument.Resolve(usable, null)!.DocumentNumber);
        Assert.Equal("CT000101 – CT000105", NextAccountableDocument.RangeLabel(usable));
    }

    [Fact]
    public void ATicketAlreadyIssuedOnThisDevice_IsNeverOfferedAgain()
    {
        var first = Ct(101);
        var usable = NextAccountableDocument.Usable([first, Ct(102)], new HashSet<Guid> { first.DocumentId });

        Assert.Equal("CT000102", NextAccountableDocument.Resolve(usable, first.DocumentId)!.DocumentNumber);
        Assert.Single(usable);
    }

    [Fact]
    public void AnExplicitChoice_IsKeptWhileItIsStillUsable()
    {
        var chosen = Ct(104);
        var usable = NextAccountableDocument.Usable([Ct(101), chosen], new HashSet<Guid>());

        Assert.Equal(chosen.DocumentId, NextAccountableDocument.Resolve(usable, chosen.DocumentId)!.DocumentId);
    }

    [Fact]
    public void WithNoAssignedTicket_NothingIsResolved()
    {
        var usable = NextAccountableDocument.Usable([], new HashSet<Guid>());

        Assert.Null(NextAccountableDocument.Resolve(usable, null));
        Assert.Equal(string.Empty, NextAccountableDocument.RangeLabel(usable));
    }
}

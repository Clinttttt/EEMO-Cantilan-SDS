using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Models;
using EEMOCantilanSDS.Mobile.Records;

namespace EEMOCantilanSDS.Mobile.ComponentTests;

public sealed class OfflineCollectionHistoryTests
{
    private static PendingOperation SyncedSession() => new()
    {
        Kind = OfflineOperationKind.ItemizedCollectionSession,
        LocalStatus = PendingLocalStatus.Synced,
        IssuedDocumentState = IssuedDocumentLocalState.SyncedAcknowledged,
        Title = "Itemized collection",
        Amount = 0m,
        CollectionSessionResult = new(
            Guid.NewGuid(), CollectionSessionStatus.Recorded, Guid.NewGuid(), 150m,
            [new(Guid.NewGuid(), "SRC-2026-000042", RevenueInstrumentType.OfficialReceipt, 150m, [Guid.NewGuid()], "Posted")], [])
    };

    [Fact]
    public void FullyAcknowledgedItemizedSessionsAreHiddenFromOfflineSheetWithoutDeletingTheQueue()
    {
        var synced = SyncedSession();
        var pending = new PendingOperation { Kind = OfflineOperationKind.ItemizedCollectionSession, LocalStatus = PendingLocalStatus.Pending };
        var failed = new PendingOperation { Kind = OfflineOperationKind.ItemizedCollectionSession, LocalStatus = PendingLocalStatus.Failed };
        var rejected = new PendingOperation { Kind = OfflineOperationKind.ItemizedCollectionSession, LocalStatus = PendingLocalStatus.ReconciliationRequired };
        PendingOperation[] stored = [synced, pending, failed, rejected];

        var visible = OfflineCollectionHistory.Display(stored);

        Assert.Equal(4, stored.Length);
        Assert.DoesNotContain(synced, visible);
        Assert.Equal(3, visible.Count);
        Assert.Contains(pending, visible);
        Assert.Contains(failed, visible);
        Assert.Contains(rejected, visible);
    }

    [Fact]
    public void IncompleteAcknowledgmentsAndPhysicalDocumentEvidenceStayVisible()
    {
        var noResult = SyncedSession();
        noResult.CollectionSessionResult = null;
        var noReference = SyncedSession();
        noReference.CollectionSessionResult = noReference.CollectionSessionResult! with
        {
            Collections = [new(Guid.NewGuid(), "", RevenueInstrumentType.OfficialReceipt, 150m, [], "Posted")]
        };
        var issued = SyncedSession();
        issued.AccountableDocumentId = Guid.NewGuid();

        Assert.Equal(3, OfflineCollectionHistory.Display([noResult, noReference, issued]).Count);
        Assert.Equal(1, OfflineCollectionHistory.IssuedDocumentCount([noResult, noReference, issued]));
        Assert.Null(OfflineCollectionHistory.DisplayAmount(noResult));
        Assert.Equal(150m, OfflineCollectionHistory.DisplayAmount(issued));
        Assert.Equal("SRC-2026-000042", OfflineCollectionHistory.ServerReferences(issued));
    }

    [Fact]
    public void MultipleCollectionsAreNotMisrepresentedAsOneReceipt()
    {
        var session = SyncedSession();
        session.AccountableDocumentId = Guid.NewGuid();
        session.CollectionSessionResult = session.CollectionSessionResult! with
        {
            GrandTotal = 300m,
            Collections =
            [
                new(Guid.NewGuid(), "SRC-2026-000042", RevenueInstrumentType.OfficialReceipt, 150m, [], "Posted"),
                new(Guid.NewGuid(), "SRC-2026-000043", RevenueInstrumentType.CashTicket, 150m, [], "Posted")
            ]
        };
        Assert.Equal("SRC-2026-000042, SRC-2026-000043", OfflineCollectionHistory.ServerReferences(session));
        Assert.Equal(300m, OfflineCollectionHistory.DisplayAmount(session));
        Assert.Equal("Collection checkout", OfflineCollectionHistory.Title(session));
    }
}

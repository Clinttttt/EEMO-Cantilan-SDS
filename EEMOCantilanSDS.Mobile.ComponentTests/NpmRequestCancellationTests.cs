using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Mobile.ComponentTests;

public sealed class NpmRequestCancellationTests
{
    [Fact]
    public void Cancellation_is_terminal_versioned_and_does_not_issue_any_collection()
    {
        var row = NpmRegistrationRequest.Submit(Guid.NewGuid(), Guid.NewGuid(), "Collector",
            Guid.NewGuid(), 2026, "Josephine Reyes", Guid.NewGuid(), "intent-fingerprint",
            "2", MarketSection.FishSection, "Office reference", DateTime.UtcNow);
        var operation = Guid.NewGuid();
        row.Cancel(operation, row.Version, "Collector", DateTime.UtcNow);
        Assert.Equal(NpmRegistrationRequestStatus.Cancelled, row.Status);
        Assert.Equal(operation, row.DecisionOperationId);
        Assert.Equal(2, row.Version);
        Assert.Null(row.AssociationId);
        Assert.Equal("Cancelled by submitting collector", row.ReviewReason);
        Assert.Throws<InvalidOperationException>(() => row.Cancel(Guid.NewGuid(), row.Version, "Collector", DateTime.UtcNow));
    }
}

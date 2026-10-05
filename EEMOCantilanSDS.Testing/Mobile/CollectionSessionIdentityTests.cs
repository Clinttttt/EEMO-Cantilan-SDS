using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Constants;

namespace EEMOCantilanSDS.UnitTest.Mobile;

public sealed class CollectionSessionIdentityTests
{
    private static CollectionSessionIntent Intent() => new(Guid.NewGuid(), new(2026, 10, 5), Guid.NewGuid(),
    [new(Guid.NewGuid(), CollectionSessionItemKind.GovernedService, 30m, Service: new(CollectorOperationCodes.MarketFees)),
     new(Guid.NewGuid(), CollectionSessionItemKind.GovernedService, 200m, Service: new(CollectorOperationCodes.LandingBerthing))]);
    [Fact]
    public void Reordering_and_decimal_scale_do_not_change_intent_but_item_source_payer_and_amount_do()
    {
        var a = Intent(); var hash = CollectionSessionWorkflow.IntentFingerprint(a);
        Assert.Equal(hash, CollectionSessionWorkflow.IntentFingerprint(a with { Items = a.Items.Reverse().ToArray() }));
        Assert.Equal(hash, CollectionSessionWorkflow.IntentFingerprint(a with { Items = [a.Items[0] with { ConfirmedAmount = 30.00m }, a.Items[1]] }));
        Assert.NotEqual(hash, CollectionSessionWorkflow.IntentFingerprint(a with { PayorId = Guid.NewGuid() }));
        foreach (var changed in new[] { a.Items[0] with { ConfirmedAmount = 31m }, a.Items[0] with { ClientItemId = Guid.NewGuid() },
                     a.Items[0] with { Service = new(CollectorOperationCodes.LandingBerthing) } })
            Assert.NotEqual(hash, CollectionSessionWorkflow.IntentFingerprint(a with { Items = [changed, a.Items[1]] }));
    }
    [Fact]
    public void Invalid_precision_must_not_alias_a_previously_accepted_amount()
    {
        var a = Intent();
        Assert.NotEqual(CollectionSessionWorkflow.IntentFingerprint(a), CollectionSessionWorkflow.IntentFingerprint(
            a with { Items = [a.Items[0] with { ConfirmedAmount = 30.001m }, a.Items[1]] }));
    }
    [Fact]
    public void Child_identity_is_stable_and_namespaced_by_tenant_session_and_item()
    {
        var t = Guid.NewGuid(); var s = Guid.NewGuid(); var i = Guid.NewGuid();
        var id = CollectionSessionWorkflow.ChildOperationId(t, s, i);
        Assert.Equal(id, CollectionSessionWorkflow.ChildOperationId(t, s, i));
        Assert.NotEqual(id, CollectionSessionWorkflow.ChildOperationId(Guid.NewGuid(), s, i));
        Assert.NotEqual(id, CollectionSessionWorkflow.ChildOperationId(t, Guid.NewGuid(), i));
        Assert.NotEqual(id, CollectionSessionWorkflow.ChildOperationId(t, s, Guid.NewGuid()));
    }
}

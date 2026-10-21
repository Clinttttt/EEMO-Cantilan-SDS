using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using Xunit;

namespace EEMOCantilanSDS.Testing;

public sealed class FishMeatRegistrationRequestTests
{
    private static FishMeatRegistrationRequest Submitted() => FishMeatRegistrationRequest.Submit(Guid.NewGuid(), Guid.NewGuid(),
        "Collector", Guid.NewGuid(), new string('a', 64), new string('b', 64), "Proposed identity", FishMeatVendorType.Fish,
        2026, null, null, DateTime.UtcNow, null);

    [Fact]
    public void Default_policy_is_off_and_tenant_dates_are_independent()
    {
        var tenant = Guid.NewGuid(); var date = new DateOnly(2026, 10, 19);
        Assert.False(new FishMeatRegistrationOptions().RequestsInstalled);
        Assert.False(new FishMeatRegistrationOptions().RequiresRegistration(tenant, date));
        var policy = new FishMeatRegistrationOptions { RegistrationRequiredFrom = new Dictionary<Guid, DateOnly> { [tenant] = date } };
        Assert.True(policy.RequiresRegistration(tenant, date));
        Assert.False(policy.RequiresRegistration(tenant, date.AddDays(-1)));
        Assert.False(policy.RequiresRegistration(Guid.NewGuid(), date));
    }

    [Fact]
    public void Activated_request_queue_is_limited_to_the_explicitly_authorized_working_copy_tenant()
    {
        var approved = Guid.NewGuid();
        var enabled = new FishMeatRegistrationOptions
        {
            RequestsInstalled = true,
            RequestTenants = new HashSet<Guid> { approved }
        };
        Assert.True(enabled.RequestsAvailable(approved));
        Assert.False(enabled.RequestsAvailable(Guid.NewGuid()));
        Assert.False(enabled.RequestsAvailable(Guid.Empty));
        Assert.False(new FishMeatRegistrationOptions().RequestsAvailable(approved));
        Assert.False(enabled.RequiresRegistration(approved, new DateOnly(2026, 10, 20)));
    }

    [Fact]
    public void Approval_requires_registration_and_decisions_are_terminal_and_versioned()
    {
        var row = Submitted(); var actor = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => row.Decide(Guid.NewGuid(), "hash", 1, FishMeatRequestStatus.Approved, "Head", "Verified", DateTime.UtcNow, null, actor));
        Assert.Equal(FishMeatRequestStatus.Submitted, row.Status);
        row.Decide(Guid.NewGuid(), "hash", 1, FishMeatRequestStatus.Rejected, "Head", "Evidence not verified", DateTime.UtcNow, null, actor);
        Assert.Equal(2, row.Version); Assert.Equal(actor, row.ReviewedByUserId); Assert.Null(row.RegistrationId);
        Assert.Throws<InvalidOperationException>(() => row.Decide(Guid.NewGuid(), "hash", 2, FishMeatRequestStatus.Approved, "Head", "Retry", DateTime.UtcNow, Guid.NewGuid(), actor));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Invalid_vendor_types_are_rejected(int value) => Assert.Throws<ArgumentException>(() =>
        FishMeatRegistrationRequest.Submit(Guid.NewGuid(), Guid.NewGuid(), "Collector", Guid.NewGuid(), "hash", "key",
            "Proposed", (FishMeatVendorType)value, 2026, null, null, DateTime.UtcNow, null));
}

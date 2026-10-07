using EEMOCantilanSDS.Domain.Entities.Revenue;

namespace EEMOCantilanSDS.Testing;

public class FishMeatRegistryLifecycleTests
{
    private static FishMeatVendorRegistration Vendor() => FishMeatVendorRegistration.Register(Guid.NewGuid(), Guid.NewGuid(), 2026,
        FishMeatVendorType.Fish, VendorRegistrationKind.New, " Vendor ", "Business", "Address", "Reference", "Head");
    [Fact]
    public void Close_keeps_identity_and_registration_facts_and_records_audit()
    {
        var v = Vendor(); var id = v.Id; var operation = v.ClientOperationId; var close = Guid.NewGuid();
        v.Close(close, new(2026, 10, 7), " Head ", " Reviewed ");
        Assert.Equal(id, v.Id); Assert.Equal(operation, v.ClientOperationId); Assert.Equal(2026, v.TaxYear);
        Assert.Equal(VendorRegistrationKind.New, v.RegistrationKind); Assert.Equal("Vendor", v.DisplayName);
        Assert.Equal(VendorRegistrationStatus.Closed, v.Status); Assert.Equal(close, v.CloseClientOperationId);
        Assert.Equal("Head", v.ClosedBy); Assert.Equal("Reviewed", v.CloseNote); Assert.NotNull(v.ClosedAtUtc);
        Assert.Throws<InvalidOperationException>(() => v.Close(Guid.NewGuid(), new(2026, 10, 8), "Head", null));
    }
    [Fact]
    public void Renewal_is_a_new_record_with_explicit_prior_identity_and_same_type()
    {
        var v = Vendor(); v.Close(Guid.NewGuid(), new(2026, 10, 7), "Head", null);
        var next = FishMeatVendorRegistration.Renew(v, Guid.NewGuid(), 2027, null, null, null, null, "Head");
        Assert.NotEqual(v.Id, next.Id); Assert.Equal(v.Id, next.PriorRegistrationId); Assert.Equal(v.VendorType, next.VendorType);
        Assert.Equal(2026, v.TaxYear); Assert.Equal(2027, next.TaxYear); Assert.Equal(VendorRegistrationKind.Renew, next.RegistrationKind);
        Assert.Equal(VendorRegistrationStatus.Active, next.Status); Assert.Equal(v.BusinessName, next.BusinessName); Assert.Null(next.ClosedOn);
    }
    [Theory]
    [InlineData(2025)]
    [InlineData(2026)]
    [InlineData(2201)]
    public void Renewal_rejects_invalid_year(int year) => Assert.Throws<ArgumentException>(() =>
        FishMeatVendorRegistration.Renew(Vendor(), Guid.NewGuid(), year, null, null, null, null, "Head"));
}

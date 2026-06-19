using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Models;

namespace EEMOCantilanSDS.UnitTest.Mobile;

public class PendingOperationMappingTests
{
    [Fact]
    public void ToDto_maps_every_wire_field()
    {
        var id = Guid.NewGuid();
        var stallId = Guid.NewGuid();
        var transporterId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 6, 5, 1, 2, 3, DateTimeKind.Utc);
        var bizDate = new DateOnly(2026, 6, 5);

        var op = new PendingOperation
        {
            ClientOperationId = id,
            Kind = OfflineOperationKind.Slaughter,
            BusinessDate = bizDate,
            ORNumber = "OR-1",
            StallId = stallId,
            IsPaid = true,
            FishKilos = 12.5m,
            Status = PaymentStatus.Partial,
            PartialAmount = 100m,
            OwnerName = "Owner",
            AnimalType = AnimalType.Hog,
            CustomAnimalType = "Goat",
            NumberOfHeads = 3,
            CustomRate = 200m,
            TransporterId = transporterId,
            DriverName = "Driver",
            PlateNumber = "ABC 123",
            Route = "Route A",
            Organization = "Coop",
            OccurredAt = occurredAt,
            VendorName = "Vendor",
            Goods = "Fish",
            Remarks = "note",
            // local-only fields must NOT leak into the DTO shape
            LocalStatus = PendingLocalStatus.Failed,
            ResultMessage = "ignored",
            FacilityLabel = "SLH",
            Title = "ignored",
            Amount = 999m
        };

        SyncOfflineOperationDto dto = op.ToDto();

        Assert.Equal(id, dto.ClientOperationId);
        Assert.Equal(OfflineOperationKind.Slaughter, dto.Kind);
        Assert.Equal(bizDate, dto.BusinessDate);
        Assert.Equal("OR-1", dto.ORNumber);
        Assert.Equal(stallId, dto.StallId);
        Assert.True(dto.IsPaid);
        Assert.Equal(12.5m, dto.FishKilos);
        Assert.Equal(PaymentStatus.Partial, dto.Status);
        Assert.Equal(100m, dto.PartialAmount);
        Assert.Equal("Owner", dto.OwnerName);
        Assert.Equal(AnimalType.Hog, dto.AnimalType);
        Assert.Equal("Goat", dto.CustomAnimalType);
        Assert.Equal(3, dto.NumberOfHeads);
        Assert.Equal(200m, dto.CustomRate);
        Assert.Equal(transporterId, dto.TransporterId);
        Assert.Equal("Driver", dto.DriverName);
        Assert.Equal("ABC 123", dto.PlateNumber);
        Assert.Equal("Route A", dto.Route);
        Assert.Equal("Coop", dto.Organization);
        Assert.Equal(occurredAt, dto.OccurredAt);
        Assert.Equal("Vendor", dto.VendorName);
        Assert.Equal("Fish", dto.Goods);
        Assert.Equal("note", dto.Remarks);
    }

    [Fact]
    public void New_operation_defaults_to_pending_with_generated_id()
    {
        var op = new PendingOperation();

        Assert.NotEqual(Guid.Empty, op.ClientOperationId);
        Assert.Equal(PendingLocalStatus.Pending, op.LocalStatus);
    }
}

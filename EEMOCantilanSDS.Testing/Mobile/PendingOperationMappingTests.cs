using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Models;

namespace EEMOCantilanSDS.UnitTest.Mobile;

public class PendingOperationMappingTests
{
    [Fact]
    public void Outstanding_rent_retry_keeps_owner_amount_date_and_quote_after_serialization()
    {
        var operation = new PendingOperation { Kind = OfflineOperationKind.NpmOutstandingRent, StallId = Guid.NewGuid(),
            NpmContractId = Guid.NewGuid(), ClientOperationId = Guid.NewGuid(), BusinessDate = new(2026, 10, 20),
            ReceivedAmount = 1000, NpmQuoteToken = "frozen-cross-period-token", PayloadVersion = 1 };
        var copy = System.Text.Json.JsonSerializer.Deserialize<PendingOperation>(System.Text.Json.JsonSerializer.Serialize(operation))!.ToDto();
        Assert.Equal(operation.ClientOperationId, copy.ClientOperationId); Assert.Equal(operation.StallId, copy.StallId);
        Assert.Equal(operation.NpmContractId, copy.NpmContractId); Assert.Equal(operation.ReceivedAmount, copy.ReceivedAmount);
        Assert.Equal(operation.BusinessDate, copy.BusinessDate); Assert.Equal(operation.NpmQuoteToken, copy.NpmQuoteToken);
        Assert.Equal(OfflineOperationKind.NpmOutstandingRent, copy.Kind);
    }
    [Fact]
    public void Monthly_daily_queue_keeps_cash_date_separate_from_installment_and_frozen_amount()
    {
        var operation = new PendingOperation { ClientOperationId = Guid.NewGuid(), Kind = OfflineOperationKind.NpmDaily,
            BusinessDate = new(2026, 10, 17), NpmInstallmentDate = new(2026, 9, 26),
            ReceivedAmount = 25, NpmQuoteToken = "reviewed-source-token", StallId = Guid.NewGuid() };
        var copy = System.Text.Json.JsonSerializer.Deserialize<PendingOperation>(System.Text.Json.JsonSerializer.Serialize(operation))!;
        var dto = copy.ToDto();
        Assert.Equal(operation.ClientOperationId, dto.ClientOperationId);
        Assert.Equal(operation.BusinessDate, dto.BusinessDate); Assert.Equal(operation.NpmInstallmentDate, dto.NpmInstallmentDate);
        Assert.Equal(25, dto.ReceivedAmount); Assert.Equal("reviewed-source-token", dto.NpmQuoteToken);
    }
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
            MeatKilos = 3.25m,
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
        Assert.Equal(3.25m, dto.MeatKilos);
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

    [Fact]
    public void Canonical_rent_retry_preserves_source_period_amount_version_and_operation()
    {
        var operation = new PendingOperation { Kind = OfflineOperationKind.RentCollection, StallId = Guid.NewGuid(),
            BusinessDate = new(2026, 10, 18), BillingYear = 2026, BillingMonth = 9, ReceivedAmount = 25, RentSourceVersion = 7 };
        var first = operation.ToDto(); var retry = operation.ToDto();
        Assert.Equal(first, retry); Assert.Equal(operation.ClientOperationId, first.ClientOperationId);
        Assert.Equal(25, first.ReceivedAmount); Assert.Equal(7, first.RentSourceVersion);
        Assert.Equal((2026, 9), (first.BillingYear, first.BillingMonth)); Assert.Null(first.ORNumber);
    }
}

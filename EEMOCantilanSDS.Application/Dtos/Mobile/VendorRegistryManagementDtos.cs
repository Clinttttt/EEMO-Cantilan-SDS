using EEMOCantilanSDS.Domain.Entities.Revenue;

namespace EEMOCantilanSDS.Application.Dtos.Mobile;

public sealed record CloseVendorRegistrationRequest(Guid ClientOperationId, string? Note = null);
public sealed record RenewVendorRegistrationRequest(Guid ClientOperationId, int TaxYear, string? DisplayName = null,
    string? BusinessName = null, string? Address = null, string? Reference = null);
public sealed record VendorRegistrationManagementRow(FishMeatVendorRegistrationDto Registration,
    VendorRegistrationStatus Status, string CreatedBy, DateTime RecordedAtUtc, DateOnly? ClosedOn,
    DateTime? ClosedAtUtc, string? ClosedBy, string? CloseNote, Guid? PriorRegistrationId,
    decimal VendorFeeCollected, decimal WeightMeasureCollected)
{
    public decimal TotalCollected => VendorFeeCollected + WeightMeasureCollected;
}
public sealed record VendorRegistryManagement(int TaxYear, int Year, int Month,
    IReadOnlyList<VendorRegistrationManagementRow> Rows, decimal WalkUpVendorFeeCollected);
public sealed record VendorRegistrationMutationResult(FishMeatVendorRegistrationDto Registration,
    VendorRegistrationStatus Status, Guid? PriorRegistrationId, DateOnly? ClosedOn,
    DateTime? ClosedAtUtc, string? ClosedBy, string? CloseNote, bool ExistingOutcome);

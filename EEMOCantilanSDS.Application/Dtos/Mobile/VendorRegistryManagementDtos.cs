using EEMOCantilanSDS.Domain.Entities.Revenue;

namespace EEMOCantilanSDS.Application.Dtos.Mobile;

/// <summary>Head-only correction of office facts. Before is the original full registration DTO
/// and protects the editing session against overwriting an intervening registration change.</summary>
public sealed record CorrectVendorOfficeDetailsRequest(FishMeatVendorRegistrationDto Before,
    string? BusinessName, string? Address, string? LastName, string? FirstName,
    string? MiddleName, string? NameExtension, string? Sex, string? NatureOfBusiness);
public sealed record CloseVendorRegistrationRequest(Guid ClientOperationId, string? Note = null);
public sealed record DeleteVendorRegistrationRequest(Guid ClientOperationId, string Reason);
public sealed record RenewVendorRegistrationRequest(Guid ClientOperationId, int TaxYear, string? DisplayName = null,
    string? BusinessName = null, string? Address = null, string? Reference = null);
public sealed record VendorRegistrationManagementRow(FishMeatVendorRegistrationDto Registration,
    VendorRegistrationStatus Status, string CreatedBy, DateTime RecordedAtUtc, DateOnly? ClosedOn,
    DateTime? ClosedAtUtc, string? ClosedBy, string? CloseNote, Guid? PriorRegistrationId,
    decimal VendorFeeCollected, decimal WeightMeasureCollected,
    DateTime? DeletedAtUtc = null, string? DeletedBy = null, string? DeleteReason = null)
{
    public decimal TotalCollected => VendorFeeCollected + WeightMeasureCollected;
}
public sealed record VendorRegistryManagement(int TaxYear, int Year, int Month,
    IReadOnlyList<VendorRegistrationManagementRow> Rows, decimal WalkUpVendorFeeCollected);
public sealed record VendorRegistrationMutationResult(FishMeatVendorRegistrationDto Registration,
    VendorRegistrationStatus Status, Guid? PriorRegistrationId, DateOnly? ClosedOn,
    DateTime? ClosedAtUtc, string? ClosedBy, string? CloseNote, bool ExistingOutcome,
    DateTime? DeletedAtUtc = null, string? DeletedBy = null, string? DeleteReason = null);

using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Mobile;

public sealed record SubmitFishMeatRegistrationRequest(Guid ClientOperationId, string VendorName,
    FishMeatVendorType VendorType, int TaxYear, string? Contact = null, string? Note = null, Guid? ReplacesRequestId = null);
public sealed record ReviewFishMeatRegistrationRequest(Guid ClientOperationId, int ExpectedVersion,
    FishMeatRequestStatus Outcome, string Reason, Guid? RegistrationId = null);
public sealed record CancelFishMeatRegistrationRequest(Guid ClientOperationId, int ExpectedVersion);
public sealed record CompleteFishMeatRegistrationRequest(Guid ClientOperationId, int ExpectedVersion,
    string Reason, RegisterFishMeatVendorRequest Registration);
public sealed record FishMeatRegistrationRequestDto(Guid Id, Guid CollectorId, string SubmittedBy, string VendorName,
    FishMeatVendorType VendorType, int TaxYear, string? Contact, string? Note, DateTime SubmittedAtUtc,
    FishMeatRequestStatus Status, int Version, Guid? RegistrationId, string? ReviewedBy,
    DateTime? ReviewedAtUtc, string? Reason, Guid? ReplacesRequestId, Guid? ReviewedByUserId);
public sealed record FishMeatRegistrationRequestPage(IReadOnlyList<FishMeatRegistrationRequestDto> Rows, int Total, int Page, int PageSize);

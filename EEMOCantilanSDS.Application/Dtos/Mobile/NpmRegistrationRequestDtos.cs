using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Mobile;
public sealed record SubmitNpmRegistrationRequest(Guid ClientOperationId, Guid VendorRegistrationId,
    string ProposedStall, MarketSection? ProposedSection, string Note, Guid? ReplacesRequestId = null);
public sealed record ReviewNpmRegistrationRequest(Guid ClientOperationId, int ExpectedVersion,
    NpmRegistrationRequestStatus Outcome, string Reason, Guid? AssociationId = null);
public sealed record NpmRegistrationRequestDto(Guid Id, Guid VendorRegistrationId, int TaxYear, string VendorName,
    Guid CollectorId, string CollectorName, string ProposedStall, MarketSection? ProposedSection, string Note,
    DateTime SubmittedAtUtc, NpmRegistrationRequestStatus Status, int Version, string? ReviewedBy, DateTime? ReviewedAtUtc,
    string? ReviewReason, Guid? AssociationId, Guid? ReplacesRequestId, string FinancialState = "No cash received; registration request only");
public sealed record NpmRegistrationRequestPage(IReadOnlyList<NpmRegistrationRequestDto> Rows, int Total, int Page, int PageSize);

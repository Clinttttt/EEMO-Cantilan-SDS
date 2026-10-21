using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Entities.Revenue;

namespace EEMOCantilanSDS.Application.Dtos.Revenue;

/// <summary>A posted direct Fish/Meat Vendor Fee recorded against a payer name only, with no confirmed vendor registration yet.</summary>
public sealed record FishMeatPayorFollowUpRowDto(
    Guid CollectionId, string ReferenceCode, DateOnly BusinessDate, DateTime RecordedAtUtc, string Payor, decimal Amount, string? CollectorName);

public sealed record FishMeatPayorFollowUpPageDto(IReadOnlyList<FishMeatPayorFollowUpRowDto> Rows, int Total);

/// <summary>A registered vendor offered to the office as a possible match. A suggestion is never a link.</summary>
public sealed record FishMeatPayorSuggestionDto(FishMeatVendorRegistrationDto Registration, string Reason);

public sealed record FishMeatPayorFollowUpDetailDto(
    FishMeatPayorFollowUpRowDto Row, string? DocumentNumber, string? DocumentKind, string Disposition, int TaxYear,
    IReadOnlyList<FishMeatPayorSuggestionDto> Suggestions, string? LinkedVendor, DateTime? LinkedAtUtc, string? LinkedBy);

/// <summary>
/// Resolve a follow-up. Exactly one of <see cref="VendorRegistrationId"/> (link to an existing registration of the collection's tax year) or
/// <see cref="Register"/> (create the registration, then link) is given. The Collection, its SRC and its amount are never touched.
/// </summary>
public sealed record FishMeatPayorResolveRequest(
    Guid ClientOperationId, Guid CollectionId, Guid? VendorRegistrationId = null, FishMeatPayorRegistration? Register = null, string? Note = null);

public sealed record FishMeatPayorRegistration(
    FishMeatVendorType VendorType, string DisplayName, string? BusinessName = null, string? Address = null,
    VendorRegistrationKind RegistrationKind = VendorRegistrationKind.New,
    string? LastName = null, string? FirstName = null, string? MiddleName = null,
    string? NameExtension = null, string? Sex = null, string? NatureOfBusiness = null);

public sealed record FishMeatPayorResolveResult(Guid CollectionId, FishMeatVendorRegistrationDto Registration, bool Registered);

using EEMOCantilanSDS.Domain.Entities.Revenue;

namespace EEMOCantilanSDS.Application.Dtos.Mobile;

public sealed record NpmVendorAssociationDto(Guid Id, Guid VendorRegistrationId, Guid ContractId, Guid StallId,
    string OccupantSnapshot, string ConfirmedBy, DateTime ConfirmedAtUtc, string ConfirmationNote,
    DateTime? CorrectedAtUtc, string? CorrectedBy, string? CorrectionReason);
public sealed record NpmVendorCandidate(Guid ContractId, Guid StallId, string StallNo, string Occupant,
    string Area, bool Active, DateOnly EffectiveFrom, FishMeatVendorType? SuggestedType, string SourceVersion,
    IReadOnlyList<NpmVendorAssociationDto> Links, IReadOnlyList<FishMeatVendorRegistrationDto> PossibleMatches);
public sealed record NpmVendorCandidates(int TaxYear, int UnresolvedCount, IReadOnlyList<NpmVendorCandidate> Rows);
public sealed record ConfirmNpmVendorAssociationRequest(Guid ClientOperationId, Guid ContractId, Guid StallId,
    string SourceVersion, string ConfirmationNote, bool ExceptionConfirmed = false, bool DuplicateReviewConfirmed = false);
public sealed record CorrectNpmVendorAssociationRequest(Guid ClientOperationId, string Reason);

using EEMOCantilanSDS.Domain.Enums;
namespace EEMOCantilanSDS.Application.Dtos.Revenue;
public sealed record DirectFineCategoryDto(Guid AuthorityId, string Name, GovernedServiceBasis Basis,
    decimal? FixedAmount, decimal? MaximumAmount, bool RequiresPayerParticulars, bool RequiresReason,
    bool RequiresSupportingReference, string GoverningReference);
public sealed record DirectFineOptionsDto(DateOnly BusinessDate, IReadOnlyList<DirectFineCategoryDto> Categories, string RecoveryScope = "");
public sealed record DirectFineCollectionRequest(Guid ClientOperationId, Guid AuthorityId, DateOnly BusinessDate,
    decimal Amount, string? Reason = null, string? SupportingReference = null, string? PayerParticulars = null);
public sealed record DirectFineQuoteDto(DirectFineCollectionRequest Intent, string Category, string GoverningReference,
    RevenueInstrumentType Instrument);

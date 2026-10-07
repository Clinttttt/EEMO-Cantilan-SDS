using EEMOCantilanSDS.Domain.Enums;
namespace EEMOCantilanSDS.Application.Dtos.Revenue;

public sealed record TaboBatchRequest(IReadOnlyList<FeeSchedulePostRequest> Items, string? QuoteFingerprint = null);
public sealed record TaboBatchItemQuote(Guid ClientOperationId, Guid? VendorId, DateOnly MarketDate,
    decimal Amount, RevenueInstrumentType? Instrument, string? ProblemCode, string? Message, string? Version);
public sealed record TaboBatchQuoteDto(IReadOnlyList<TaboBatchItemQuote> Items, decimal Total, bool CanRecord, string Fingerprint);
public sealed record TaboBatchOutcomeDto(IReadOnlyList<GovernedServiceOutcomeDto> Collections, decimal Total);

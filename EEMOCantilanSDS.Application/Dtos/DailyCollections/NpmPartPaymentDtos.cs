using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.DailyCollections;

/// <summary>One installment a part payment reaches. <see cref="Status"/> is "Paid", "Partial" or "Unpaid" after the payment.</summary>
public sealed record NpmPartPaymentLineDto(DateOnly Day, decimal Fee, decimal Settled, decimal Applied, decimal Remaining, string Status);

/// <summary>
/// The server's preview of an amount received for an NPM stall's month. <see cref="Outcome"/> is "Applied" (recordable),
/// "ExceedsPayable", "NothingPayable" or "Invalid". <see cref="Payable"/> is what the month owes in installments today;
/// <see cref="Adjustment"/> is a closed short month's separately collected top-up, never taken in parts. Records nothing.
/// </summary>
public sealed record NpmPartPaymentQuoteDto(Guid StallId, int Year, int Month, DateOnly BusinessDate, string Outcome, string? Message,
    decimal Amount, decimal Applied, decimal Payable, decimal RemainingAfter, decimal Adjustment, decimal Unallocated,
    IReadOnlyList<NpmPartPaymentLineDto> Lines, string QuoteToken, RevenueInstrumentType? Instrument,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    CollectionSourceKind? SettlementSourceKind = null);

/// <summary>Records the amount a reviewed quote previewed. The token and amount must still describe the month.</summary>
public sealed record NpmPartPaymentRequest(Guid ClientOperationId, Guid StallId, int Year, int Month,
    DateOnly BusinessDate, decimal Amount, string QuoteToken);

/// <summary>The recorded part payment: its Collection, SRC and actual amount. <see cref="Existing"/> is true when a retry returned the earlier one.</summary>
public sealed record NpmPartPaymentOutcomeDto(Guid CollectionId, string ReferenceCode, decimal Amount, bool Existing);

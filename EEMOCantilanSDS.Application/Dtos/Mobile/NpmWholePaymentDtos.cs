namespace EEMOCantilanSDS.Application.Dtos.Mobile;

public sealed record NpmWholePaymentQuoteDto(Guid StallId, int Year, int Month, DateOnly BusinessDate,
    int Days, decimal Amount, decimal Adjustment, string QuoteToken,
    EEMOCantilanSDS.Domain.Enums.RevenueInstrumentType Instrument);

public sealed record NpmWholePaymentRequest(Guid ClientOperationId, Guid StallId, int Year, int Month,
    DateOnly BusinessDate, decimal Amount, string QuoteToken);

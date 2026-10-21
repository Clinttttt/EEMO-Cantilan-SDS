namespace EEMOCantilanSDS.Application.Dtos.Mobile;

public sealed record NpmWholePaymentQuoteDto(Guid StallId, int Year, int Month, DateOnly BusinessDate,
    int Days, decimal Amount, decimal Adjustment, string QuoteToken,
    EEMOCantilanSDS.Domain.Enums.RevenueInstrumentType Instrument, decimal? MonthlyObligation = null, decimal? Collected = null, decimal? Credits = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    EEMOCantilanSDS.Domain.Enums.CollectionSourceKind? SettlementSourceKind = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] long? SettlementVersion = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] Guid? ResponsibleContractId = null);

public sealed record NpmWholePaymentRequest(Guid ClientOperationId, Guid StallId, int Year, int Month,
    DateOnly BusinessDate, decimal Amount, string QuoteToken, string? OfficialReceiptNumber = null);

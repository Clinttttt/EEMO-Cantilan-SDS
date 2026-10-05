using EEMOCantilanSDS.Domain.Enums;
namespace EEMOCantilanSDS.Application.Dtos.Mobile;

public sealed record DirectVendorFeeRequest(Guid ClientOperationId, DateOnly BusinessDate, Guid StallId, Guid PayorId, decimal AmountReceived);
public sealed record DirectVendorFeeSource(Guid StallId, string StallNo, string Section, Guid? PayorId, string PayerName, bool CanCollect, string? Reason);
public sealed record DirectVendorFeeQuote(DirectVendorFeeSource Source, decimal Amount, RevenueInstrumentType Instrument, string Version);

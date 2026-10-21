using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.DailyCollections;

public sealed record NpmOutstandingRentPeriod(DateOnly PeriodStart, CollectionSourceKind SourceKind,
    decimal Outstanding, decimal Applied, decimal Remaining);
public sealed record NpmOutstandingRentQuote(Guid StallId, Guid ContractId, string StallNo, string PayorName,
    DateOnly BusinessDate, decimal TotalOutstanding, decimal Amount, decimal RemainingAfter,
    string QuoteToken, IReadOnlyList<NpmOutstandingRentPeriod> Periods);
public sealed record NpmOutstandingRentPayment(Guid ClientOperationId, Guid StallId, Guid ContractId,
    DateOnly BusinessDate, decimal Amount, string QuoteToken, string? OfficialReceiptNumber = null);
public sealed record NpmOutstandingRentAccount(Guid StallId, Guid ContractId, string StallNo, string PayorName,
    string SectionName, decimal? Outstanding, string? BlockingReason = null);
public sealed record NpmOutstandingRentAccountsPage(IReadOnlyList<NpmOutstandingRentAccount> Accounts,
    int? NextOffset);

using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Requests.Mobile;

public sealed record RecordMobileMonthlyCollectionRequest(
    Guid StallId,
    PaymentStatus Status,
    decimal? PartialAmount = null,
    string? ORNumber = null,
    // The billing month being collected: omitted = this month (the facility's own monthly screen); an earlier month collects a delinquent month.
    int? BillingYear = null,
    int? BillingMonth = null);

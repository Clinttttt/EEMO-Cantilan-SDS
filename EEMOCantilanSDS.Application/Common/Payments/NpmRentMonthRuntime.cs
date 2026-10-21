using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Payments;

/// <summary>
/// One coordinated server boundary. No configuration binding or production registration enables it in this sprint.
/// Schema verification, complete path coordination AND an explicitly accepted municipality are all required.
/// </summary>
public sealed record NpmRentMonthRuntime(bool Enabled = false, bool SchemaVerified = false,
    bool CoordinatedPathsReady = false, IReadOnlySet<Guid>? AcceptedMunicipalities = null)
{
    public bool Allows(Guid municipalityId) => Enabled && SchemaVerified && CoordinatedPathsReady
        && municipalityId != Guid.Empty && AcceptedMunicipalities?.Contains(municipalityId) == true;
}

public enum NpmRentPaymentScope { WholeMonth = 1, DailyInstallment = 2, PartPayment = 3 }
public sealed class NpmMonthReportScopeException() : InvalidOperationException(
    "This NPM occupancy uses monthly settlement. Select a monthly or yearly compliance view; a payment cannot be assigned to invented daily receipts.");
public sealed record NpmRentMonthDayReview(bool IsActivated, NpmRentMonthQuote? Quote);
public sealed record NpmRentMonthCreditRequest(Guid ClientOperationId, Guid StallId, DateOnly PeriodStart, long ExpectedVersion,
    decimal ApprovedCredits, string AuthorityReference, string Reason);
public sealed record NpmRentMonthCreditOutcome(long Version, bool Existing);
public sealed record NpmRentMonthReplacementRequest(Guid ClientOperationId, Guid CollectionId, long ExpectedVersion,
    decimal Amount, string AuthorityReference, string Reason);
public sealed record NpmRentMonthReverseRequest(Guid ClientOperationId, Guid CollectionId, Guid StallId, DateOnly PeriodStart,
    long ExpectedVersion, string Reason);
public sealed record NpmRentMonthHeadReview(NpmRentMonthQuote Quote, IReadOnlyList<NpmRentMonthCashEvent> Cash,
    IReadOnlyList<Guid> RemittedCollectionIds);
public sealed record NpmRentMonthReportPosition(EEMOCantilanSDS.Domain.Common.Billing.NpmRentMonthPosition Position, decimal EarnedLiability,
    NpmOpeningMonthRateEvidence? OpeningAuthority = null);
public sealed record NpmRentMonthCashMovement(Guid MonthId, Guid StallId, Guid ContractId, DateOnly PeriodStart,
    Guid CollectionId, DateOnly BusinessDate, decimal Amount, string? SourceSnapshot = null)
{
    public string? SectionKey => SnapshotText("sectionKey");
    public string? Occupant => SnapshotText("actualOccupant");
    private string? SnapshotText(string key)
    {
        if (string.IsNullOrWhiteSpace(SourceSnapshot)) return null;
        using var json = System.Text.Json.JsonDocument.Parse(SourceSnapshot);
        return json.RootElement.TryGetProperty(key, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() : null;
    }
}
public sealed record NpmRentMonthQuote(Guid MonthId, Guid StallId, Guid ContractId, DateOnly PeriodStart,
    DateOnly BusinessDate, NpmRentPaymentScope Scope, long Version, decimal Assessment, decimal Credits,
    decimal Received, decimal Outstanding, decimal EligibleAmount, decimal HistoricalExcess, string Token,
    RevenueInstrumentType Instrument, DateOnly? InstallmentDate = null, decimal? ApprovedDailyInstallment = null,
    NpmOpeningMonthRateEvidence? OpeningAuthority = null);
public sealed record NpmRentMonthPayment(Guid ClientOperationId, Guid StallId, DateOnly PeriodStart,
    DateOnly BusinessDate, NpmRentPaymentScope Scope, decimal Amount, string QuoteToken, DateOnly? InstallmentDate = null,
    string? OfficialReceiptNumber = null);
public sealed record NpmRentMonthPaymentOutcome(Guid CollectionId, string ReferenceCode, decimal Amount, bool Existing);
public sealed record NpmRentMonthCashEvent(Guid AllocationId, Guid CollectionId, string ReferenceCode,
    DateOnly BusinessDate, DateTime RecordedAtUtc, DateOnly? InstallmentDate, decimal Received,
    decimal CorrectionEffect, Guid? CollectorId)
{
    public decimal Net => Received + CorrectionEffect;
}

/// <summary>Internal capability: other canonical workflows cannot accidentally post the dormant month kind.</summary>
public sealed class NpmRentMonthPostingPermit
{
    internal NpmRentMonthPostingPermit(Guid tenant, Guid operation, Guid month) : this(tenant, operation, new HashSet<Guid> { month }) { }
    internal NpmRentMonthPostingPermit(Guid tenant, Guid operation, IReadOnlySet<Guid> months) => (Tenant, Operation, Months) = (tenant, operation, months);
    internal Guid Tenant { get; }
    internal Guid Operation { get; }
    internal IReadOnlySet<Guid> Months { get; }
}

using EEMOCantilanSDS.Domain.Entities.Payments;

namespace EEMOCantilanSDS.Domain.Common.Billing;

/// <summary>
/// A projection of approved assessment and immutable payment evidence, never another cash ledger.
/// Legacy-only evidence, canonical daily allocations and month allocations must be disjoint.
/// Corrections remain linked to the exact allocation they correct.
/// </summary>
public sealed record NpmRentMonthPosition(
    Guid MunicipalityId, Guid MonthId, Guid ContractId, DateOnly PeriodStart, long Version,
    decimal Assessment, decimal Credits, decimal LegacyCash, decimal CanonicalCash,
    decimal Outstanding, decimal HistoricalExcess)
{
    public static NpmRentMonthPosition From(
        NpmRentMonth month, IReadOnlyCollection<NpmRentCashEvidence> receipts,
        IReadOnlyCollection<NpmRentCorrectionEvidence> corrections)
    {
        if (month.Status is not (NpmRentMonthPreparationStatus.Reconciled or NpmRentMonthPreparationStatus.FinanciallyActivated) || month.MonthBasis is null)
            throw new InvalidOperationException("Month ownership, assessment and historical evidence require reconciliation.");
        if (receipts.Any(x => x.EvidenceId == Guid.Empty) || receipts.Select(x => x.EvidenceId).Distinct().Count() != receipts.Count)
            throw new InvalidOperationException("Duplicate or missing payment evidence cannot establish a month balance.");
        if (corrections.Any(x => x.CorrectionAllocationId == Guid.Empty)
            || corrections.Select(x => x.CorrectionAllocationId).Distinct().Count() != corrections.Count)
            throw new InvalidOperationException("Duplicate or missing correction evidence.");
        if (receipts.Any(x => x.Kind == NpmRentCashEvidenceKind.CanonicalMonthAllocation
                ? x.DailySourceId is not null : x.DailySourceId is null || x.DailySourceId == Guid.Empty)
            || receipts.Where(x => x.Kind == NpmRentCashEvidenceKind.LegacyDaily)
                .Any(x => x.EvidenceId != x.DailySourceId || receipts.Any(other =>
                    other.Kind == NpmRentCashEvidenceKind.CanonicalDailyAllocation && other.DailySourceId == x.DailySourceId)))
            throw new InvalidOperationException("Daily operational evidence cannot also be counted as canonical cash.");
        decimal legacy = 0, canonical = 0;
        foreach (var receipt in receipts)
        {
            if (receipt.MunicipalityId != month.MunicipalityId || receipt.ContractId != month.ContractId
                || receipt.StallId != month.StallId || receipt.PeriodStart != month.PeriodStart)
                throw new InvalidOperationException("Payment evidence belongs to a different tenant, stall, occupancy or month.");
            Money(receipt.Amount, positive: true);
            if (!Enum.IsDefined(receipt.Kind)) throw new InvalidOperationException("Unknown rent evidence authority.");
            var effects = corrections.Where(x => x.OriginalEvidenceId == receipt.EvidenceId).ToArray();
            if (receipt.Kind == NpmRentCashEvidenceKind.LegacyDaily && effects.Length != 0)
                throw new InvalidOperationException("Legacy reconciliation requires separately reviewed original evidence; canonical corrections cannot rewrite legacy cash.");
            var net = receipt.Amount;
            foreach (var effect in effects)
            {
                if (effect.MunicipalityId != month.MunicipalityId)
                    throw new InvalidOperationException("Correction belongs to another tenant.");
                Money(effect.FinancialEffectAmount, positive: false);
                net = checked(net + effect.FinancialEffectAmount);
            }
            if (net < 0m) throw new InvalidOperationException("Corrections exceed their original rent allocation.");
            if (receipt.Kind == NpmRentCashEvidenceKind.LegacyDaily) legacy = checked(legacy + net);
            else canonical = checked(canonical + net);
        }
        if (corrections.Any(x => !receipts.Any(r => r.EvidenceId == x.OriginalEvidenceId)))
            throw new InvalidOperationException("A correction is missing its authoritative original allocation.");
        var received = checked(legacy + canonical);
        var liability = month.AssessedAmount - month.CreditedAmount;
        return new(month.MunicipalityId, month.Id, month.ContractId, month.PeriodStart, month.SettlementVersion,
            month.AssessedAmount, month.CreditedAmount, legacy, canonical,
            Math.Max(0m, liability - received), Math.Max(0m, received - liability));
    }

    /// <summary>Whole settlement uses the exact remaining month amount; no day count or daily rate is fabricated.</summary>
    public decimal WholePayment => Outstanding;

    /// <summary>Prospective full opening rent still earns through real occupied days; no new due date is inferred.</summary>
    public decimal EarnedInstallmentLiability(decimal approvedInstallment, DateOnly occupancyStart, DateOnly cutoff)
    {
        Money(approvedInstallment, positive: true);
        if (occupancyStart < PeriodStart || occupancyStart >= PeriodStart.AddMonths(1))
            throw new ArgumentException("The opening occupancy must begin within this assessment month.", nameof(occupancyStart));
        if (cutoff < occupancyStart) return 0m;
        var earned = cutoff >= PeriodStart.AddMonths(1) ? Assessment
            : Math.Min(Assessment, approvedInstallment * (cutoff.DayNumber - occupancyStart.DayNumber + 1));
        return Math.Max(0m, earned - Credits);
    }

    /// <summary>The approved installment is a ceiling, including the genuine terminal remainder.</summary>
    public decimal DailyInstallment(decimal approvedInstallment)
    {
        Money(approvedInstallment, positive: true);
        return Math.Min(approvedInstallment, Outstanding);
    }

    public void RequirePayment(decimal amount, decimal eligibleAmount, long expectedVersion)
    {
        Money(amount, positive: true); Money(eligibleAmount, positive: false);
        if (Version != expectedVersion) throw new InvalidOperationException("Stale month settlement version.");
        if (amount > Outstanding || amount > eligibleAmount)
            throw new InvalidOperationException("Payment exceeds the authoritative eligible month balance.");
        if (HistoricalExcess != 0m)
            throw new InvalidOperationException("Historical excess requires audited reconciliation; it cannot be used as an advance.");
    }

    private static void Money(decimal amount, bool positive)
    {
        if ((positive ? amount <= 0m : amount == 0m) || Math.Abs(amount) > 9_999_999_999_999_999.99m
            || decimal.Round(amount, 2) != amount)
            throw new ArgumentOutOfRangeException(nameof(amount), "Valid cent evidence is required.");
    }
}

public enum NpmRentCashEvidenceKind { LegacyDaily = 1, CanonicalDailyAllocation = 2, CanonicalMonthAllocation = 3 }
public sealed record NpmRentCashEvidence(Guid EvidenceId, Guid MunicipalityId, Guid StallId, Guid ContractId,
    DateOnly PeriodStart, NpmRentCashEvidenceKind Kind, decimal Amount, Guid? DailySourceId = null);
public sealed record NpmRentCorrectionEvidence(Guid CorrectionAllocationId, Guid OriginalEvidenceId,
    Guid MunicipalityId, decimal FinancialEffectAmount);

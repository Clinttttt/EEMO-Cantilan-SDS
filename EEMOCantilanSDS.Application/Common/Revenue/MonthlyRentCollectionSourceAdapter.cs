using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Application.Queries.Mobile.GetMobileMonthlyCollection;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// Adapts the existing monthly PaymentRecord source without creating a second assessment or balance authority.
/// It exposes BaseRentalAmount only; legacy utility and fish components remain outside the rent classification.
/// </summary>
public sealed class MonthlyRentCollectionSourceAdapter(IAppDbContext db)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<RentSourceFacts?> LoadAsync(
        Guid tenantId,
        Guid stallId,
        int year,
        int month,
        DateOnly businessDate,
        bool tracked,
        bool materializeMissingSource,
        string actor,
        CancellationToken ct)
    {
        if (year is < 2000 or > 2200 || month is < 1 or > 12)
            throw new InvalidOperationException("A valid rental billing year and month are required.");

        IQueryable<Stall> stalls = db.Stalls
            .Where(x => x.Id == stallId && x.MunicipalityId == tenantId)
            .Include(x => x.Facility)
            .Include(x => x.Contracts).ThenInclude(x => x.Payor);
        if (!tracked) stalls = stalls.AsNoTracking();
        var stall = await stalls.SingleOrDefaultAsync(ct);
        if (stall?.Facility is null || !MonthlyRentalFacilities.Codes.Contains(stall.Facility.Code))
            return null;

        var periodStart = new DateOnly(year, month, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);
        if (periodStart > businessDate) return null;
        var occupancy = stall.OccupancyAnsweringForMonth(year, month, businessDate);
        if (occupancy is null || !occupancy.Contract.BillsCalendarMonth(year, month))
            return null;

        var excused = await db.StallMonthlyExceptions.AsNoTracking().AnyAsync(x =>
            x.MunicipalityId == tenantId && x.StallId == stallId
            && x.BillingYear == year && x.BillingMonth == month, ct);
        if (excused)
            return null;

        IQueryable<PaymentRecord> records = db.PaymentRecords
            .Where(x => x.MunicipalityId == tenantId && x.StallId == stallId
                && x.BillingYear == year && x.BillingMonth == month);
        if (!tracked) records = records.AsNoTracking();
        var record = await records.SingleOrDefaultAsync(ct);
        if (record is null)
        {
            if (!materializeMissingSource)
                return await BuildFacts(tenantId, null, stall, occupancy.Contract, year, month, businessDate, ct);

            // Materialize the already-existing specialized monthly assessment as an Unpaid PaymentRecord. This is
            // an operation-owned source row, not a Collection, allocation, payment, or alternate balance.
            var assessedRent = occupancy.Contract.MonthlyRentalRate > 0m
                ? occupancy.Contract.MonthlyRentalRate
                : stall.MonthlyRate;
            if (assessedRent <= 0m)
                throw new InvalidOperationException("The answerable monthly occupancy has no approved rental amount.");
            record = PaymentRecord.Create(stallId, year, month, assessedRent, actor);
            db.PaymentRecords.Add(record);
        }

        return await BuildFacts(tenantId, record, stall, occupancy.Contract, year, month, businessDate, ct);
    }

    public async Task<RentSourceFacts?> LoadByRecordIdAsync(
        Guid tenantId, Guid paymentRecordId, DateOnly businessDate, bool tracked, CancellationToken ct)
    {
        IQueryable<PaymentRecord> records = db.PaymentRecords
            .Where(x => x.MunicipalityId == tenantId && x.Id == paymentRecordId)
            .Include(x => x.Stall!).ThenInclude(x => x.Facility)
            .Include(x => x.Stall!).ThenInclude(x => x.Contracts).ThenInclude(x => x.Payor);
        if (!tracked) records = records.AsNoTracking();
        var record = await records.SingleOrDefaultAsync(ct);
        if (record?.Stall is null || record.Stall.Facility is null
            || !MonthlyRentalFacilities.Codes.Contains(record.Stall.Facility.Code))
            return null;

        var periodStart = new DateOnly(record.BillingYear, record.BillingMonth, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);
        if (periodStart > businessDate) return null;
        var occupancy = record.Stall.OccupancyAnsweringForMonth(
            record.BillingYear, record.BillingMonth, businessDate);
        if (occupancy is null || !occupancy.Contract.BillsCalendarMonth(record.BillingYear, record.BillingMonth))
            return null;
        var excused = await db.StallMonthlyExceptions.AsNoTracking().AnyAsync(x =>
            x.MunicipalityId == tenantId && x.StallId == record.StallId
            && x.BillingYear == record.BillingYear && x.BillingMonth == record.BillingMonth, ct);
        if (excused) return null;
        return await BuildFacts(tenantId, record, record.Stall, occupancy.Contract,
            record.BillingYear, record.BillingMonth, businessDate, ct);
    }

    public async Task<IReadOnlyList<RentObligationQuoteDto>> GetPayorObligationsAsync(
        Guid tenantId, Guid payorId, DateOnly businessDate, CancellationToken ct)
    {
        var stalls = await db.Stalls.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && x.Contracts.Any(c =>
                c.MunicipalityId == tenantId && c.PayorId == payorId))
            .Include(x => x.Facility)
            .Include(x => x.Contracts).ThenInclude(x => x.Payor)
            .ToListAsync(ct);
        stalls = stalls.Where(x => x.Facility is not null
            && MonthlyRentalFacilities.Codes.Contains(x.Facility.Code)).ToList();
        if (stalls.Count == 0) return [];

        var stallIds = stalls.Select(x => x.Id).ToArray();
        var records = await db.PaymentRecords.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && stallIds.Contains(x.StallId)
                && (x.BillingYear < businessDate.Year
                    || (x.BillingYear == businessDate.Year && x.BillingMonth <= businessDate.Month)))
            .ToDictionaryAsync(x => (x.StallId, x.BillingYear, x.BillingMonth), ct);
        var exceptions = await db.StallMonthlyExceptions.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && stallIds.Contains(x.StallId))
            .Select(x => new { x.StallId, x.BillingYear, x.BillingMonth })
            .ToListAsync(ct);
        var exceptionKeys = exceptions.Select(x => (x.StallId, x.BillingYear, x.BillingMonth)).ToHashSet();
        var classification = await db.RevenueClassifications.AsNoTracking().SingleOrDefaultAsync(x =>
            x.MunicipalityId == tenantId && x.SemanticCode == RevenueClassificationCodes.PermanentStallRent
            && x.IsActive, ct);
        var policy = classification is null ? null : await db.RevenueClassificationPolicies.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId
                && x.RevenueClassificationId == classification.Id
                && x.BusinessContext == RevenuePolicyContext.Default
                && x.EffectiveDate <= businessDate)
            .OrderByDescending(x => x.EffectiveDate).FirstOrDefaultAsync(ct);
        if (classification is null || policy?.PermittedInstrumentType != RevenueInstrumentType.OfficialReceipt)
            throw new InvalidOperationException("Permanent Stall Rent has no effective Official Receipt policy for this tenant.");
        var policyFacts = new RentPolicyFacts(classification, policy);
        var quotes = new List<RentObligationQuoteDto>();
        var periods = stalls.SelectMany(stall => stall.Contracts
                .Where(contract => contract.PayorId == payorId && contract.DurationYears > 0)
                .SelectMany(contract =>
                {
                    var start = new DateOnly(contract.EffectivityDate.Year, contract.EffectivityDate.Month, 1);
                    var expiry = Contract.ComputeExpiry(contract.EffectivityDate, contract.DurationYears);
                    var latest = businessDate < expiry ? businessDate : expiry;
                    var end = new DateOnly(latest.Year, latest.Month, 1);
                    var result = new List<(Stall Stall, int Year, int Month)>();
                    for (var month = start; month <= end; month = month.AddMonths(1))
                        if (ContractMonthBills(contract.DurationYears, contract.EffectivityDate, month.Year, month.Month))
                            result.Add((stall, month.Year, month.Month));
                    return result;
                }))
            .DistinctBy(x => (x.Stall.Id, x.Year, x.Month))
            .OrderBy(x => x.Year).ThenBy(x => x.Month).ThenBy(x => x.Stall.StallNo, StringComparer.OrdinalIgnoreCase);
        foreach (var period in periods)
        {
            var stall = period.Stall;
            var occupancy = stall.OccupancyAnsweringForMonth(period.Year, period.Month, businessDate);
            if (occupancy is null || occupancy.Contract.PayorId != payorId
                || !occupancy.Contract.BillsCalendarMonth(period.Year, period.Month)
                || exceptionKeys.Contains((stall.Id, period.Year, period.Month)))
                continue;
            records.TryGetValue((stall.Id, period.Year, period.Month), out var record);
            var facts = await BuildFacts(tenantId, record, stall, occupancy.Contract,
                period.Year, period.Month, businessDate, ct, policyFacts);
            if (facts.Quote.OutstandingAmount > 0m) quotes.Add(facts.Quote);
        }
        return quotes;
    }

    private async Task<RentSourceFacts> BuildFacts(
        Guid tenantId,
        PaymentRecord? record,
        Stall stall,
        Contract contract,
        int billingYear,
        int billingMonth,
        DateOnly businessDate,
        CancellationToken ct,
        RentPolicyFacts? policyFacts = null)
    {
        var classification = policyFacts?.Classification ?? await db.RevenueClassifications.SingleOrDefaultAsync(x =>
            x.MunicipalityId == tenantId && x.SemanticCode == RevenueClassificationCodes.PermanentStallRent
            && x.IsActive, ct);
        var policy = policyFacts?.Policy ?? (classification is null ? null : await db.RevenueClassificationPolicies
            .Where(x => x.MunicipalityId == tenantId
                && x.RevenueClassificationId == classification.Id
                && x.BusinessContext == RevenuePolicyContext.Default
                && x.EffectiveDate <= businessDate)
            .OrderByDescending(x => x.EffectiveDate)
            .FirstOrDefaultAsync(ct));
        if (classification is null || policy?.PermittedInstrumentType != RevenueInstrumentType.OfficialReceipt)
            throw new InvalidOperationException("Permanent Stall Rent has no effective Official Receipt policy for this tenant.");

        var payor = contract.Payor is { MunicipalityId: var payorTenant } linked && payorTenant == tenantId
            ? contract.Payor
            : null;
        var payorId = payor?.Id;
        var payerName = payor?.DisplayName
            ?? (string.IsNullOrWhiteSpace(contract.ActualOccupant) ? null : contract.ActualOccupant.Trim());
        var assessed = record?.BaseRentalAmount ?? (contract.MonthlyRentalRate > 0m
            ? contract.MonthlyRentalRate
            : stall.MonthlyRate);
        var mixedLegacyComponents = record is not null
            && (record.ElecAmount.GetValueOrDefault() != 0m
                || record.WaterAmount.GetValueOrDefault() != 0m
                || record.FishFeeAmount.GetValueOrDefault() != 0m);
        var ambiguousLegacySettlement = record is not null && mixedLegacyComponents
            && (record.SettlementAuthorityState == SettlementAuthority.Canonical
                || (record.SettlementAuthorityState == SettlementAuthority.Legacy
                    && record.Status == PaymentStatus.Partial));

        decimal settled;
        if (record is null)
        {
            settled = 0m;
        }
        else if (record.SettlementAuthorityState == SettlementAuthority.Canonical)
        {
            if (mixedLegacyComponents)
                throw new InvalidOperationException("A canonical rent source still contains legacy utility or fish components and requires reconciliation.");
            if (record.SettlementCutoverId is not { } cutoverId)
                throw new InvalidOperationException("Canonical rent source has no frozen cutover evidence.");
            var cutover = await db.CollectionSettlementCutovers.AsNoTracking().SingleOrDefaultAsync(x =>
                x.Id == cutoverId && x.MunicipalityId == tenantId
                && x.SourceKind == CollectionSourceKind.PaymentRecord
                && x.SourceId == record.Id && x.SourcePart == null, ct);
            if (cutover is null || cutover.OpeningAssessmentAmount != assessed)
                throw new InvalidOperationException("Canonical rent cutover evidence does not match this PaymentRecord source.");
            var allocations = db.CollectionAllocations.AsNoTracking().Where(x =>
                x.MunicipalityId == tenantId && x.SourceKind == CollectionSourceKind.PaymentRecord
                && x.SourceId == record.Id && x.SourcePart == null);
            var allocated = await allocations.Select(x => (decimal?)x.Amount).SumAsync(ct) ?? 0m;
            var allocationIds = allocations.Select(x => x.Id);
            var corrections = await db.CollectionCorrectionAllocations.AsNoTracking()
                .Where(x => x.MunicipalityId == tenantId && allocationIds.Contains(x.OriginalAllocationId))
                .Select(x => (decimal?)x.FinancialEffectAmount).SumAsync(ct) ?? 0m;
            settled = cutover.OpeningLegacySettledAmount + allocated + corrections;
        }
        else if (record.Status == PaymentStatus.Paid)
        {
            // Paid means the complete legacy TotalBill was received, so the rent component is fully settled.
            settled = assessed;
        }
        else if (record.Status == PaymentStatus.Partial && !mixedLegacyComponents)
        {
            settled = Math.Min(assessed, record.PartialAmount);
        }
        else
        {
            settled = 0m;
        }

        var outstanding = Math.Max(0m, assessed - settled);
        var canAdd = outstanding > 0m && !ambiguousLegacySettlement
            && (record is null || record.SettlementAuthorityState != SettlementAuthority.PendingCutover);
        var canPost = record is not null && canAdd
            && record.SettlementAuthorityState == SettlementAuthority.Canonical;
        var snapshot = new RentSourceSnapshot(
            1, record?.Id, record?.SettlementVersion ?? 0, stall.Id, stall.StallNo,
            stall.Facility!.Id, stall.Facility.Name, contract.Id, contract.UpdatedAt,
            contract.PayorId == payorId ? payorId : null, payerName,
            billingYear, billingMonth,
            assessed, settled, outstanding, classification.Id, policy.Id, policy.DisplayName,
            RevenueInstrumentType.OfficialReceipt, "MonthlyRentalRate",
            record?.SettlementAuthorityState ?? SettlementAuthority.Legacy,
            record?.BaseRentalAmount ?? assessed, record?.ElecAmount, record?.WaterAmount, record?.FishKilos,
            ambiguousLegacySettlement);
        var quote = new RentObligationQuoteDto(
            tenantId, record?.Id, CollectionSourceKind.PaymentRecord, stall.Id, stall.StallNo,
            stall.Facility.Name, billingYear, billingMonth,
            contract.Id, payorId, payerName, assessed, settled, outstanding,
            record?.SettlementAuthorityState ?? SettlementAuthority.Legacy,
            record?.SettlementVersion ?? 0, classification.Id, policy.Id, policy.DisplayName,
            RevenueInstrumentType.OfficialReceipt, contract.MonthlyRentalRate > 0m
                ? contract.MonthlyRentalRate : stall.MonthlyRate,
            canAdd, canPost, ambiguousLegacySettlement);
        return new RentSourceFacts(record, stall, contract, classification, policy, quote,
            snapshot, JsonSerializer.Serialize(snapshot, JsonOptions));
    }

    public sealed record RentSourceFacts(
        PaymentRecord? Record,
        Stall Stall,
        Contract Contract,
        RevenueClassification Classification,
        RevenueClassificationPolicy Policy,
        RentObligationQuoteDto Quote,
        RentSourceSnapshot Snapshot,
        string SnapshotJson);

    public sealed record RentSourceSnapshot(
        int SchemaVersion,
        Guid? PaymentRecordId,
        long SettlementVersion,
        Guid StallId,
        string StallNo,
        Guid FacilityId,
        string FacilityName,
        Guid ContractId,
        DateTime? ContractUpdatedAt,
        Guid? PayorId,
        string? PayerName,
        int BillingYear,
        int BillingMonth,
        decimal AssessedRentalAmount,
        decimal CumulativeSettledEvidence,
        decimal OutstandingAmount,
        Guid ClassificationId,
        Guid PolicyId,
        string ClassificationName,
        RevenueInstrumentType Instrument,
        string ChargeBasis,
        SettlementAuthority SettlementAuthority,
        decimal ContractRate,
        decimal? LegacyElectricityAmount,
        decimal? LegacyWaterAmount,
        decimal? LegacyFishKilos,
        bool AmbiguousLegacySettlement);

    private sealed record RentPolicyFacts(
        RevenueClassification Classification,
        RevenueClassificationPolicy Policy);

    private static bool ContractMonthBills(int durationYears, DateOnly effectivityDate, int year, int month)
    {
        if (durationYears <= 0) return false;
        var first = effectivityDate.Year * 12 + effectivityDate.Month - 1;
        var asked = year * 12 + month - 1;
        return asked >= first && asked < first + durationYears * 12;
    }
}

using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

/// <inheritdoc cref="ICollectorReportQueries" />
public class CollectorReportQueries(AppDbContext context) : ICollectorReportQueries
{
    public async Task<CollectorCollectionsData> GetCollectionsAsync(
        Guid collectorId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var (startUtc, _) = PhilippineTime.DayUtcRange(from);
        var (_, endUtc) = PhilippineTime.DayUtcRange(to);

        var lines = new List<CollectorCollectionLine>();
        var absences = new List<CollectorAbsenceLine>();

        // ── NPM daily fees. The fee's own day is kept, since a receipt may answer for days the payor owed. ──
        var npmFishRate = await ResolveNpmFishRateAsync(to, ct);
        var daily = await context.DailyCollections
            .AsNoTracking()
            .Where(d => d.CollectorId == collectorId
                     && (d.IsPaid || d.IsAbsent)
                     && (d.UpdatedAt ?? d.CreatedAt) >= startUtc && (d.UpdatedAt ?? d.CreatedAt) < endUtc)
            .Select(d => new
            {
                d.ORNumber,
                d.CollectionDate,
                d.DailyFee,
                d.FishKilos,
                d.MeatFeeAmount,
                d.IsAbsent,
                d.Stall!.StallNo,
                Code = d.Stall.Facility!.Code,
                Contracts = d.Stall.Contracts.Select(c => new { c.ActualOccupant, c.NameOnContract, c.EffectivityDate }).ToList(),
                When = d.UpdatedAt ?? d.CreatedAt
            })
            .ToListAsync(ct);

        foreach (var d in daily)
        {
            var payor = d.Contracts
                .OrderByDescending(c => c.EffectivityDate)
                .Select(c => string.IsNullOrWhiteSpace(c.ActualOccupant) ? c.NameOnContract : c.ActualOccupant)
                .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "No active occupant";

            if (d.IsAbsent)
            {
                absences.Add(new CollectorAbsenceLine(d.CollectionDate, payor!, d.StallNo, d.Code));
                continue;
            }

            lines.Add(new CollectorCollectionLine(
                d.ORNumber, d.When, payor!, d.StallNo, d.Code, "Daily Fee",
                // DailyFee already includes the month-end difference. Weighed charges remain distinct source facts.
                d.DailyFee + ((d.FishKilos ?? 0m) * npmFishRate) + d.MeatFeeAmount,
                d.CollectionDate, null));
        }

        // ── Monthly rentals. Fee money only: the meters are banked apart and are totalled separately below. ──
        var monthly = await context.PaymentRecords
            .AsNoTracking()
            .Where(p => p.CollectorId == collectorId
                     && p.Status != PaymentStatus.Unpaid
                     && (p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt) >= startUtc
                     && (p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt) < endUtc)
            .Select(p => new
            {
                p.ORNumber,
                p.Status,
                p.BaseRentalAmount,
                p.PartialAmount,
                p.FishKilos,
                p.BillingYear,
                p.BillingMonth,
                p.SettlementAuthorityState,
                p.Stall!.StallNo,
                Code = p.Stall.Facility!.Code,
                Contracts = p.Stall.Contracts.Select(c => new { c.ActualOccupant, c.NameOnContract, c.EffectivityDate }).ToList(),
                When = p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt
            })
            .ToListAsync(ct);

        foreach (var p in monthly)
        {
            // A converted rent row keeps legacy fields only as a compatibility projection: its canonical Collection line below is
            // the one authoritative record of that money, so the legacy figure is never counted beside it (IA-050).
            if (!CollectionSourceAuthorityMap.LegacyMoneyCounts(CollectionSourceKind.PaymentRecord, p.SettlementAuthorityState))
                continue;

            var payor = p.Contracts
                .OrderByDescending(c => c.EffectivityDate)
                .Select(c => string.IsNullOrWhiteSpace(c.ActualOccupant) ? c.NameOnContract : c.ActualOccupant)
                .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "No active occupant";

            lines.Add(new CollectorCollectionLine(
                p.ORNumber, p.When, payor!, p.StallNo, p.Code, "Stall Rental",
                CollectorFeeMoney.MonthlyFeePortion(p.Status, p.BaseRentalAmount, p.FishKilos, p.PartialAmount, npmFishRate),
                null,
                new DateOnly(p.BillingYear, p.BillingMonth, 1)));
        }

        // ── Canonical rent this collector took: the authoritative record of a converted rent row (IA-050) ──
        var rentFrom = from;
        var rentTo = to;
        var canonicalRent = await (
            from collection in context.Collections.AsNoTracking()
            join line in context.CollectionLines.AsNoTracking() on collection.Id equals line.CollectionId
            join allocation in context.CollectionAllocations.AsNoTracking() on line.Id equals allocation.CollectionLineId
            join record in context.PaymentRecords.AsNoTracking() on allocation.SourceId equals record.Id
            join stall in context.Stalls.AsNoTracking() on record.StallId equals stall.Id
            join facility in context.Facilities.AsNoTracking() on stall.FacilityId equals facility.Id
            where collection.CollectorId == collectorId
                && collection.BusinessDate >= rentFrom && collection.BusinessDate <= rentTo
                && allocation.SourceKind == CollectionSourceKind.PaymentRecord
                && record.SettlementAuthorityState == SettlementAuthority.Canonical
            select new
            {
                DocumentNumber = collection.ReferenceCode, collection.RecordedAtUtc, collection.BusinessDate, collection.PayerName,
                stall.StallNo, FacilityCode = facility.Code, record.BillingYear, record.BillingMonth, allocation.Amount
            })
            .ToListAsync(ct);

        lines.AddRange(canonicalRent.Select(x => new CollectorCollectionLine(
            x.DocumentNumber, x.RecordedAtUtc,
            string.IsNullOrWhiteSpace(x.PayerName) ? "Unidentified payor" : x.PayerName,
            x.StallNo, x.FacilityCode,
            x.FacilityCode == FacilityCode.ICE ? "Ice Plant" : "Stall Rental",
            x.Amount, null, new DateOnly(x.BillingYear, x.BillingMonth, 1), x.BusinessDate, IsCanonical: true)));

        // ── Slaughterhouse: one line per animal type on the receipt, its own date being the day it was taken ──
        var slaughter = await context.SlaughterTransactions
            .AsNoTracking()
            .Where(s => s.CollectorId == collectorId && s.TransactionDate >= from && s.TransactionDate <= to)
            .Select(s => new { s.ORNumber, s.OwnerName, s.AnimalType, s.RatePerHead, s.NumberOfHeads, s.TransactionDate, When = s.UpdatedAt ?? s.CreatedAt })
            .ToListAsync(ct);

        lines.AddRange(slaughter.Select(s => new CollectorCollectionLine(
            s.ORNumber, s.When, s.OwnerName, null, FacilityCode.SLH,
            $"{s.AnimalType} × {s.NumberOfHeads}", s.RatePerHead * s.NumberOfHeads, s.TransactionDate, null)));

        // ── Terminal trips ──
        var trips = await context.TrmTrips
            .AsNoTracking()
            .Where(t => t.CollectorId == collectorId && t.RecordedAt >= startUtc && t.RecordedAt < endUtc)
            .Select(t => new { t.ORNumber, t.DriverName, t.PlateNumber, t.Fee, t.RecordedAt })
            .ToListAsync(ct);

        lines.AddRange(trips.Select(t => new CollectorCollectionLine(
            t.ORNumber, t.RecordedAt, t.DriverName, t.PlateNumber, FacilityCode.TRM,
            "Trip Fee", t.Fee, DateOnly.FromDateTime(PhilippineTime.ToPhilippineTime(t.RecordedAt)), null)));

        // ── Tabo-an vendors, whose market day is the day they paid ──
        var taboan = await context.TpmAttendances
            .AsNoTracking()
            .Where(a => a.CollectorId == collectorId && a.IsPaid && a.MarketDate >= from && a.MarketDate <= to)
            .Select(a => new { a.ORNumber, a.Vendor!.VendorName, a.Fee, a.MarketDate, When = a.UpdatedAt ?? a.CreatedAt })
            .ToListAsync(ct);

        lines.AddRange(taboan.Select(a => new CollectorCollectionLine(
            a.ORNumber, a.When, a.VendorName, null, FacilityCode.TPM,
            "Vendor Fee", a.Fee, a.MarketDate, null)));

        // ── What the office itself recorded at this collector's facilities, stated apart from their own accountability ──
        var assigned = await context.CollectorFacilityAssignments
            .AsNoTracking()
            .Where(a => a.CollectorId == collectorId)
            .Select(a => a.FacilityCode)
            .ToListAsync(ct);

        var officeDaily = await context.DailyCollections
            .AsNoTracking()
            .Where(d => d.CollectorId == null && d.IsPaid
                     && (d.UpdatedAt ?? d.CreatedAt) >= startUtc && (d.UpdatedAt ?? d.CreatedAt) < endUtc
                     && assigned.Contains(d.Stall!.Facility!.Code))
            .Select(d => new { d.DailyFee, d.FishKilos, d.MeatFeeAmount })
            .ToListAsync(ct);

        var officeRecorded = officeDaily.Sum(d => d.DailyFee + ((d.FishKilos ?? 0m) * npmFishRate) + d.MeatFeeAmount);
        var officeReceipts = officeDaily.Count;

        // ── Electricity and water this collector took, kept in their own totals ──
        var bills = await context.UtilityBills
            .AsNoTracking()
            .Where(b => b.CollectorId == collectorId
                     && (b.UpdatedAt ?? b.CreatedAt) >= startUtc && (b.UpdatedAt ?? b.CreatedAt) < endUtc)
            .Select(b => new
            {
                b.ElecPreviousReading, b.ElecCurrentReading, b.ElecRatePerKwh, b.ElecStatus, b.ElecPartialAmount,
                b.WaterPreviousReading, b.WaterCurrentReading, b.WaterRatePerCubicMeter, b.WaterStatus, b.WaterPartialAmount,
                b.WaterSettlementAuthorityState, b.ElectricitySettlementAuthorityState
            })
            .ToListAsync(ct);

        decimal utilityBilled = 0m, utilityCollected = 0m, utilityOutstanding = 0m;
        foreach (var b in bills)
        {
            var elecCharge = Math.Max(0m, b.ElecCurrentReading - b.ElecPreviousReading) * b.ElecRatePerKwh;
            var waterCharge = Math.Max(0m, b.WaterCurrentReading - b.WaterPreviousReading) * b.WaterRatePerCubicMeter;
            var elecPaid = Collected(b.ElecStatus, elecCharge, b.ElecPartialAmount);
            var waterPaid = Collected(b.WaterStatus, waterCharge, b.WaterPartialAmount);
            utilityBilled += elecCharge + waterCharge;
            // Converted electricity is counted once, from its canonical ECF receipt line below; the legacy figure is a projection.
            if (CollectionSourceAuthorityMap.LegacyMoneyCounts(CollectionSourceKind.UtilityBill, b.ElectricitySettlementAuthorityState))
                utilityCollected += elecPaid;
            utilityOutstanding += Math.Max(0m, elecCharge - elecPaid) + Math.Max(0m, waterCharge - waterPaid);
            // Legacy cumulative fields remain a compatibility projection after source cutover.
            // Canonical WCF receipts are represented in the receipt lines below, once per CT.
            if (b.WaterSettlementAuthorityState != SettlementAuthority.Canonical)
                utilityCollected += waterPaid;
        }

        // Canonical WCF is a real collector receipt, selected by its business date. Its UtilityBill projection above is
        // used only to describe outstanding position and is excluded from utility cash so it cannot duplicate the CT.
        var businessDateFrom = from;
        var businessDateTo = to;
        var canonicalWcf = await (
            from collection in context.Collections.AsNoTracking()
            join line in context.CollectionLines.AsNoTracking() on collection.Id equals line.CollectionId
            join bill in context.UtilityBills.AsNoTracking() on line.SourceId equals bill.Id
            join stall in context.Stalls.AsNoTracking() on bill.StallId equals stall.Id
            join facility in context.Facilities.AsNoTracking() on stall.FacilityId equals facility.Id
            join classification in context.RevenueClassifications.AsNoTracking() on line.RevenueClassificationId equals classification.Id
            join policy in context.RevenueClassificationPolicies.AsNoTracking() on line.RevenueClassificationPolicyId equals policy.Id
            where (collection.CollectorId == collectorId
                && collection.BusinessDate >= businessDateFrom && collection.BusinessDate <= businessDateTo
                && line.SourceKind == CollectionSourceKind.UtilityBill
                && line.SourcePart == CollectionSourcePart.Water
                && classification.SemanticCode == RevenueClassificationCodes.Wcf
                && bill.WaterSettlementAuthorityState == SettlementAuthority.Canonical
                && policy.PermittedInstrumentType == RevenueInstrumentType.CashTicket)
            select new
            {
                DocumentNumber = collection.ReferenceCode,
                collection.RecordedAtUtc,
                collection.BusinessDate,
                PayorName = collection.PayerName,
                stall.StallNo,
                FacilityCode = facility.Code,
                bill.BillingYear,
                bill.BillingMonth,
                line.Amount
            })
            .ToListAsync(ct);

        lines.AddRange(canonicalWcf.Select(x => new CollectorCollectionLine(
            x.DocumentNumber,
            x.RecordedAtUtc,
            string.IsNullOrWhiteSpace(x.PayorName) ? "Unidentified payor" : x.PayorName,
            x.StallNo,
            x.FacilityCode,
            $"Water Consumption Fee / WCF · {new DateOnly(x.BillingYear, x.BillingMonth, 1):MMM yyyy}",
            x.Amount,
            null,
            new DateOnly(x.BillingYear, x.BillingMonth, 1),
            x.BusinessDate, IsCanonical: true)));

        var canonicalEcf = await (
            from collection in context.Collections.AsNoTracking()
            join line in context.CollectionLines.AsNoTracking() on collection.Id equals line.CollectionId
            join bill in context.UtilityBills.AsNoTracking() on line.SourceId equals bill.Id
            join stall in context.Stalls.AsNoTracking() on bill.StallId equals stall.Id
            join facility in context.Facilities.AsNoTracking() on stall.FacilityId equals facility.Id
            where collection.CollectorId == collectorId
                && collection.BusinessDate >= businessDateFrom && collection.BusinessDate <= businessDateTo
                && line.SourceKind == CollectionSourceKind.UtilityBill
                && line.SourcePart == CollectionSourcePart.Electricity
                && bill.ElectricitySettlementAuthorityState == SettlementAuthority.Canonical
            select new
            {
                DocumentNumber = collection.ReferenceCode, collection.RecordedAtUtc, collection.BusinessDate, collection.PayerName,
                stall.StallNo, FacilityCode = facility.Code, bill.BillingYear, bill.BillingMonth, line.Amount
            })
            .ToListAsync(ct);

        lines.AddRange(canonicalEcf.Select(x => new CollectorCollectionLine(
            x.DocumentNumber, x.RecordedAtUtc,
            string.IsNullOrWhiteSpace(x.PayerName) ? "Unidentified payor" : x.PayerName,
            x.StallNo, x.FacilityCode,
            $"Electricity Consumption Fee / ECF · {new DateOnly(x.BillingYear, x.BillingMonth, 1):MMM yyyy}",
            x.Amount, null, new DateOnly(x.BillingYear, x.BillingMonth, 1), x.BusinessDate, IsCanonical: true)));

        // Governed operations (Market Fees, Landing/Berthing, Transfer Large Cattle, Vegetable/Fruit) are canonical
        // collections with no facility. They are read from the posted Collection, never rebuilt from a device queue.
        var governed = await (
            from collection in context.Collections.AsNoTracking()
            join line in context.CollectionLines.AsNoTracking() on collection.Id equals line.CollectionId
            join service in context.GovernedServices.AsNoTracking()
                on new { line.MunicipalityId, Id = line.SourceId } equals new { service.MunicipalityId, Id = (Guid?)service.Id }
            join policy in context.RevenueClassificationPolicies.AsNoTracking() on line.RevenueClassificationPolicyId equals policy.Id
            where collection.CollectorId == collectorId
                && collection.BusinessDate >= businessDateFrom && collection.BusinessDate <= businessDateTo
                && line.SourceKind == CollectionSourceKind.GovernedService
            select new
            {
                DocumentNumber = collection.ReferenceCode,
                InstrumentType = (RevenueInstrumentType?)policy.PermittedInstrumentType,
                collection.RecordedAtUtc,
                collection.BusinessDate,
                collection.PayerName,
                service.OperationCode,
                line.CalculationSnapshot,
                line.Amount
            })
            .ToListAsync(ct);

        // Obligation installments and approved penalties this collector took (canonical from birth, no legacy twin).
        var obligations = await (
            from collection in context.Collections.AsNoTracking()
            join line in context.CollectionLines.AsNoTracking() on collection.Id equals line.CollectionId
            join allocation in context.CollectionAllocations.AsNoTracking() on line.Id equals allocation.CollectionLineId
            join period in context.ObligationPeriods.AsNoTracking() on allocation.SourceId equals period.Id
            join account in context.ObligationAccounts.AsNoTracking()
                on new { period.MunicipalityId, Id = period.ObligationAccountId } equals new { account.MunicipalityId, account.Id }
            join policy in context.RevenueClassificationPolicies.AsNoTracking() on line.RevenueClassificationPolicyId equals policy.Id
            where collection.CollectorId == collectorId
                && collection.BusinessDate >= businessDateFrom && collection.BusinessDate <= businessDateTo
                && allocation.SourceKind == CollectionSourceKind.ObligationPeriod
            select new
            {
                DocumentNumber = collection.ReferenceCode, InstrumentType = (RevenueInstrumentType?)policy.PermittedInstrumentType, collection.RecordedAtUtc, collection.BusinessDate,
                collection.PayerName, account.Kind, account.SubjectLabel, period.PeriodStart, allocation.Amount
            })
            .ToListAsync(ct);
        var penalties = await (
            from collection in context.Collections.AsNoTracking()
            join line in context.CollectionLines.AsNoTracking() on collection.Id equals line.CollectionId
            join version in context.PenaltyDefinitions.AsNoTracking()
                on new { line.MunicipalityId, Id = line.SourceId } equals new { version.MunicipalityId, Id = (Guid?)version.Id }
            join policy in context.RevenueClassificationPolicies.AsNoTracking() on line.RevenueClassificationPolicyId equals policy.Id
            where collection.CollectorId == collectorId
                && collection.BusinessDate >= businessDateFrom && collection.BusinessDate <= businessDateTo
                && line.SourceKind == CollectionSourceKind.PenaltyDefinition
            select new
            {
                DocumentNumber = collection.ReferenceCode, InstrumentType = (RevenueInstrumentType?)policy.PermittedInstrumentType, collection.RecordedAtUtc, collection.BusinessDate,
                collection.PayerName, version.Code, version.DisplayName, line.CalculationSnapshot, line.Amount
            })
            .ToListAsync(ct);
        var operations = governed
            .OrderBy(x => x.RecordedAtUtc)
            .Select(x => new CollectorOperationCollection(
                x.DocumentNumber, x.RecordedAtUtc, x.BusinessDate, x.OperationCode,
                Application.Common.Revenue.GovernedServiceCatalog.Find(x.OperationCode)?.Name ?? x.OperationCode,
                x.InstrumentType, x.PayerName,
                Application.Common.Revenue.GovernedServiceWorkflow.ReadReference(x.CalculationSnapshot), x.Amount))
            .Concat(obligations.Select(x => new CollectorOperationCollection(
                x.DocumentNumber, x.RecordedAtUtc, x.BusinessDate, x.Kind.ToString().ToUpperInvariant(),
                Application.Common.Revenue.ObligationCollectionSource.KindLabel(x.Kind), x.InstrumentType, x.PayerName,
                $"{x.SubjectLabel} · {x.PeriodStart:MMM yyyy}", x.Amount)))
            .Concat(penalties.Select(x => new CollectorOperationCollection(
                x.DocumentNumber, x.RecordedAtUtc, x.BusinessDate, x.Code, x.DisplayName, x.InstrumentType, x.PayerName,
                Application.Common.Revenue.PenaltyDefinitionWorkflow.ReadOrigin(x.CalculationSnapshot), x.Amount)))
            .OrderBy(x => x.TakenAtUtc)
            .ToList();

        return new CollectorCollectionsData(
            lines.OrderBy(l => l.TakenAtUtc).ToList(),
            absences.OrderBy(a => a.Day).ToList(),
            officeRecorded,
            officeReceipts,
            utilityBilled,
            utilityCollected,
            utilityOutstanding,
            operations);

        static decimal Collected(PaymentStatus status, decimal charge, decimal partial) => status switch
        {
            PaymentStatus.Paid => charge,
            PaymentStatus.Partial => partial,
            _ => 0m
        };
    }

    /// <summary>The office's own fish fee as of the period, falling back to the ordinance constant.</summary>
    private async Task<decimal> ResolveNpmFishRateAsync(DateOnly asOf, CancellationToken ct)
    {
        var rate = await context.FacilityRates
            .AsNoTracking()
            .Where(r => r.FacilityCode == FacilityCode.NPM
                     && r.RateKey == FeeRateKey.NpmFishPerKilo
                     && r.EffectiveDate <= asOf
                     && !r.IsDeleted)
            .OrderByDescending(r => r.EffectiveDate)
            .Select(r => (decimal?)r.Amount)
            .FirstOrDefaultAsync(ct);

        return rate ?? Domain.Constants.FeeRates.NpmFishFeePerKilo;
    }
}

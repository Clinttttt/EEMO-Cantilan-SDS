using System.Data;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Monitoring;
using EEMOCantilanSDS.Application.Queries.Monitoring.GetContractsMonitoring;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public sealed class ContractsMonitoringQueries(AppDbContext db, ICurrentMunicipalityAccessor municipality) : IContractsMonitoringQueries
{
    public async Task<ContractsMonitoringPageDto> GetAsync(ContractsMonitoringRequest filters, DateOnly today, CancellationToken ct)
    {
        if (municipality.MunicipalityId == Guid.Empty)
            return new(today, new(0, 0, 0, 0, 0, 0, 0, 0, 0), 0, filters.Page, filters.PageSize, []);

        // All statements observe one read snapshot, so concurrent renewal cannot move the row between counts/page.
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var scope = SourceRows(today).Select(ContractMonitoringRules.WithExpiry());
        if (!string.IsNullOrWhiteSpace(filters.Operation))
        {
            var operation = filters.Operation.Trim().ToUpperInvariant();
            scope = scope.Where(r => r.Operation.ToUpper() == operation);
        }
        if (filters.FacilityId is { } facility) scope = scope.Where(r => r.FacilityId == facility);
        if (!string.IsNullOrWhiteSpace(filters.OccupancyBasis))
        {
            if (filters.OccupancyBasis.Trim().Equals("Unknown", StringComparison.OrdinalIgnoreCase)) scope = scope.Where(r => r.OccupancyBasis == null);
            else
            {
                var basis = Enum.Parse<OccupancyArrangement>(filters.OccupancyBasis.Trim(), true);
                scope = scope.Where(r => r.OccupancyBasis == basis);
            }
        }
        if (!string.IsNullOrWhiteSpace(filters.Search))
        {
            var search = filters.Search.Trim().ToLowerInvariant(); // Contains is literal; %, _ are not user-controlled LIKE patterns.
            scope = scope.Where(r => (r.ActualOccupant != null && r.ActualOccupant.ToLower().Contains(search)) ||
                (r.ContractName != null && r.ContractName.ToLower().Contains(search)) ||
                (r.ContractReference != null && r.ContractReference.ToLower().Contains(search)) ||
                r.SpaceNumber.ToLower().Contains(search) || r.OperationDisplayName.ToLower().Contains(search));
        }

        var counts = await scope.Select(ContractMonitoringRules.Status(today)).GroupBy(s => s)
            .Select(g => new { Status = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Status, x => x.Count, ct);
        var seven = await scope.CountAsync(ContractMonitoringRules.InWindow(today, 7), ct);
        var thirty = counts.GetValueOrDefault(ContractMonitoringStatus.ExpiringSoon);
        var summary = new ContractsMonitoringSummaryDto(counts.GetValueOrDefault(ContractMonitoringStatus.Active) + thirty,
            seven, thirty, counts.GetValueOrDefault(ContractMonitoringStatus.Expired), counts.GetValueOrDefault(ContractMonitoringStatus.NoContract),
            counts.GetValueOrDefault(ContractMonitoringStatus.Ended), counts.GetValueOrDefault(ContractMonitoringStatus.Unknown),
            counts.GetValueOrDefault(ContractMonitoringStatus.NotYetEffective), counts.Values.Sum());
        var matching = scope;
        if (!string.IsNullOrWhiteSpace(filters.Status))
            matching = matching.Where(ContractMonitoringRules.HasStatus(today, Enum.Parse<ContractMonitoringStatus>(filters.Status.Trim(), true)));
        if (filters.ExpiryWindow is { } days) matching = matching.Where(ContractMonitoringRules.InWindow(today, days));
        var total = await matching.CountAsync(ct);
        var sort = filters.Sort?.Trim().ToLowerInvariant();
        IOrderedQueryable<ContractMonitoringRecord> sorted = sort switch
        {
            "occupant" => matching.OrderBy(r => r.ActualOccupant == null).ThenBy(r => r.ActualOccupant),
            "operation" => matching.OrderBy(r => r.Operation),
            "space" => matching.OrderBy(r => r.SpaceNumber),
            "expirydesc" => matching.OrderBy(r => r.ExpiryDate == null).ThenByDescending(r => r.ExpiryDate),
            _ => matching.OrderBy(r => r.ExpiryDate == null).ThenBy(r => r.ExpiryDate)
        };
        var rows = await sorted.ThenBy(r => r.Operation).ThenBy(r => r.SpaceNumber).ThenBy(r => r.SourceType).ThenBy(r => r.SourceRecordId)
            .Skip((filters.Page - 1) * filters.PageSize).Take(filters.PageSize).ToListAsync(ct);
        await snapshot.CommitAsync(ct);
        var status = ContractMonitoringRules.Status(today).Compile();
        return new(today, summary, total, filters.Page, filters.PageSize, rows.Select(r => ContractMonitoringRules.ToDto(r, today, status)).ToArray());
    }

    private IQueryable<ContractMonitoringRecord> SourceRows(DateOnly today)
    {
        var tenant = municipality.MunicipalityId;
        // Global filters remain on every source. Composite tenant joins also reject corrupt cross-tenant relationships.
        var facilityRows = from contract in db.Contracts.AsNoTracking()
                           join stall in db.Stalls.AsNoTracking() on new { contract.MunicipalityId, Id = contract.StallId } equals new { stall.MunicipalityId, stall.Id }
                           join facility in db.Facilities.AsNoTracking() on new { stall.MunicipalityId, Id = stall.FacilityId } equals new { facility.MunicipalityId, facility.Id }
                           where contract.MunicipalityId == tenant &&
                               (facility.Archetype == BillingArchetype.DailyStall || facility.Archetype == BillingArchetype.MonthlyRental) &&
                               contract.Id == db.Contracts.Where(c => c.MunicipalityId == tenant && c.StallId == stall.Id)
                                   .OrderByDescending(c => c.EffectivityDate <= today && (c.IsActive && c.EndedOn == null || c.EndedOn >= today))
                                   .ThenByDescending(c => c.IsActive).ThenByDescending(c => c.EffectivityDate).ThenByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
                                   .Select(c => c.Id).FirstOrDefault()
                           select new ContractMonitoringRecord
                           {
                               SourceType = ContractMonitoringSourceType.FacilityContract, SourceRecordId = contract.Id,
                               FacilityId = facility.Id, FacilityCode = facility.Code, StallId = stall.Id, PayorId = contract.PayorId,
                               ObligationKind = null, ContractReference = null, ExpiryDate = null,
                               Operation = facility.Code == FacilityCode.NPM ? "NPM" : facility.Code == FacilityCode.NCC ? "NCC" :
                                   facility.Code == FacilityCode.TCC ? "TCC" : facility.Code == FacilityCode.BBQ ? "BBQ" : facility.Code == FacilityCode.ICE ? "ICE" :
                                   facility.Code == FacilityCode.Custom1 ? "Custom1" : facility.Code == FacilityCode.Custom2 ? "Custom2" :
                                   facility.Code == FacilityCode.Custom3 ? "Custom3" : facility.Code == FacilityCode.Custom4 ? "Custom4" :
                                   facility.Code == FacilityCode.Custom5 ? "Custom5" : facility.Code == FacilityCode.SLH ? "SLH" : facility.Code == FacilityCode.TRM ? "TRM" : "TPM",
                               OperationDisplayName = facility.Name, ActualOccupant = contract.ActualOccupant, SpaceNumber = stall.StallNo,
                               OccupancyBasis = contract.Arrangement, ContractName = contract.Arrangement == OccupancyArrangement.SignedContract ? contract.NameOnContract : null,
                               OccupancyStart = contract.EffectivityDate,
                               EffectivityDate = contract.Arrangement == OccupancyArrangement.SignedContract ? contract.EffectivityDate : null,
                               DurationYears = contract.Arrangement == OccupancyArrangement.SignedContract && contract.DurationYears > 0 ? contract.DurationYears : null,
                               DurationMonths = null,
                               Ended = stall.Status == StallStatus.Closed || contract.EndedOn < today || !contract.IsActive && (contract.EndedOn == null || contract.EndedOn < today),
                               ClosureDate = stall.Status == StallStatus.Closed ? stall.ClosedAt : contract.EndedOn,
                               ApprovedCharge = facility.Archetype == BillingArchetype.MonthlyRental ? contract.ActualMonthlyRental ?? contract.MonthlyRentalRate : null,
                               ChargeFrequency = facility.Archetype == BillingArchetype.MonthlyRental ? ContractMonitoringChargeFrequency.Monthly : ContractMonitoringChargeFrequency.Daily,
                               ContractStatedAmount = contract.Arrangement == OccupancyArrangement.SignedContract ? contract.MonthlyRentalRate : null,
                               AreaSqm = (decimal?)stall.AreaSqm, AreaLocation = stall.AreaNote ??
                                   (stall.AreaLocation == NccAreaLocation.Extension ? "Extension" : stall.AreaLocation == NccAreaLocation.Corner ? "Corner" : stall.AreaLocation == NccAreaLocation.Standard ? "Standard" : null)
                           };

        var obligationRows = from account in db.ObligationAccounts.AsNoTracking()
                             where account.MunicipalityId == tenant && (account.Kind == ObligationKind.KanmanggaySpaceRental || account.Kind == ObligationKind.VegetableFruitSpaceRental)
                             select new ContractMonitoringRecord
                             {
                                 SourceType = ContractMonitoringSourceType.ObligationAccount, SourceRecordId = account.Id,
                                 FacilityId = null, FacilityCode = null, StallId = null, ExpiryDate = null,
                                 ObligationKind = account.Kind, PayorId = account.PayorId,
                                 Operation = account.Kind == ObligationKind.KanmanggaySpaceRental ? "KM" : "VF",
                                 OperationDisplayName = db.TenantOperationSettings.Where(s => s.MunicipalityId == tenant &&
                                     s.OperationCode == (account.Kind == ObligationKind.KanmanggaySpaceRental ? CollectorOperationCodes.KanmanggaySpaceRental : CollectorOperationCodes.VegetableFruitSpaceRental))
                                     .Select(s => s.LocalDisplayName).FirstOrDefault() ?? (account.Kind == ObligationKind.KanmanggaySpaceRental ? "Kanmanggay" : "Vegetable / Fruits"),
                                 ActualOccupant = account.ActualOccupant, SpaceNumber = account.SubjectLabel, OccupancyBasis = account.Arrangement,
                                 ContractName = account.ContractName, ContractReference = account.ContractReference,
                                 OccupancyStart = account.ActiveFrom,
                                 EffectivityDate = account.Arrangement == OccupancyArrangement.SignedContract ? account.ContractEffectiveDate : null,
                                 DurationYears = account.Arrangement == OccupancyArrangement.SignedContract ? account.ContractYears : null,
                                 DurationMonths = account.Arrangement == OccupancyArrangement.SignedContract ? account.ContractMonths : null,
                                 Ended = account.ActiveTo != null && account.ActiveTo < today, ClosureDate = account.ActiveTo,
                                 ApprovedCharge = db.ObligationRates.Where(r => r.MunicipalityId == tenant && r.ObligationAccountId == account.Id && r.EffectiveFrom <= today)
                                     .OrderByDescending(r => r.EffectiveFrom).ThenByDescending(r => r.Id).Select(r => (decimal?)r.Amount).FirstOrDefault(),
                                 ChargeFrequency = ContractMonitoringChargeFrequency.Monthly,
                                 ContractStatedAmount = account.Arrangement == OccupancyArrangement.SignedContract ? account.ContractMonthlyRate : null,
                                 AreaSqm = account.AreaSqm, AreaLocation = account.AreaLocation
                             };
        return facilityRows.Concat(obligationRows);
    }
}

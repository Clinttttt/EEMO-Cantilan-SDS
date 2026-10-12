using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories.Revenue;

/// <summary>
/// The ECF / WCF source register: every verified utility source of the billing month with where its period stands, filtered, counted
/// and paged in the database. A source is a market space of ANY facility whose own service flag for the utility is on (an NPM stall
/// always), never a name or a vendor registration. Money is read as the canonical Collections' own allocations net of corrections
/// (plus the legacy cash recorded on a legacy part, and the opening settlement frozen at a cutover) - the register never invents
/// an assessment for a direct collection, and never adds a prepared amount to revenue.
/// </summary>
public sealed class UtilityRegisterReader(AppDbContext db, ICurrentUserService user, ICurrentMunicipalityAccessor municipality, IClock clock)
    : IUtilityRegisterReader
{
    public const int MaxPage = 100;

    private sealed class Fact
    {
        public Guid StallId { get; set; }
        public Guid? BillId { get; set; }
        public decimal Charge { get; set; }
        public bool Direct { get; set; }
        public SettlementAuthority Authority { get; set; }
        public long Version { get; set; }
        public decimal LegacyPaid { get; set; }
        public decimal Opening { get; set; }
        public decimal Allocated { get; set; }
        public decimal Corrected { get; set; }
        public string FacilityName { get; set; } = "";
        public FacilityCode FacilityCode { get; set; }
        public string StallNo { get; set; } = "";
        public MarketSection? Section { get; set; }
        public string? CustomSection { get; set; }
    }

    private sealed class Derived
    {
        public Fact F { get; set; } = null!;
        public decimal Settled { get; set; }
        public decimal Outstanding { get; set; }
        public bool Prepared { get; set; }
        public string Status { get; set; } = "";
    }

    public async Task<Result<UtilityRegisterPageDto>> GetAsync(UtilityRegisterRequest request, CancellationToken ct = default)
    {
        if (!user.IsAuthenticated || user.Role is not ("SuperAdmin" or "Admin")) return Result<UtilityRegisterPageDto>.Forbidden();
        var tenant = municipality.MunicipalityId;
        if (tenant == Guid.Empty || user.MunicipalityId is { } claimed && claimed != tenant) return Result<UtilityRegisterPageDto>.Forbidden();
        if (request.Year is < 2000 or > 2200 || request.Month is < 1 or > 12 || !Enum.IsDefined(request.Kind))
            return Result<UtilityRegisterPageDto>.Failure("Choose a valid billing month.", ResultStatus.Invalid);
        var take = Math.Clamp(request.Take <= 0 ? 25 : request.Take, 1, MaxPage);
        var skip = Math.Max(0, request.Skip);

        var facts = request.Kind == UtilityKind.Electricity ? ElectricityFacts(tenant, request) : WaterFacts(tenant, request);
        var derived = facts.Select(f => new Derived
        {
            F = f,
            Prepared = !f.Direct && f.Charge > 0m,
            Settled = f.Authority == SettlementAuthority.Canonical ? f.Opening + f.Allocated + f.Corrected : f.LegacyPaid,
            Outstanding = !f.Direct && f.Charge > 0m
                ? (f.Charge - (f.Authority == SettlementAuthority.Canonical ? f.Opening + f.Allocated + f.Corrected : f.LegacyPaid) > 0m
                    ? f.Charge - (f.Authority == SettlementAuthority.Canonical ? f.Opening + f.Allocated + f.Corrected : f.LegacyPaid) : 0m)
                : 0m,
            Status = f.Authority == SettlementAuthority.PendingCutover ? "NeedsReview"
                : !f.Direct && f.Charge > 0m
                    ? ((f.Authority == SettlementAuthority.Canonical ? f.Opening + f.Allocated + f.Corrected : f.LegacyPaid) <= 0m ? "Unpaid"
                        : f.Charge - (f.Authority == SettlementAuthority.Canonical ? f.Opening + f.Allocated + f.Corrected : f.LegacyPaid) > 0m ? "Partial" : "Settled")
                    : f.Direct && (f.Authority == SettlementAuthority.Canonical ? f.Opening + f.Allocated + f.Corrected : f.LegacyPaid) > 0m ? "Direct"
                    : "Unprepared"
        });

        // The header totals describe the facility scope of the period; search and status narrow only the rows below them.
        var scoped = derived;
        if (request.Facility is { } facility) scoped = scoped.Where(d => d.F.FacilityCode == facility);
        scoped = ApplySection(scoped, request.Section);
        var totals = await scoped.GroupBy(_ => 1).Select(g => new
        {
            Assessed = g.Sum(d => d.Prepared ? d.F.Charge : 0m),
            Collected = g.Sum(d => d.Settled),
            Outstanding = g.Sum(d => d.Outstanding),
            Direct = g.Sum(d => d.F.Direct ? d.Settled : 0m),
            Count = g.Count()
        }).SingleOrDefaultAsync(ct);

        var rows = scoped;
        if (!string.IsNullOrWhiteSpace(request.Status) && request.Status != "All") rows = rows.Where(d => d.Status == request.Status);
        var term = (request.Search ?? "").Trim().ToLower();
        if (term.Length > 0)
            rows = rows.Where(d => d.F.StallNo.ToLower().Contains(term) || d.F.FacilityName.ToLower().Contains(term)
                || db.Contracts.Any(c => c.StallId == d.F.StallId && c.MunicipalityId == tenant
                    && ((c.ActualOccupant != null && c.ActualOccupant.ToLower().Contains(term)) || c.NameOnContract.ToLower().Contains(term)
                        || (c.Payor != null && c.Payor.DisplayName.ToLower().Contains(term)))));

        var total = await rows.CountAsync(ct);
        // Facility, space number, then the stall id: a fixed total order, so paging never repeats or drops a source.
        var page = await rows.OrderBy(d => d.F.FacilityName).ThenBy(d => d.F.StallNo).ThenBy(d => d.F.StallId)
            .Skip(skip).Take(take).ToListAsync(ct);

        var ids = page.Select(d => d.F.StallId).ToArray();
        var stalls = await db.Stalls.AsNoTracking().Include(x => x.Facility).Include(x => x.Contracts).ThenInclude(x => x.Payor)
            .Where(x => x.MunicipalityId == tenant && ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var today = clock.PhilippineToday;
        var result = page.Select(d =>
        {
            var stall = stalls[d.F.StallId];
            var contract = stall.OccupancyAnsweringForMonth(request.Year, request.Month, today)?.Contract;
            var payer = contract is null ? null
                : contract.PayorId is not null && contract.Payor?.MunicipalityId == tenant ? contract.Payor.DisplayName
                : string.IsNullOrWhiteSpace(contract.ActualOccupant) ? null : contract.ActualOccupant.Trim();
            var section = d.F.Section is { } marketSection ? stall.Facility!.SectionLabel(marketSection) ?? d.F.CustomSection ?? "" : d.F.CustomSection ?? "";
            string? @lock = d.F.Direct ? "This period is collected directly; an amount cannot be prepared after the first direct collection."
                : d.F.Authority != SettlementAuthority.Legacy ? "This period already has canonical settlement; its amount is frozen."
                : d.F.LegacyPaid > 0m ? "This period has recorded payment; its amount is frozen."
                : null;
            return new UtilityRegisterRowDto(d.F.StallId, d.F.BillId, d.F.StallNo, d.F.FacilityCode, d.F.FacilityName, section, payer,
                d.Prepared ? d.F.Charge : null, d.Settled, d.Outstanding, d.F.Direct ? "Direct" : d.Prepared ? "Prepared" : "None", d.Status,
                d.F.Authority, d.F.Version, @lock is null && contract is not null, contract is null ? "No occupancy answers for this month." : @lock);
        }).ToList();

        var eligible = db.Stalls.AsNoTracking().Where(s => s.MunicipalityId == tenant).Where(UtilityServiceEligibility.SourcePredicate(request.Kind));
        var facilities = await eligible.Select(s => new { s.Facility!.Code, s.Facility.Name }).Distinct().OrderBy(x => x.Name).ToListAsync(ct);
        var sections = await ApplySectionOptions(eligible, request.Facility).ToListAsync(ct);
        var t = totals;
        return Result<UtilityRegisterPageDto>.Success(new(result, total,
            new(t?.Assessed ?? 0m, t?.Collected ?? 0m, t?.Outstanding ?? 0m, t?.Direct ?? 0m, t?.Count ?? 0),
            facilities.Select(f => new UtilityFacilityOptionDto(f.Code, f.Name)).ToList(), sections));
    }

    private static IQueryable<Derived> ApplySection(IQueryable<Derived> q, string? section)
    {
        if (string.IsNullOrWhiteSpace(section)) return q;
        if (Enum.TryParse<MarketSection>(section, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)) return q.Where(d => d.F.Section == parsed);
        return q.Where(d => d.F.CustomSection == section);
    }

    private static IQueryable<string> ApplySectionOptions(IQueryable<Stall> eligible, FacilityCode? facility)
    {
        var scoped = facility is { } code ? eligible.Where(s => s.Facility!.Code == code) : eligible;
        var canonical = scoped.Where(s => s.Section != null).Select(s => s.Section!.Value.ToString());
        var custom = scoped.Where(s => s.CustomSectionName != null).Select(s => s.CustomSectionName!);
        return canonical.Union(custom).OrderBy(x => x);
    }

    private IQueryable<Fact> WaterFacts(Guid tenant, UtilityRegisterRequest r)
    {
        var start = new DateOnly(r.Year, r.Month, 1); var end = start.AddMonths(1).AddDays(-1);
        var bills = db.UtilityBills.Where(b => b.MunicipalityId == tenant && b.BillingYear == r.Year && b.BillingMonth == r.Month);
        var stalls = db.Stalls.Where(s => s.MunicipalityId == tenant).Where(UtilityServiceEligibility.SourcePredicate(UtilityKind.Water));
        return from s in stalls
               join b in bills on s.Id equals b.StallId into joined
               from b in joined.DefaultIfEmpty()
               where b != null || s.Contracts.Any(c => c.EffectivityDate <= end && (c.EndedOn == null || c.EndedOn >= start))
               select new Fact
               {
                   StallId = s.Id, BillId = b == null ? (Guid?)null : b.Id, StallNo = s.StallNo, FacilityName = s.Facility!.Name, FacilityCode = s.Facility.Code,
                   Section = s.Section, CustomSection = s.CustomSectionName,
                   Charge = b == null ? 0m : (b.WaterCurrentReading > b.WaterPreviousReading ? b.WaterCurrentReading - b.WaterPreviousReading : 0m) * b.WaterRatePerCubicMeter,
                   Direct = b != null && b.WaterDirectCollection,
                   Authority = b == null ? SettlementAuthority.Legacy : b.WaterSettlementAuthorityState,
                   Version = b == null ? 0L : b.WaterSourceVersion,
                   LegacyPaid = b == null ? 0m : b.WaterStatus == PaymentStatus.Paid
                       ? (b.WaterCurrentReading > b.WaterPreviousReading ? b.WaterCurrentReading - b.WaterPreviousReading : 0m) * b.WaterRatePerCubicMeter
                       : b.WaterStatus == PaymentStatus.Partial ? b.WaterPartialAmount : 0m,
                   Opening = b == null || b.WaterSettlementCutoverId == null ? 0m
                       : (db.CollectionSettlementCutovers.Where(x => x.Id == b.WaterSettlementCutoverId).Sum(x => (decimal?)x.OpeningLegacySettledAmount) ?? 0m),
                   Allocated = b == null ? 0m : (db.CollectionAllocations.Where(a => a.MunicipalityId == tenant && a.SourceKind == CollectionSourceKind.UtilityBill
                       && a.SourceId == b.Id && a.SourcePart == CollectionSourcePart.Water).Sum(a => (decimal?)a.Amount) ?? 0m),
                   Corrected = b == null ? 0m : (db.CollectionCorrectionAllocations.Where(c => c.MunicipalityId == tenant
                       && db.CollectionAllocations.Any(a => a.Id == c.OriginalAllocationId && a.SourceKind == CollectionSourceKind.UtilityBill
                           && a.SourceId == b.Id && a.SourcePart == CollectionSourcePart.Water)).Sum(c => (decimal?)c.FinancialEffectAmount) ?? 0m)
               };
    }

    private IQueryable<Fact> ElectricityFacts(Guid tenant, UtilityRegisterRequest r)
    {
        var start = new DateOnly(r.Year, r.Month, 1); var end = start.AddMonths(1).AddDays(-1);
        var bills = db.UtilityBills.Where(b => b.MunicipalityId == tenant && b.BillingYear == r.Year && b.BillingMonth == r.Month);
        var stalls = db.Stalls.Where(s => s.MunicipalityId == tenant).Where(UtilityServiceEligibility.SourcePredicate(UtilityKind.Electricity));
        return from s in stalls
               join b in bills on s.Id equals b.StallId into joined
               from b in joined.DefaultIfEmpty()
               where b != null || s.Contracts.Any(c => c.EffectivityDate <= end && (c.EndedOn == null || c.EndedOn >= start))
               select new Fact
               {
                   StallId = s.Id, BillId = b == null ? (Guid?)null : b.Id, StallNo = s.StallNo, FacilityName = s.Facility!.Name, FacilityCode = s.Facility.Code,
                   Section = s.Section, CustomSection = s.CustomSectionName,
                   Charge = b == null ? 0m : (b.ElecCurrentReading > b.ElecPreviousReading ? b.ElecCurrentReading - b.ElecPreviousReading : 0m) * b.ElecRatePerKwh,
                   Direct = b != null && b.ElectricityDirectCollection,
                   Authority = b == null ? SettlementAuthority.Legacy : b.ElectricitySettlementAuthorityState,
                   Version = b == null ? 0L : b.ElectricitySourceVersion,
                   LegacyPaid = b == null ? 0m : b.ElecStatus == PaymentStatus.Paid
                       ? (b.ElecCurrentReading > b.ElecPreviousReading ? b.ElecCurrentReading - b.ElecPreviousReading : 0m) * b.ElecRatePerKwh
                       : b.ElecStatus == PaymentStatus.Partial ? b.ElecPartialAmount : 0m,
                   Opening = b == null || b.ElectricitySettlementCutoverId == null ? 0m
                       : (db.CollectionSettlementCutovers.Where(x => x.Id == b.ElectricitySettlementCutoverId).Sum(x => (decimal?)x.OpeningLegacySettledAmount) ?? 0m),
                   Allocated = b == null ? 0m : (db.CollectionAllocations.Where(a => a.MunicipalityId == tenant && a.SourceKind == CollectionSourceKind.UtilityBill
                       && a.SourceId == b.Id && a.SourcePart == CollectionSourcePart.Electricity).Sum(a => (decimal?)a.Amount) ?? 0m),
                   Corrected = b == null ? 0m : (db.CollectionCorrectionAllocations.Where(c => c.MunicipalityId == tenant
                       && db.CollectionAllocations.Any(a => a.Id == c.OriginalAllocationId && a.SourceKind == CollectionSourceKind.UtilityBill
                           && a.SourceId == b.Id && a.SourcePart == CollectionSourcePart.Electricity)).Sum(c => (decimal?)c.FinancialEffectAmount) ?? 0m)
               };
    }
}

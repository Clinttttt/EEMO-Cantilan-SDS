using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Domain.Constants;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>Everything the Composer needs to allocate to one obligation period, resolved on the server.</summary>
public sealed record ObligationSourceFacts(
    ObligationAccount Account,
    ObligationPeriod Period,
    RevenueClassification Classification,
    RevenueClassificationPolicy Policy,
    ObligationQuoteDto Quote,
    string SnapshotJson);

/// <summary>
/// The canonical source for the specialized obligation accounts (Fish/Meat Vendor Fee, Kanmanggay, event lot rental).
/// An account's assessed amount is frozen on its period; what has been collected is never stored but read from the
/// canonical allocations (less corrections), so no second balance authority exists. Assessment happens only when a
/// period is first allocated to (or explicitly by the Head), never as a side effect of reading.
/// </summary>
public sealed class ObligationCollectionSource(IAppDbContext db)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public const int LookbackMonths = 60;

    public static string KindLabel(ObligationKind kind) => kind switch
    {
        ObligationKind.FishMeatVendorFee => "Fish/Meat Vendor Fee",
        ObligationKind.KanmanggaySpaceRental => "Kanmanggay Space Rental",
        ObligationKind.FiestaArawLotRental => "Fiesta/Araw Lot Rental",
        _ => kind.ToString()
    };

    /// <summary>The quotes for every period of the accounts up to the business date, oldest first, unpaid or not.</summary>
    public async Task<IReadOnlyList<ObligationQuoteDto>> GetQuotesAsync(
        Guid tenantId, IReadOnlyList<ObligationAccount> accounts, DateOnly businessDate, CancellationToken ct)
    {
        if (accounts.Count == 0) return [];
        var accountIds = accounts.Select(x => x.Id).ToArray();
        var rates = (await db.ObligationRates.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && accountIds.Contains(x.ObligationAccountId)).ToListAsync(ct))
            .ToLookup(x => x.ObligationAccountId);
        var periods = (await db.ObligationPeriods.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && accountIds.Contains(x.ObligationAccountId)).ToListAsync(ct))
            .ToDictionary(x => (x.ObligationAccountId, x.PeriodStart));
        var settled = await SettledByPeriodAsync(tenantId, periods.Values.Select(x => x.Id).ToArray(), ct);
        var payors = await PayorNamesAsync(tenantId, accounts.Select(x => x.PayorId).Distinct().ToArray(), ct);

        var quotes = new List<ObligationQuoteDto>();
        foreach (var account in accounts)
        {
            foreach (var start in account.PeriodStarts(businessDate))
            {
                if (start < businessDate.AddMonths(-LookbackMonths)) continue;
                periods.TryGetValue((account.Id, start), out var period);
                if (account.Kind == ObligationKind.FishMeatVendorFee && FishMeatVendorFeeRules.UsesDirectCollection(businessDate) && period is null) continue;
                var rate = period is null ? ObligationRate.Resolve(rates[account.Id], start) : null;
                if (period is null && rate is null) continue;   // no approved amount in force: not billable, never invented
                var assessed = period?.AssessedAmount ?? rate!.Amount;
                var paid = period is null ? 0m : settled.GetValueOrDefault(period.Id);
                var outstanding = Math.Max(0m, assessed - paid);
                quotes.Add(new ObligationQuoteDto(
                    account.Id, period?.Id, account.Kind, KindLabel(account.Kind), account.SubjectLabel, start,
                    assessed, paid, outstanding, account.PayorId, payors.GetValueOrDefault(account.PayorId),
                    period?.ObligationRateId ?? rate!.Id, outstanding > 0m && !(account.Kind == ObligationKind.FishMeatVendorFee && FishMeatVendorFeeRules.UsesDirectCollection(businessDate))));
            }
        }
        return quotes.OrderBy(x => x.PeriodStart).ThenBy(x => x.SubjectLabel, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Loads (and, when asked, assesses) one period. Returns null when the account or period does not exist for this
    /// tenant, or no approved amount is in force for it. A missing period is only created when <paramref name="assess"/>.
    /// </summary>
    public async Task<ObligationSourceFacts?> LoadAsync(
        Guid tenantId, Guid accountId, DateOnly periodStart, DateOnly businessDate, bool tracked, bool assess,
        string actor, CancellationToken ct)
    {
        IQueryable<ObligationAccount> accounts = db.ObligationAccounts.Where(x => x.MunicipalityId == tenantId && x.Id == accountId);
        var account = await (tracked ? accounts : accounts.AsNoTracking()).SingleOrDefaultAsync(ct);
        if (account is null || !account.PeriodStarts(businessDate).Contains(periodStart)) return null;
        if (assess && account.Kind == ObligationKind.FishMeatVendorFee && FishMeatVendorFeeRules.UsesDirectCollection(businessDate)) return null;

        IQueryable<ObligationPeriod> periods = db.ObligationPeriods.Where(x =>
            x.MunicipalityId == tenantId && x.ObligationAccountId == accountId && x.PeriodStart == periodStart);
        var period = await (tracked ? periods : periods.AsNoTracking()).SingleOrDefaultAsync(ct);
        // A period assessed earlier in this same unit of work is not yet in the database; never assess it twice.
        period ??= db.ObligationPeriods.Local.FirstOrDefault(x =>
            x.MunicipalityId == tenantId && x.ObligationAccountId == accountId && x.PeriodStart == periodStart);
        if (period is null)
        {
            if (!assess) return null;
            var rates = await db.ObligationRates.AsNoTracking()
                .Where(x => x.MunicipalityId == tenantId && x.ObligationAccountId == accountId).ToListAsync(ct);
            var rate = ObligationRate.Resolve(rates, periodStart);
            if (rate is null) return null;
            period = ObligationPeriod.Assess(account, rate, periodStart, actor);
            db.ObligationPeriods.Add(period);
        }
        return await BuildFactsAsync(tenantId, account, period, businessDate, ct);
    }

    public async Task<ObligationSourceFacts?> LoadByPeriodIdAsync(
        Guid tenantId, Guid periodId, DateOnly businessDate, bool tracked, CancellationToken ct)
    {
        IQueryable<ObligationPeriod> periods = db.ObligationPeriods.Where(x => x.MunicipalityId == tenantId && x.Id == periodId);
        var period = await (tracked ? periods : periods.AsNoTracking()).SingleOrDefaultAsync(ct);
        if (period is null) return null;
        IQueryable<ObligationAccount> accounts = db.ObligationAccounts.Where(x =>
            x.MunicipalityId == tenantId && x.Id == period.ObligationAccountId);
        var account = await (tracked ? accounts : accounts.AsNoTracking()).SingleAsync(ct);
        return await BuildFactsAsync(tenantId, account, period, businessDate, ct);
    }

    private async Task<ObligationSourceFacts?> BuildFactsAsync(
        Guid tenantId, ObligationAccount account, ObligationPeriod period, DateOnly businessDate, CancellationToken ct)
    {
        var code = ObligationAccount.ClassificationCodeFor(account.Kind);
        var classification = await db.RevenueClassifications.AsNoTracking().SingleOrDefaultAsync(x =>
            x.MunicipalityId == tenantId && x.SemanticCode == code && x.IsActive, ct);
        var policy = classification is null ? null : await db.RevenueClassificationPolicies.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && x.RevenueClassificationId == classification.Id
                && x.BusinessContext == RevenuePolicyContext.Default && x.EffectiveDate <= businessDate)
            .OrderByDescending(x => x.EffectiveDate).FirstOrDefaultAsync(ct);
        if (classification is null || policy?.PermittedInstrumentType != RevenueInstrumentType.OfficialReceipt)
            throw new InvalidOperationException($"{KindLabel(account.Kind)} has no effective Official Receipt policy for this tenant.");

        var settled = (await SettledByPeriodAsync(tenantId, [period.Id], ct)).GetValueOrDefault(period.Id);
        var outstanding = Math.Max(0m, period.AssessedAmount - settled);
        var payorName = (await PayorNamesAsync(tenantId, [account.PayorId], ct)).GetValueOrDefault(account.PayorId);
        var quote = new ObligationQuoteDto(
            account.Id, period.Id, account.Kind, KindLabel(account.Kind), account.SubjectLabel, period.PeriodStart,
            period.AssessedAmount, settled, outstanding, account.PayorId, payorName, period.ObligationRateId,
            outstanding > 0m && !(account.Kind == ObligationKind.FishMeatVendorFee && FishMeatVendorFeeRules.UsesDirectCollection(businessDate)));

        var snapshot = JsonSerializer.Serialize(new ObligationSnapshot(
            1, tenantId, (int)account.Kind, account.Id, period.Id, period.PeriodStart.Year, period.PeriodStart.Month,
            period.PeriodStart, period.AssessedAmount, period.ObligationRateId, account.SubjectLabel,
            $"{KindLabel(account.Kind)} · {account.SubjectLabel}", (int)SettlementAuthority.Canonical,
            period.SettlementVersion, settled, outstanding, account.PayorId, payorName, code, policy.Id), JsonOptions);
        return new ObligationSourceFacts(account, period, classification, policy, quote, snapshot);
    }

    /// <summary>Collected per period: canonical allocations plus their corrections. The one balance authority.</summary>
    private async Task<Dictionary<Guid, decimal>> SettledByPeriodAsync(Guid tenantId, Guid[] periodIds, CancellationToken ct)
    {
        if (periodIds.Length == 0) return [];
        var allocations = await db.CollectionAllocations.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && x.SourceKind == CollectionSourceKind.ObligationPeriod
                && periodIds.Contains(x.SourceId))
            .Select(x => new { x.Id, x.SourceId, x.Amount }).ToListAsync(ct);
        var allocationIds = allocations.Select(x => x.Id).ToArray();
        var corrections = allocationIds.Length == 0
            ? []
            : await db.CollectionCorrectionAllocations.AsNoTracking()
                .Where(x => x.MunicipalityId == tenantId && allocationIds.Contains(x.OriginalAllocationId))
                .Select(x => new { x.OriginalAllocationId, x.FinancialEffectAmount }).ToListAsync(ct);
        var correctionByAllocation = corrections.GroupBy(x => x.OriginalAllocationId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.FinancialEffectAmount));
        return allocations.GroupBy(x => x.SourceId).ToDictionary(g => g.Key,
            g => g.Sum(x => x.Amount + correctionByAllocation.GetValueOrDefault(x.Id)));
    }

    private async Task<Dictionary<Guid, string>> PayorNamesAsync(Guid tenantId, Guid[] payorIds, CancellationToken ct) =>
        await db.Payors.AsNoTracking().Where(x => x.MunicipalityId == tenantId && payorIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);

    private sealed record ObligationSnapshot(
        int SchemaVersion, Guid TenantId, int Kind, Guid AccountId, Guid PeriodId, int BillingYear, int BillingMonth,
        DateOnly PeriodStart, decimal AssessedAmount, Guid RateId, string SubjectLabel, string FacilityName,
        int SettlementAuthority, long SettlementVersion, decimal Settled, decimal Outstanding, Guid PayorId,
        string? PayerName, string ClassificationCode, Guid PolicyId);
}

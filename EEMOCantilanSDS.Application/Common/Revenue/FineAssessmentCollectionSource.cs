using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

public sealed record FineSettlementFacts(FineAssessment Assessment, RevenueClassification Classification,
    RevenueClassificationPolicy Policy, FineSettlementQuoteDto Quote, string Snapshot);

/// <summary>One canonical fine balance: original principal less attributable allocations and correction effects.</summary>
public sealed class FineAssessmentCollectionSource(IAppDbContext db)
{
    /// <summary>A fine that can be assessed must be one the office can collect. Fines only ever take an Official Receipt (LoadAsync refuses anything else), so writing that down chooses nothing for the office (IA-078). A source the office retired stays retired, and a policy it already has is left alone.</summary>
    public static async Task EnsureRevenueSourceAsync(IAppDbContext db, Guid tenant, DateOnly today, string by, CancellationToken ct = default)
    {
        var source = db.RevenueClassifications.Local.FirstOrDefault(c => c.MunicipalityId == tenant && c.SemanticCode == RevenueClassificationCodes.PenaltiesAndFines)
            ?? await db.RevenueClassifications.AsNoTracking().FirstOrDefaultAsync(c => c.MunicipalityId == tenant && c.SemanticCode == RevenueClassificationCodes.PenaltiesAndFines, ct);
        if (source is null)
        {
            source = RevenueClassification.Create(RevenueClassificationCodes.PenaltiesAndFines, tenant, by);
            db.RevenueClassifications.Add(source);
        }
        else if (!source.IsActive)
            return;

        var sourceId = source.Id;
        if (db.RevenueClassificationPolicies.Local.Any(p => p.RevenueClassificationId == sourceId)
            || await db.RevenueClassificationPolicies.AsNoTracking().AnyAsync(p => p.MunicipalityId == tenant && p.RevenueClassificationId == sourceId, ct))
            return;
        db.RevenueClassificationPolicies.Add(RevenueClassificationPolicy.Create(sourceId, today, "Fines", RevenueInstrumentType.OfficialReceipt, tenant, createdBy: by));
    }

    public async Task<Dictionary<Guid, decimal>> SettledAsync(Guid tenant, Guid[] ids, CancellationToken ct = default)
    {
        var allocations = await db.CollectionAllocations.AsNoTracking().Where(a => a.MunicipalityId == tenant &&
            a.SourceKind == CollectionSourceKind.FineAssessment && ids.Contains(a.SourceId))
            .Select(a => new {a.Id, a.SourceId, a.Amount}).ToListAsync(ct);
        var allocationIds = allocations.Select(a => a.Id).ToArray();
        var effects = await db.CollectionCorrectionAllocations.AsNoTracking().Where(a => a.MunicipalityId == tenant &&
            allocationIds.Contains(a.OriginalAllocationId)).Select(a => new {a.OriginalAllocationId, a.FinancialEffectAmount}).ToListAsync(ct);
        var corrections = effects.GroupBy(a => a.OriginalAllocationId).ToDictionary(g => g.Key, g => g.Sum(a => a.FinancialEffectAmount));
        return allocations.GroupBy(a => a.SourceId).ToDictionary(g => g.Key, g => g.Sum(a => a.Amount + corrections.GetValueOrDefault(a.Id)));
    }
    public async Task<FineSettlementFacts?> LoadAsync(Guid tenant, Guid id, DateOnly date, bool tracked, CancellationToken ct = default)
    {
        var query = db.FineAssessments.Where(a => a.MunicipalityId == tenant && a.Id == id);
        var a = await (tracked ? query : query.AsNoTracking()).SingleOrDefaultAsync(ct);
        if (a is null) return null;
        var authority = await db.FineAssessmentPolicies.AsNoTracking().SingleOrDefaultAsync(p => p.MunicipalityId == tenant &&
            p.Id == a.PolicyId && p.PenaltyVersionId == a.PenaltyVersionId && p.AccountKind == a.AccountKind, ct);
        if (authority is null || string.IsNullOrWhiteSpace(authority.GoverningReference) || string.IsNullOrWhiteSpace(a.AuthoritySnapshot))
            throw new InvalidOperationException("The assessment has no verified governing evidence.");
        // Frozen assessment evidence governs liability; closure or definition retirement does not invalidate it.
        var classification = await db.RevenueClassifications.AsNoTracking().SingleOrDefaultAsync(c => c.MunicipalityId == tenant &&
            c.SemanticCode == RevenueClassificationCodes.PenaltiesAndFines && c.IsActive, ct);
        if (classification is null) throw new InvalidOperationException("Fines/Penalties classification is unavailable.");
        var policy = await db.RevenueClassificationPolicies.AsNoTracking().Where(p => p.MunicipalityId == tenant &&
            p.RevenueClassificationId == classification.Id && p.BusinessContext == RevenuePolicyContext.Default && p.EffectiveDate <= date)
            .OrderByDescending(p => p.EffectiveDate).FirstOrDefaultAsync(ct);
        if (policy?.PermittedInstrumentType != RevenueInstrumentType.OfficialReceipt)
            throw new InvalidOperationException("Fines/Penalties requires an approved Official Receipt policy.");
        var settled = (await SettledAsync(tenant, [id], ct)).GetValueOrDefault(id);
        if (settled < 0m || settled > a.AmountDue) throw new InvalidOperationException("Fine settlement evidence does not reconcile.");
        var remaining = a.AmountDue - settled;
        var snapshot = JsonSerializer.Serialize(new {SchemaVersion = 1, TenantId = tenant, AssessmentId = id, a.AccountKind,
            a.ContractId, a.ObligationAccountId, a.PolicyId, a.PenaltyVersionId, a.AuthoritySnapshot, a.Version,
            a.Occupant, a.AssessedAmount, a.AdjustedAmount, origin = $"{a.SourceName} · {a.UnitReference}", SettlementVersion = a.Version,
            Settled = settled, Remaining = remaining, BusinessDate = date, RevenuePolicyId = policy.Id});
        var token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot)));
        return new(a, classification, policy, new(id, new(a.AccountKind, a.ContractId ?? a.ObligationAccountId!.Value),
            a.Occupant, a.SourceName, a.UnitReference, a.AmountDue, settled, remaining,
            a.AmountDue == 0 ? "Waived" : remaining == 0 ? "Settled" : settled > 0 ? "Partial" : "Outstanding", a.Version, token,
            RevenueInstrumentType.OfficialReceipt, remaining > 0, remaining > 0 ? null : a.AmountDue == 0 ? "The Head waived this penalty." : "The fine is already settled."), snapshot);
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// Assesses overdue monthly rent as ordinary fine assessments, automatically, once the Head has set a rate (IA-072). It only states a liability: no cash, no
/// OR, no change to the rent month. Each charge has one identity, so running again creates nothing new, and a charge the Head waived does not come back.
/// </summary>
// shortcut: monthly-billed spaces only, because a day-billed market has no single due date to be late against. Extend when an office rules how a due date applies to a day's fee.
public sealed class LatePenaltyAssessor(IAppDbContext db, IFacilityReportsRepository reports, BillingRules billing, ICurrentMunicipalityAccessor municipality, IClock clock)
{
    private Guid Tenant => municipality.MunicipalityId;

    private static bool IsMonthlyBilled(Facility facility) => facility.Code is FacilityCode.NCC or FacilityCode.TCC or FacilityCode.BBQ or FacilityCode.ICE
        || facility.Code == FacilityCode.NPM && facility.IsMonthlyRentMarket;

    /// <summary>The spaces to look at today, or none when the rules in force today charge no penalty, so a Head turning it off stops new charges at once.</summary>
    public async Task<IReadOnlyList<Guid>> StallsToReviewAsync(CancellationToken ct = default)
    {
        var versions = await VersionsAsync(ct);
        if (BillingPolicyVersion.InForce(versions, clock.PhilippineToday)?.Rules.ChargesPenalty != true) return [];
        var stalls = await db.Stalls.AsNoTracking().Include(s => s.Facility)
            .Where(s => s.MunicipalityId == Tenant && s.Status == StallStatus.Active).ToListAsync(ct);
        return stalls.Where(s => s.Facility is not null && IsMonthlyBilled(s.Facility)).Select(s => s.Id).ToList();
    }

    /// <summary>Assesses what this space has newly reached. Returns how many charges were created.</summary>
    public async Task<int> AssessStallAsync(Guid stallId, CancellationToken ct = default)
    {
        var today = clock.PhilippineToday;
        var versions = await VersionsAsync(ct);
        var inForce = BillingPolicyVersion.InForce(versions, today);
        if (inForce?.Rules.ChargesPenalty != true) return 0;
        var stall = await db.Stalls.AsNoTracking().Include(s => s.Facility).Include(s => s.Contracts)
            .SingleOrDefaultAsync(s => s.MunicipalityId == Tenant && s.Id == stallId, ct);
        if (stall?.Facility is null || !IsMonthlyBilled(stall.Facility)) return 0;
        var statement = await reports.GetStallStatementAsync(stallId, ct);
        if (statement is null) return 0;

        var open = statement.UnpaidMonths.Select(m => (m.Year, m.Month, m.Unpaid)).ToList();
        if (statement.CurrentBalance > 0m) open.Add((today.Year, today.Month, statement.CurrentBalance));
        var created = 0;
        if (open.Count > 0)
        {
            // A month held for the office cannot be paid right now, so it must not grow a penalty while it waits.
            var paused = await billing.PausedMonthsAsync(stallId, ct);
            foreach (var (year, month, unpaid) in open.Where(m => inForce.Rules.Counts(m.Year, m.Month) && !paused.Contains((m.Year, m.Month))))
            {
                var governing = BillingPolicyVersion.GoverningMonth(versions, year, month);
                if (governing is null || !governing.Rules.ChargesPenalty) continue;
                var charges = governing.Rules.ChargesReached(year, month, today);
                if (charges == 0) continue;
                var contract = stall.OccupancyAnsweringForMonth(year, month, today)?.Contract;
                if (contract is null) continue;
                created += await AssessMonthAsync(stall, contract, governing, year, month, unpaid, charges, ct);
            }
        }
        if (created > 0) await db.SaveChangesAsync(ct);
        return created;
    }

    private async Task<int> AssessMonthAsync(Stall stall, Contract contract, BillingPolicyVersion governing, int year, int month, decimal unpaid, int charges, CancellationToken ct)
    {
        var start = new DateOnly(year, month, 1);
        var existing = await db.FineAssessments.AsNoTracking()
            .Where(a => a.MunicipalityId == Tenant && a.ContractId == contract.Id && a.PenaltyCode == FineAssessment.LatePaymentCode && a.RelatedPeriodStart == start)
            .Select(a => new { a.IncidentReference, a.AssessedAmount }).ToListAsync(ct);
        var assessed = existing.Sum(a => a.AssessedAmount);
        var rules = governing.Rules;
        var firstLate = rules.FirstLateDay(year, month);
        var created = 0;
        for (var charge = 1; charge <= charges; charge++)
        {
            var incident = $"{year:0000}-{month:00}-{charge}";
            if (existing.Any(a => a.IncidentReference == incident)) continue;
            var amount = rules.PenaltyFor(unpaid);
            if (rules.PenaltyMonthCap is { } cap) amount = Math.Min(amount, cap - assessed);
            if (amount <= 0m) break;
            var (policy, definition) = await AuthorityAsync(stall.Facility!.Code, governing, ct);
            if (policy is null || definition is null) return created;
            var label = start.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
            var evidence = JsonSerializer.Serialize(new { BillingPolicyVersion = governing.Id, governing.GoverningReference, rules.PenaltyBasis, rules.PenaltyValue, rules.PenaltyMonthCap,
                rules.PenaltyRepeatsMonthly, DueDate = rules.DueDate(year, month), rules.GraceDays, Period = label, UnpaidRent = unpaid, Charge = charge, Automatic = true });
            var identity = $"{Tenant:N}|{contract.Id:N}|{FineAssessment.LatePaymentCode}|{incident}";
            var fine = FineAssessment.Create(Tenant, FineAccountKind.FacilityContract, contract.Id, policy, definition, contract.ActualOccupant, stall.StallNo, stall.Facility.Name,
                firstLate.AddMonths(charge - 1), clock.PhilippineToday, incident, $"Late payment on {label} rent.", amount, new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(identity))[..16]),
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{identity}|{amount.ToString("0.00", CultureInfo.InvariantCulture)}"))), governing.ActorId, clock.UtcNow, evidence,
                FineAffectedPeriodKind.FacilityMonth, start, start.AddMonths(1).AddDays(-1), periodLabel: label);
            db.FineAssessments.Add(fine);
            db.FineAssessmentEvents.Add(FineAssessmentEvent.Created(fine));
            assessed += amount;
            created++;
        }
        return created;
    }

    /// <summary>The Head's rate is the authority, so the definition and facility policy a fine must cite are written from it. A definition the Head retired stops new charges.</summary>
    private async Task<(FineAssessmentPolicy? Policy, PenaltyDefinition? Definition)> AuthorityAsync(FacilityCode facility, BillingPolicyVersion governing, CancellationToken ct)
    {
        var today = clock.PhilippineToday;
        var added = db.PenaltyDefinitions.Local.FirstOrDefault(d => d.MunicipalityId == Tenant && d.Code == FineAssessment.LatePaymentCode);
        var definition = added ?? PenaltyDefinition.Resolve(await db.PenaltyDefinitions.AsNoTracking()
            .Where(d => d.MunicipalityId == Tenant && d.Code == FineAssessment.LatePaymentCode).ToListAsync(ct), FineAssessment.LatePaymentCode, today);
        if (definition is null)
        {
            definition = PenaltyDefinition.Create(Tenant, FineAssessment.LatePaymentCode, today, "Late payment penalty", "Monthly rent past its due date",
                GovernedServiceBasis.DirectApprovedAmount, null, null, true, "Billing policy", clock.UtcNow);
            db.PenaltyDefinitions.Add(definition);
        }
        else if (!definition.IsActive)
            return (null, null);

        var reference = governing.GoverningReference!;
        var definitionId = definition.Id;
        var policy = db.FineAssessmentPolicies.Local.FirstOrDefault(p => p.PenaltyVersionId == definitionId && p.Facility == facility && p.GoverningReference == reference)
            ?? await db.FineAssessmentPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.MunicipalityId == Tenant && p.PenaltyVersionId == definitionId
                && p.AccountKind == FineAccountKind.FacilityContract && p.Facility == facility && p.GoverningReference == reference, ct);
        if (policy is null)
        {
            policy = FineAssessmentPolicy.Create(Tenant, definitionId, FineAccountKind.FacilityContract, facility, reference, FinePenaltyDateBasis.AssessmentDate,
                uncapped: true, closed: false, governing.ActorId, clock.UtcNow);
            db.FineAssessmentPolicies.Add(policy);
        }
        await FineAssessmentCollectionSource.EnsureRevenueSourceAsync(db, Tenant, today, "Billing policy", ct);
        return (policy, definition);
    }

    private async Task<List<BillingPolicyVersion>> VersionsAsync(CancellationToken ct) =>
        await db.BillingPolicyVersions.AsNoTracking().Where(v => v.MunicipalityId == Tenant).ToListAsync(ct);
}

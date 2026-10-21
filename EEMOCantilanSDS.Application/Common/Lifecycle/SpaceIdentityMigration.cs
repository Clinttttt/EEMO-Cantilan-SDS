using System.Security.Cryptography;
using System.Text;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Lifecycle;

public sealed record PreflightFinding(string Code, string Description, IReadOnlyList<Guid> AccountIds, bool BlocksBackfill, bool HasFinancialEvidence);

public sealed record SpaceIdentityPreflightReport(
    Guid TenantId,
    int MonthlyAccounts,
    int LinkedAccounts,
    int UnlinkedAccounts,
    /// <summary>Unlinked accounts that are alone on their normalized number: the only ones a backfill will link.</summary>
    int LinkableAccounts,
    IReadOnlyList<PreflightFinding> Findings)
{
    public bool IsClean => Findings.All(f => !f.BlocksBackfill);
}

/// <summary>
/// READ-ONLY check of one municipality's monthly space accounts before any physical-space identity is created. It writes nothing. It states what
/// a backfill would link and, above all, what it must NOT: accounts that share a normalized number are reported, never merged into a chain,
/// because equal numbers or names do not prove a succession (IA-009, IA-068).
/// </summary>
public sealed class SpaceIdentityPreflight(IAppDbContext db)
{
    public async Task<SpaceIdentityPreflightReport> RunAsync(Guid tenantId, CancellationToken ct = default)
    {
        var accounts = await db.ObligationAccounts.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && (x.Kind == ObligationKind.KanmanggaySpaceRental || x.Kind == ObligationKind.VegetableFruitSpaceRental)).ToListAsync(ct);
        var ids = accounts.Select(x => x.Id).ToArray();
        // Financial evidence per account: assessed periods. A group with money behind it needs a human, not a rule.
        var withPeriods = (await db.ObligationPeriods.AsNoTracking().Where(x => x.MunicipalityId == tenantId && ids.Contains(x.ObligationAccountId))
            .Select(x => x.ObligationAccountId).Distinct().ToListAsync(ct)).ToHashSet();
        var lastPeriod = (await db.ObligationPeriods.AsNoTracking().Where(x => x.MunicipalityId == tenantId && ids.Contains(x.ObligationAccountId))
            .GroupBy(x => x.ObligationAccountId).Select(g => new { Id = g.Key, Last = g.Max(p => p.PeriodStart) }).ToListAsync(ct)).ToDictionary(x => x.Id, x => x.Last);
        var registrations = await db.SpaceRegistrations.AsNoTracking().Where(x => x.MunicipalityId == tenantId).ToDictionaryAsync(x => x.Id, ct);

        var findings = new List<PreflightFinding>();
        bool Evidence(IEnumerable<Guid> accountIds) => accountIds.Any(withPeriods.Contains);

        // Number groups, only among accounts that are not yet identified.
        var unlinked = accounts.Where(x => x.SpaceRegistrationId is null).ToList();
        var groups = unlinked.GroupBy(x => (x.Kind, Key: SpaceRegistration.Normalize(x.SubjectLabel))).ToList();
        foreach (var g in groups.Where(g => g.Count() > 1))
        {
            var members = g.OrderBy(x => x.ActiveFrom).ToList();
            var overlaps = false;
            for (var i = 0; i < members.Count && !overlaps; i++)
                for (var j = i + 1; j < members.Count && !overlaps; j++)
                    overlaps = Overlap(members[i], members[j]);
            findings.Add(new("DuplicateNormalizedNumber",
                $"{g.Count()} {g.Key.Kind} accounts share the number \"{g.Key.Key}\". They are NOT linked: a successor chain is never guessed.",
                members.Select(x => x.Id).ToList(), BlocksBackfill: true, Evidence(members.Select(x => x.Id))));
            if (overlaps)
                findings.Add(new("OverlappingIntervals", $"Accounts on number \"{g.Key.Key}\" have overlapping active intervals.", members.Select(x => x.Id).ToList(), true, Evidence(members.Select(x => x.Id))));
        }

        foreach (var a in accounts)
        {
            if (string.IsNullOrWhiteSpace(a.SubjectLabel) || a.SubjectLabel.Trim().Length > 120)
                findings.Add(new("InvalidNumber", "The space number is empty or too long to register.", [a.Id], true, Evidence([a.Id])));
            if (a.ActiveTo is { } to && lastPeriod.TryGetValue(a.Id, out var last) && last > to)
                findings.Add(new("PeriodAfterClosure", $"An assessed period ({last:MMM yyyy}) is later than the closure date ({to:MMM d, yyyy}).", [a.Id], true, true));
            if (a.Kind == ObligationKind.VegetableFruitSpaceRental && !long.TryParse(a.SubjectLabel.Trim(), out _))
                findings.Add(new("NonNumericServerNumber", "A Vegetable/Fruit number is server-assigned and normally a whole number; this one is not.", [a.Id], false, Evidence([a.Id])));
            if (a.SpaceRegistrationId is { } rid && (!registrations.TryGetValue(rid, out var reg) || reg.Kind != a.Kind || reg.NormalizedNumber != SpaceRegistration.Normalize(a.SubjectLabel)))
                findings.Add(new("RegistrationMismatch", "The account is linked to a registration with a different number or source.", [a.Id], true, Evidence([a.Id])));
        }
        // Events do not have a lifecycle identity of their own to reconcile; only reported so the count is explicit.
        // Linkable = alone on its number AND named by no blocking finding (an invalid number, a period after closure, ...).
        var blocked = findings.Where(f => f.BlocksBackfill).SelectMany(f => f.AccountIds).ToHashSet();
        var linkable = groups.Count(g => g.Count() == 1 && !blocked.Contains(g.Single().Id));
        return new(tenantId, accounts.Count, accounts.Count(x => x.SpaceRegistrationId is not null), unlinked.Count, linkable, findings);
    }

    private static bool Overlap(ObligationAccount a, ObligationAccount b) =>
        a.ActiveFrom <= (b.ActiveTo ?? DateOnly.MaxValue) && b.ActiveFrom <= (a.ActiveTo ?? DateOnly.MaxValue);
}

public sealed record BackfillResult(int Linked, int SkippedAmbiguous, SpaceIdentityPreflightReport Before);

/// <summary>
/// Gives each UNAMBIGUOUS monthly space account its physical-space identity. It is a guarded, explicit operation, never run by a migration or at
/// startup: an account alone on its normalized number gets a registration; accounts that share a number are left untouched and reported (the
/// preflight lists them). It links nothing as a successor, writes no financial record, and is idempotent. Each link is recorded as an append-only
/// <see cref="SpaceLifecycleAction.Backfill"/> event.
/// </summary>
public sealed class SpaceRegistrationBackfill(IAppDbContext db)
{
    /// <param name="onlyAccountIds">When given, only these accounts are considered: an operator authorizes a backfill for named accounts, never "everything unambiguous".</param>
    public async Task<BackfillResult> RunAsync(Guid tenantId, string actor, bool apply, CancellationToken ct = default, IReadOnlySet<Guid>? onlyAccountIds = null)
    {
        var report = await new SpaceIdentityPreflight(db).RunAsync(tenantId, ct);
        if (!apply) return new(0, report.UnlinkedAccounts - report.LinkableAccounts, report);

        await using var transaction = await db.BeginSpaceAccountTransactionAsync(tenantId, ct);
        // Re-check under the tenant lock: what the first look saw may have changed, and a blocking finding is never overridden.
        var locked = await new SpaceIdentityPreflight(db).RunAsync(tenantId, ct);
        var blocked = locked.Findings.Where(f => f.BlocksBackfill).SelectMany(f => f.AccountIds).ToHashSet();
        var accounts = await db.ObligationAccounts
            .Where(x => x.MunicipalityId == tenantId && x.SpaceRegistrationId == null
                && (x.Kind == ObligationKind.KanmanggaySpaceRental || x.Kind == ObligationKind.VegetableFruitSpaceRental)).ToListAsync(ct);
        var existing = await db.SpaceRegistrations.Where(x => x.MunicipalityId == tenantId).ToListAsync(ct);
        var linked = 0;
        foreach (var g in accounts.GroupBy(x => (x.Kind, Key: SpaceRegistration.Normalize(x.SubjectLabel))))
        {
            if (g.Count() != 1) continue;                                       // ambiguous: never guessed
            var account = g.Single();
            if (blocked.Contains(account.Id)) continue;                         // a blocking finding names it: a human decides
            if (onlyAccountIds is not null && !onlyAccountIds.Contains(account.Id)) continue;   // outside what was authorized
            if (string.IsNullOrWhiteSpace(account.SubjectLabel) || account.SubjectLabel.Trim().Length > 120) continue;
            // A registration already on this number (it can only have come from an authorized action) is reused only if it is the same space kind;
            // otherwise the account is left for a human.
            var registration = existing.FirstOrDefault(r => r.Kind == account.Kind && r.NormalizedNumber == g.Key.Key);
            if (registration is null)
            {
                registration = SpaceRegistration.Create(tenantId, account.Kind, account.SubjectLabel, actor);
                db.SpaceRegistrations.Add(registration);
                existing.Add(registration);
            }
            else continue;                                                      // a registration without this account's say-so: not guessed
            account.LinkRegistration(registration.Id);
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"backfill|{account.Id}"))).ToLowerInvariant();
            db.SpaceLifecycleEvents.Add(SpaceLifecycleEvent.Record(tenantId, registration.Id, SpaceLifecycleAction.Backfill, account.Id, null, account.ActiveFrom,
                Guid.NewGuid(), fingerprint, "backfill", "Space identity backfill: this account is alone on its number.", null, Guid.Empty, actor, DateTime.UtcNow));
            linked++;
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(linked, locked.UnlinkedAccounts - linked, locked);
    }
}

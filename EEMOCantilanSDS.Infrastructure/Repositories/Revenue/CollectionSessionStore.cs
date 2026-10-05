using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories.Revenue;

public sealed class CollectionSessionStore(AppDbContext db) : ICollectionSessionStore
{
    public async Task<IReadOnlyList<EEMOCantilanSDS.Application.Dtos.Revenue.CollectionPayorDto>> SearchPayorsAsync(Guid tenantId, string search, CancellationToken ct)
    {
        var payors = await BusinessPayorSearch.Apply(db.Payors.AsNoTracking(), tenantId, search)
            .Select(x => new EEMOCantilanSDS.Application.Dtos.Revenue.CollectionPayorDto(x.Id, x.DisplayName, null)).ToListAsync(ct);
        var ids = payors.Select(x => x.PayorId).ToArray();
        var occupancies = await db.Contracts.AsNoTracking().Where(x => x.MunicipalityId == tenantId
                && x.IsActive && x.PayorId != null && ids.Contains(x.PayorId.Value))
            .Select(x => new { PayorId = x.PayorId!.Value, Facility = x.Stall!.Facility!.ShortName, x.Stall.StallNo }).ToListAsync(ct);
        var accounts = await db.ObligationAccounts.AsNoTracking().Where(x => x.MunicipalityId == tenantId
                && ids.Contains(x.PayorId) && x.ActiveTo == null)
            .Select(x => new { x.PayorId, x.Kind, x.SubjectLabel }).ToListAsync(ct);
        return payors.Select(p => p with { Contexts = occupancies.Where(x => x.PayorId == p.PayorId)
                .Select(x => $"{x.Facility} · {x.StallNo}")
                .Concat(accounts.Where(x => x.PayorId == p.PayorId).Select(x => $"{ObligationCollectionSource.KindLabel(x.Kind)} · {x.SubjectLabel}"))
                .Distinct().OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray() }).ToArray();
    }
    public Task<bool> IsActiveCollectorAsync(Guid tenantId, Guid collectorId, CancellationToken ct) =>
        db.CollectorUsers.AsNoTracking().AnyAsync(x => x.MunicipalityId == tenantId && x.Id == collectorId && x.IsActive, ct);
    public Task<bool> PayorExistsAsync(Guid tenantId, Guid payorId, CancellationToken ct) =>
        db.Payors.AsNoTracking().AnyAsync(x => x.MunicipalityId == tenantId && x.Id == payorId, ct);
    public Task<MobileCollectionSession?> FindAsync(Guid tenantId, Guid sessionId, CancellationToken ct) =>
        db.MobileCollectionSessions.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == tenantId && x.ClientCollectionSessionId == sessionId, ct);
    public async Task SaveAsync(MobileCollectionSession session, CancellationToken ct)
    {
        db.MobileCollectionSessions.Add(session);
        await db.SaveChangesAsync(ct);
    }
    public async Task ExecuteAtomicallyAsync(Func<Task> action, CancellationToken ct)
    {
        try
        {
            await using var tx = await db.BeginSerializableTransactionAsync(ct);
            await action();
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException e) { db.ChangeTracker.Clear(); throw new CollectionSessionConcurrencyException(e); }
        catch (Npgsql.PostgresException e) when (e.SqlState is "40001" or "40P01" or "25P02")
        { db.ChangeTracker.Clear(); throw new CollectionSessionConcurrencyException(e); }
        catch { db.ChangeTracker.Clear(); throw; }
    }
    public async Task<IReadOnlyList<CollectionSessionCollection>> RefreshDispositionAsync(
        IReadOnlyList<CollectionSessionCollection> collections, CancellationToken ct)
    {
        var ids = collections.Select(x => x.CollectionId).ToArray();
        var corrected = await db.CollectionCorrections.AsNoTracking().Where(x => ids.Contains(x.OriginalCollectionId))
            .GroupBy(x => x.OriginalCollectionId).Select(g => new { Id = g.Key, Effect = g.Sum(x => x.FinancialEffectAmount) }).ToDictionaryAsync(x => x.Id, x => x.Effect, ct);
        return collections.Select(x => x with { Disposition = !corrected.TryGetValue(x.CollectionId, out var effect)
            ? "Posted" : x.Amount + effect == 0m ? "Voided" : "Corrected" }).ToArray();
    }
}

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
        var term = search.Trim().ToLowerInvariant();
        if (term.Length < 2) return [];
        return await db.Payors.AsNoTracking().Where(x => x.MunicipalityId == tenantId && x.DisplayName.ToLower().Contains(term))
            .OrderBy(x => x.DisplayName).Take(50)
            .Select(x => new EEMOCantilanSDS.Application.Dtos.Revenue.CollectionPayorDto(x.Id, x.DisplayName)).ToListAsync(ct);
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

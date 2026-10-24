using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>Only UI correlation. Each child keeps its own Collection, SRC, document, amount and corrections.</summary>
public sealed record CollectionSessionDisplayGroup(Guid SessionId, IReadOnlyList<Guid> CollectionIds);

/// <summary>
/// Matches persisted successful checkouts with complete, tenant/collector/date-matching canonical children
/// already in the authoritative caller's visible feed. Filtered/truncated/missing sessions stay individual.
/// No amount or source fact is derived from the stored result JSON.
/// </summary>
public static class CollectionSessionReadGroups
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<IReadOnlyList<CollectionSessionDisplayGroup>> LoadAsync(
        IAppDbContext db, Guid tenantId, DateOnly from, DateOnly to,
        IReadOnlyCollection<(Guid Id, DateOnly Date, Guid? CollectorId)> visible,
        Guid? collectorId, CancellationToken ct)
    {
        if (tenantId == Guid.Empty || visible.Count < 2) return [];
        var byId = visible.Where(x => x.Id != Guid.Empty).GroupBy(x => x.Id)
            .ToDictionary(g => g.Key, g => g.First());
        if (byId.Count < 2) return [];
        var sessions = db.MobileCollectionSessions.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && x.BusinessDate >= from && x.BusinessDate <= to);
        if (collectorId is { } selected) sessions = sessions.Where(x => x.CollectorId == selected);
        var saved = await sessions.Select(x => new {
            x.ClientCollectionSessionId, x.CollectorId, x.BusinessDate, x.ResultJson
        }).ToListAsync(ct);
        var candidates = new List<CollectionSessionDisplayGroup>();
        foreach (var session in saved)
        {
            CollectionSessionResult? result;
            try { result = JsonSerializer.Deserialize<CollectionSessionResult>(session.ResultJson, Json); }
            catch (JsonException) { continue; }
            if (result is not { Status: CollectionSessionStatus.Recorded } ||
                result.ClientCollectionSessionId != session.ClientCollectionSessionId ||
                result.Collections is not { Count: > 1 }) continue;
            var ids = result.Collections.Select(c => c.CollectionId).ToArray();
            if (ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length ||
                ids.Any(id => !byId.TryGetValue(id, out var child) ||
                    child.Date != session.BusinessDate || child.CollectorId != session.CollectorId)) continue;
            candidates.Add(new(session.ClientCollectionSessionId, ids));
        }
        // Corrupt overlapping associations are never allowed to hide any real collection.
        var duplicates = candidates.SelectMany(x => x.CollectionIds).GroupBy(x => x)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        return candidates.Where(x => x.CollectionIds.All(id => !duplicates.Contains(id)))
            .OrderBy(x => x.SessionId).ToArray();
    }
}

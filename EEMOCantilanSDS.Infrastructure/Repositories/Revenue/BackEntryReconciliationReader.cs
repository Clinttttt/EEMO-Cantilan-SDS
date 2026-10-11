using System.Text.Json;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories.Revenue;

/// <summary>
/// The first step of any office back-entry: read-only evidence of whether the collection is already on the server. It searches only this
/// tenant's Collections, posting operations and recorded Mobile sessions, and it NEVER writes. Its answer is evidence for an officer, never
/// authority to post: a missing row is not proof the money was never collected, because the collector's device may not have synchronised.
/// </summary>
public sealed class BackEntryReconciliationReader(AppDbContext db, ICurrentUserService user, ICurrentMunicipalityAccessor municipality) : IBackEntryReconciliationReader
{
    private const int MaxMatches = 25;

    public async Task<Result<BackEntryLookupResult>> LookupAsync(BackEntryLookupRequest request, CancellationToken ct = default)
    {
        if (!user.IsAuthenticated || user.Role is not ("SuperAdmin" or "Admin")) return Result<BackEntryLookupResult>.Forbidden();
        var tenant = municipality.MunicipalityId;
        if (tenant == Guid.Empty || user.MunicipalityId is { } claimed && claimed != tenant) return Result<BackEntryLookupResult>.Forbidden();

        var reference = request.ReferenceCode?.Trim();
        var byOperation = request.ClientOperationId is { } id && id != Guid.Empty ? id : (Guid?)null;
        var byCollectorDay = request.CollectorId is { } collectorId && collectorId != Guid.Empty && request.BusinessDate is not null;
        if (byOperation is null && string.IsNullOrEmpty(reference) && !byCollectorDay)
            return Result<BackEntryLookupResult>.Failure("Give a ClientOperationId, a reference code, or a collector with a business date.", ResultStatus.Invalid);
        if (request.Amount is { } amount && (amount <= 0m || amount != decimal.Round(amount, 2)))
            return Result<BackEntryLookupResult>.Failure("Use an amount in pesos and centavos greater than zero.", ResultStatus.Invalid);

        var matches = new List<BackEntryMatchDto>();
        var exact = false;
        var collections = db.Collections.AsNoTracking().Where(x => x.MunicipalityId == tenant);

        if (byOperation is { } operation)
        {
            foreach (var c in await collections.Where(x => x.ClientOperationId == operation).Take(MaxMatches).ToListAsync(ct))
                matches.Add(Of(c, "ClientOperationId"));
            foreach (var p in await db.PostingOperations.AsNoTracking().Where(x => x.MunicipalityId == tenant && x.ClientOperationId == operation).Take(MaxMatches).ToListAsync(ct))
            {
                var recorded = p.Status == PostingOperationStatus.Succeeded;
                matches.Add(new("PostingOperation", "ClientOperationId", recorded ? "Recorded" : "Rejected", p.CollectionId, null, null, null, null, null,
                    p.ClientOperationId, recorded ? p.Origin : (p.OutcomeDetails ?? p.OutcomeCode)));
            }
            foreach (var s in await db.MobileCollectionSessions.AsNoTracking().Where(x => x.MunicipalityId == tenant && x.ClientCollectionSessionId == operation).Take(MaxMatches).ToListAsync(ct))
                matches.Add(new("Session", "SessionId", "Recorded session", null, null, s.BusinessDate, TotalOf(s.ResultJson), s.CollectorId, null, s.ClientCollectionSessionId,
                    "A Mobile checkout under this id was recorded; its Collections carry the SRC references."));
            exact = matches.Count > 0;
        }

        if (!string.IsNullOrEmpty(reference))
        {
            var code = reference.ToUpperInvariant();
            foreach (var c in await collections.Where(x => x.ReferenceCode.ToUpper() == code).Take(MaxMatches).ToListAsync(ct))
                if (!matches.Any(m => m.CollectionId == c.Id && m.Kind == "Collection")) matches.Add(Of(c, "ReferenceCode"));
            exact = matches.Count > 0;
        }

        if (byCollectorDay)
        {
            var day = request.BusinessDate!.Value;
            var query = collections.Where(x => x.CollectorId == request.CollectorId && x.BusinessDate == day);
            if (request.Amount is { } wanted) query = query.Where(x => x.TotalAmount == wanted);
            foreach (var c in await query.OrderBy(x => x.RecordedAtUtc).Take(MaxMatches).ToListAsync(ct))
                if (!matches.Any(m => m.CollectionId == c.Id && m.Kind == "Collection"))
                    matches.Add(Of(c, request.Amount is null ? "SameCollectorDate" : "SameCollectorDateAmount"));
        }

        // Name the collector and say whether a correction already took the money back.
        var collectionIds = matches.Where(m => m.CollectionId is not null).Select(m => m.CollectionId!.Value).Distinct().ToArray();
        var corrected = (await db.CollectionCorrections.AsNoTracking().Where(x => x.MunicipalityId == tenant && collectionIds.Contains(x.OriginalCollectionId))
            .Select(x => x.OriginalCollectionId).ToListAsync(ct)).ToHashSet();
        var collectorIds = matches.Where(m => m.CollectorId is not null).Select(m => m.CollectorId!.Value).Distinct().ToArray();
        var names = await db.CollectorUsers.AsNoTracking().Where(x => x.MunicipalityId == tenant && collectorIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
        var shown = matches.Select(m => m with
        {
            CollectorName = m.CollectorId is { } c && names.TryGetValue(c, out var name) ? name : null,
            State = m.Kind == "Collection" && m.CollectionId is { } cid && corrected.Contains(cid) ? "Corrected" : m.State
        }).Take(MaxMatches).ToArray();

        var result = exact && shown.Any(m => m.State is "Recorded" or "Corrected" or "Recorded session")
            ? new BackEntryLookupResult("AlreadyRecorded", "The server already holds this collection. Do not enter it again.", shown)
            : shown.Length > 0
                ? new BackEntryLookupResult("PossibleMatch", "Similar collections exist. An officer must compare them before anything is entered; a match is not a duplicate until it is the same payment.", shown)
                : new BackEntryLookupResult("NoServerRecord", "No server record. This is NOT proof the collection never happened: the collector's device may still hold it unsynchronised. Check the device and the receipt before any back-entry.", shown);
        return Result<BackEntryLookupResult>.Success(result);
    }

    private static BackEntryMatchDto Of(Collection c, string basis) => new("Collection", basis, "Recorded", c.Id, c.ReferenceCode, c.BusinessDate, c.TotalAmount,
        c.CollectorId, null, c.ClientOperationId);

    private static decimal? TotalOf(string resultJson)
    {
        try
        {
            using var document = JsonDocument.Parse(resultJson);
            foreach (var property in document.RootElement.EnumerateObject())
                if (property.NameEquals("grandTotal") || property.Name.Equals("GrandTotal", StringComparison.OrdinalIgnoreCase))
                    return property.Value.TryGetDecimal(out var total) ? total : null;
        }
        catch (JsonException) { }
        return null;
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.Extensions.Logging;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>Non-financial orchestration. Source adapters quote and call their original canonical writers.</summary>
public sealed class CollectionSessionWorkflow(ICollectionSessionStore store, ICollectionSessionSources sources,
    ICurrentUserService user, ICurrentMunicipalityAccessor municipality, IClock clock,
    ILogger<CollectionSessionWorkflow> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private async Task<bool> AuthorizedAsync(CancellationToken ct) => user.IsAuthenticated && user.Role == "Collector"
        && user.CollectorId is { } id && id != Guid.Empty && municipality.MunicipalityId != Guid.Empty
        && (!user.MunicipalityId.HasValue || user.MunicipalityId == municipality.MunicipalityId)
        && await store.IsActiveCollectorAsync(municipality.MunicipalityId, id, ct);

    public async Task<Result<CollectionSessionDiscovery>> DiscoverAsync(Guid? payorId, CancellationToken ct = default)
    {
        if (!await AuthorizedAsync(ct)) return Result<CollectionSessionDiscovery>.Forbidden();
        if (payorId is { } id && !await store.PayorExistsAsync(municipality.MunicipalityId, id, ct))
            return Result<CollectionSessionDiscovery>.NotFound();
        return Result<CollectionSessionDiscovery>.Success(await sources.DiscoverAsync(payorId, clock.PhilippineToday, ct));
    }
    public async Task<Result<IReadOnlyList<EEMOCantilanSDS.Application.Dtos.Revenue.CollectionPayorDto>>> SearchPayorsAsync(string? search, CancellationToken ct = default)
    {
        if (!await AuthorizedAsync(ct)) return Result<IReadOnlyList<EEMOCantilanSDS.Application.Dtos.Revenue.CollectionPayorDto>>.Forbidden();
        return Result<IReadOnlyList<EEMOCantilanSDS.Application.Dtos.Revenue.CollectionPayorDto>>.Success(
            await store.SearchPayorsAsync(municipality.MunicipalityId, search ?? "", ct));
    }
    public async Task<Result<IReadOnlyList<CollectionSourceSearchResult>>> SearchSourcesAsync(string? search, CancellationToken ct = default) =>
        !await AuthorizedAsync(ct) ? Result<IReadOnlyList<CollectionSourceSearchResult>>.Forbidden() :
        Result<IReadOnlyList<CollectionSourceSearchResult>>.Success(await sources.SearchSourcesAsync(search, ct));
    public async Task<Result<CollectionSessionDiscovery>> DiscoverNativeAsync(CollectionSourceIdentity? identity, CancellationToken ct = default)
    {
        if (!await AuthorizedAsync(ct)) return Result<CollectionSessionDiscovery>.Forbidden();
        if (identity is not null && !await sources.SourceExistsAsync(identity, ct)) return Result<CollectionSessionDiscovery>.NotFound();
        return Result<CollectionSessionDiscovery>.Success(await sources.DiscoverNativeAsync(identity, clock.PhilippineToday, ct));
    }

    public async Task<Result<CollectionSessionQuote>> QuoteAsync(CollectionSessionIntent intent, CancellationToken ct = default)
    {
        if (!await AuthorizedAsync(ct)) return Result<CollectionSessionQuote>.Forbidden();
        return Result<CollectionSessionQuote>.Success(await PreflightAsync(intent, ct));
    }

    private async Task<CollectionSessionQuote> PreflightAsync(CollectionSessionIntent intent, CancellationToken ct)
    {
        var errors = new List<CollectionSessionProblem>();
        var quotes = new List<CollectionSessionItemQuote>();
        if (intent.ClientCollectionSessionId == Guid.Empty || intent.Items is null || intent.Items.Count == 0
            || intent.Items.Any(i => i is null || i.ClientItemId == Guid.Empty)
            || intent.Items.Select(i => i.ClientItemId).Distinct().Count() != intent.Items.Count)
            errors.Add(new(null, "InvalidIntent", "Use a session identity and distinct item identities."));
        if (intent.BusinessDate == default || intent.BusinessDate > clock.PhilippineToday)
            errors.Add(new(null, "InvalidBusinessDate", "Choose a valid collection business date."));
        if (intent.PayorId is { } payor && !await store.PayorExistsAsync(municipality.MunicipalityId, payor, ct))
            errors.Add(new(null, "InvalidPayor", "The selected payer is not available."));
        if (intent.SourceIdentity is not null && (intent.PayorId.HasValue || !await sources.SourceExistsAsync(intent.SourceIdentity, ct)))
            errors.Add(new(null, "InvalidSource", "Choose one valid Source Identity."));
        if (intent.PayerSnapshot?.Trim().Length > 200) errors.Add(new(null, "InvalidPayerSnapshot", "Use a payer name of at most 200 characters."));
        if (errors.Count == 0)
        {
            // Duplicate source contexts in one checkout cannot spend the same balance twice.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in intent.Items!.OrderBy(i => i.ClientItemId))
            {
                var key = item.Kind switch
                {
                    CollectionSessionItemKind.Water when item.Water is { } w => $"Water|{w.StallId}|{w.Year}|{w.Month}",
                    CollectionSessionItemKind.Obligation when item.Obligation is { } o => $"Obligation|{o.AccountId}|{o.Year}|{o.Month}",
                    CollectionSessionItemKind.Electricity when item.Electricity is { UtilityBillId: var bill } && bill != Guid.Empty => $"Electricity|{bill}",
                    CollectionSessionItemKind.Electricity when item.Electricity is { } e => $"Electricity|{e.StallId}|{e.Year}|{e.Month}",
                    // These are independent transactions, not shared balances. Each keeps its own document boundary.
                    CollectionSessionItemKind.Weighing or CollectionSessionItemKind.Slaughter or CollectionSessionItemKind.SourceNative => $"Transaction|{item.ClientItemId}",
                    CollectionSessionItemKind.VendorFee => $"VendorFee|{item.ClientItemId}",
                    CollectionSessionItemKind.NpmWholePayment when item.NpmWhole is { } n => $"Npm|{n.StallId}|{n.Year}|{n.Month}",
                    CollectionSessionItemKind.NpmDaily when item.NpmDaily is { } daily => $"Npm|{daily.StallId}|{intent.BusinessDate.Year}|{intent.BusinessDate.Month}",
                    CollectionSessionItemKind.MonthlyRent when item.Rent is { } r => $"Rent|{r.StallId}|{r.Year}|{r.Month}",
                    _ => JsonSerializer.Serialize(new { item.Kind, item.Service }, Json)
                };
                if (!seen.Add(key)) { errors.Add(new(item.ClientItemId, "DuplicateBusinessEvent", "This source is already in the checkout.")); continue; }
                var resolved = await sources.QuoteAsync(intent, item, ct);
                if (resolved.Problem is { } problem) errors.Add(problem);
                if (resolved.Quote is { } quote) quotes.Add(quote with { GroupId = ChildOperationId(municipality.MunicipalityId, intent.ClientCollectionSessionId, item.ClientItemId) });
            }
        }
        var totals = quotes.GroupBy(q => q.Instrument).Select(g => new CollectionSessionInstrumentTotal(g.Key, g.Sum(q => q.Amount))).ToArray();
        var token = errors.Count == 0 ? Hash(JsonSerializer.Serialize(new
        {
            Intent = IntentFingerprint(intent), Tenant = municipality.MunicipalityId, Collector = user.CollectorId,
            Items = quotes.OrderBy(q => q.ClientItemId).Select(q => new { q.ClientItemId, q.SourceVersion, q.Instrument, Amount = Money(q.Amount), q.GroupId })
        }, Json)) : null;
        return new(intent.ClientCollectionSessionId, intent.PayorId, intent.BusinessDate, quotes, totals,
            quotes.Sum(q => q.Amount), token, errors);
    }

    public async Task<Result<CollectionSessionResult>> RecordAsync(RecordCollectionSessionRequest request, CancellationToken ct = default)
    {
        if (!await AuthorizedAsync(ct)) return Result<CollectionSessionResult>.Forbidden();
        var intent = request.Intent;
        if (intent.Items is null || intent.Items.Any(i => i is null)) return Result<CollectionSessionResult>.Success(Review(intent, new CollectionSessionProblem(null, "InvalidIntent", "Collection items are required.")));
        var fingerprint = IntentFingerprint(intent);
        var prior = await store.FindAsync(municipality.MunicipalityId, intent.ClientCollectionSessionId, ct);
        if (prior is not null) return await ResolveAsync(prior, fingerprint, intent, ct);
        CollectionSessionResult? result = null;
        try
        {
            await store.ExecuteAtomicallyAsync(async () =>
            {
                var existing = await store.FindAsync(municipality.MunicipalityId, intent.ClientCollectionSessionId, ct);
                if (existing is not null) { result = (await ResolveAsync(existing, fingerprint, intent, ct)).Value
                    ?? Review(intent, new CollectionSessionProblem(null, "SessionUnavailable", "This session is not available.")); return; }
                var quote = await PreflightAsync(intent, ct);
                if (!quote.CanRecord) { result = Review(intent, quote.Problems.ToArray()); return; }
                if (string.IsNullOrWhiteSpace(request.QuoteFingerprint) || request.QuoteFingerprint != quote.QuoteFingerprint)
                {
                    result = Review(intent, new CollectionSessionProblem(null, "QuoteStale", "Review the current quote before recording this checkout."));
                    return;
                }
                var collections = new List<CollectionSessionCollection>();
                foreach (var item in intent.Items.OrderBy(i => i.ClientItemId))
                    collections.Add(await sources.PostAsync(intent, item,
                        ChildOperationId(municipality.MunicipalityId, intent.ClientCollectionSessionId, item.ClientItemId), ct));
                result = new(intent.ClientCollectionSessionId, CollectionSessionStatus.Recorded, intent.PayorId,
                    collections.Sum(c => c.Amount), collections, []);
                await store.SaveAsync(MobileCollectionSession.Recorded(municipality.MunicipalityId,
                    intent.ClientCollectionSessionId, user.CollectorId!.Value, intent.PayorId, intent.BusinessDate,
                    fingerprint, JsonSerializer.Serialize(result, Json)), ct);
            }, ct);
        }
        catch (CollectionSessionPostingException e)
        {
            return Result<CollectionSessionResult>.Success(Review(intent, new CollectionSessionProblem(e.ItemId, "SourceChanged", e.Message)));
        }
        catch (CollectionSessionConcurrencyException)
        {
            var winner = await store.FindAsync(municipality.MunicipalityId, intent.ClientCollectionSessionId, ct);
            if (winner is not null) return await ResolveAsync(winner, fingerprint, intent, ct);
            return Result<CollectionSessionResult>.Failure("The checkout changed concurrently. Retry the same session.", 503);
        }
        logger.LogInformation("Collector checkout {SessionId} returned {Status} with {Count} canonical collections",
            intent.ClientCollectionSessionId, result?.Status, result?.Collections.Count ?? 0);
        return Result<CollectionSessionResult>.Success(result!);
    }

    public async Task<Result<CollectionSessionResult>> GetAsync(Guid sessionId, CancellationToken ct = default)
    {
        if (!await AuthorizedAsync(ct)) return Result<CollectionSessionResult>.Forbidden();
        var prior = await store.FindAsync(municipality.MunicipalityId, sessionId, ct);
        if (prior is null || prior.CollectorId != user.CollectorId) return Result<CollectionSessionResult>.NotFound();
        var result = JsonSerializer.Deserialize<CollectionSessionResult>(prior.ResultJson, Json)!;
        return Result<CollectionSessionResult>.Success(result with { ExistingOutcome = true,
            Collections = await store.RefreshDispositionAsync(result.Collections, ct) });
    }

    private async Task<Result<CollectionSessionResult>> ResolveAsync(MobileCollectionSession prior, string fingerprint,
        CollectionSessionIntent intent, CancellationToken ct)
    {
        if (prior.CollectorId != user.CollectorId) return Result<CollectionSessionResult>.Forbidden();
        if (prior.IntentFingerprint != fingerprint)
            return Result<CollectionSessionResult>.Success(Review(intent, new CollectionSessionProblem(null, "SessionIntentConflict", "This session identity belongs to a different checkout. Use a new identity.")));
        return await GetAsync(prior.ClientCollectionSessionId, ct);
    }
    private static CollectionSessionResult Review(CollectionSessionIntent intent, params CollectionSessionProblem[] problems) =>
        new(intent.ClientCollectionSessionId, CollectionSessionStatus.NeedsReview, intent.PayorId, 0m, [], problems);

    public static string IntentFingerprint(CollectionSessionIntent intent)
    {
        if (intent.SourceIdentity is not null || intent.PayerSnapshot is not null || intent.Items.Any(i => i.Native is not null))
            return Hash(JsonSerializer.Serialize(new { Version = 3, intent.BusinessDate, intent.PayorId, intent.SourceIdentity,
                PayerSnapshot = intent.PayerSnapshot?.Trim(), Items = intent.Items.OrderBy(i => i.ClientItemId) }, Json));
        var original = JsonSerializer.Serialize(new
    {
        Version = 1, intent.BusinessDate, intent.PayorId,
        Items = intent.Items.OrderBy(i => i.ClientItemId).Select(i => new
        {
            i.ClientItemId, i.Kind, Amount = Money(i.ConfirmedAmount), i.Water, i.Obligation, i.Electricity,
            Service = i.Service is null ? null : i.Service with
            { OperationCode = (i.Service.OperationCode ?? "").Trim().ToUpperInvariant(), VehicleClassCode = i.Service.VehicleClassCode?.Trim().ToUpperInvariant(), Reference = i.Service.Reference?.Trim() }
        })
        }, Json);
        // Preserve the v1 fingerprint byte-for-byte for existing queued/replayed sessions.
        return intent.Items.Any(i => i.Weighing is not null || i.Slaughter is not null || i.VendorFee is not null || i.NpmWhole is not null)
            ? Hash(JsonSerializer.Serialize(new { Version = 2, Original = original,
                Specialized = intent.Items.OrderBy(i => i.ClientItemId).Select(i => new { i.ClientItemId, i.Weighing, i.Slaughter, i.VendorFee, i.NpmWhole }) }, Json))
            : Hash(original);
    }
    public static Guid ChildOperationId(Guid tenant, Guid session, Guid item) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"StallTrack.CollectionSession.v1|{tenant:N}|{session:N}|{item:N}"))[..16]);
    private static string Money(decimal amount) => amount.ToString("0.00##########################", CultureInfo.InvariantCulture);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed class CollectionSessionPostingException(Guid itemId, string message) : Exception(message) { public Guid ItemId { get; } = itemId; }
public sealed class CollectionSessionConcurrencyException(Exception inner) : Exception("Concurrent checkout", inner);

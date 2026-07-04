using EEMOCantilanSDS.Application.Command.Payors.GenerateStallActivationCode;
using EEMOCantilanSDS.Application.Command.Sync.SyncOfflineCollections;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Payors;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Application.Requests.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Abstractions;

namespace EEMOCantilanSDS.Mobile.Services;

/// <summary>
/// Transparent offline read-through cache over <see cref="IMobileApiClient"/>. Read (GET) calls cache
/// their result on success and, when the device is offline (the HTTP layer throws), serve the last cached
/// value so a collector can still open the app and see their lists. Write calls pass straight through —
/// captured collections are handled by the offline queue, not this cache.
///
/// <para>Only a genuine connectivity failure (an exception from the inner client) falls back to cache; a
/// real server response (4xx/5xx, e.g. Unauthorized) is returned as-is so it is never masked by stale data.</para>
/// </summary>
public sealed class CachingMobileApiClient(
    IMobileApiClient inner,
    IOfflineReadCache cache,
    IConnectivityMonitor connectivity) : IMobileApiClient
{
    // Caches invalidated after a write are the COLLECTION-ENTRY views (the data that changes the moment
    // a collection is recorded/synced). menu + profile are excluded so the offline app-open keeps working.
    // "records" and "report" are deliberately NOT here: they are offline REVIEW views, and since reads
    // always hit the network when online (see ReadThroughAsync), wiping them gives no online benefit and
    // only destroys the offline copy a collector relies on after going offline.
    private static readonly string[] CollectionPrefixes =
        { "npm", "utility", "monthly", "slaughter", "trm", "tpm" };

    // ── Reads: cache on success, serve last-known on connectivity failure ───
    public Task<Result<MobileMenuDto>> GetMenuAsync() =>
        ReadThroughAsync("menu", inner.GetMenuAsync);

    public Task<Result<MobileCollectorProfileDto>> GetProfileAsync() =>
        ReadThroughAsync("profile", inner.GetProfileAsync);

    public Task<Result<IReadOnlyList<MobileCollectorRecordDto>>> GetRecordsAsync(FacilityCode? facility, DateOnly from, DateOnly to) =>
        ReadThroughAsync($"records|{facility?.ToString() ?? "all"}|{from:yyyy-MM-dd}|{to:yyyy-MM-dd}",
            () => inner.GetRecordsAsync(facility, from, to));

    public Task<Result<MobileCollectorReportDto>> GetReportAsync(FacilityCode? facility, int year, int month) =>
        ReadThroughAsync($"report|{facility?.ToString() ?? "all"}|{year}|{month}",
            () => inner.GetReportAsync(facility, year, month));

    public Task<Result<MobileNpmCollectionDto>> GetNpmCollectionAsync(int year, int month) =>
        ReadThroughAsync($"npm|{year}|{month}", () => inner.GetNpmCollectionAsync(year, month));

    public Task<Result<MobileNpmUtilityDto>> GetNpmUtilityAsync(int year, int month) =>
        ReadThroughAsync($"utility|{year}|{month}", () => inner.GetNpmUtilityAsync(year, month));

    public Task<Result<MobileMonthlyCollectionDto>> GetMonthlyCollectionAsync(FacilityCode facility, int year, int month) =>
        ReadThroughAsync($"monthly|{facility}|{year}|{month}", () => inner.GetMonthlyCollectionAsync(facility, year, month));

    public Task<Result<MobileSlaughterCollectionDto>> GetSlaughterCollectionAsync(int year, int month, int day) =>
        ReadThroughAsync($"slaughter|{year}|{month}|{day}", () => inner.GetSlaughterCollectionAsync(year, month, day));

    public Task<Result<MobileTrmCollectionDto>> GetTrmCollectionAsync() =>
        ReadThroughAsync("trm", inner.GetTrmCollectionAsync);

    public Task<Result<MobileTpmCollectionDto>> GetTpmCollectionAsync() =>
        ReadThroughAsync("tpm", inner.GetTpmCollectionAsync);

    // ── Writes / sync: pass through, then invalidate the now-stale collection caches ──
    public Task<Result<bool>> RecordNpmCollectionAsync(RecordMobileNpmCollectionRequest request) =>
        InvalidatingAsync(() => inner.RecordNpmCollectionAsync(request));

    public Task<Result<bool>> RecordNpmUtilityPaymentAsync(RecordMobileUtilityPaymentRequest request) =>
        InvalidatingAsync(() => inner.RecordNpmUtilityPaymentAsync(request));

    public Task<Result<bool>> RecordMonthlyCollectionAsync(RecordMobileMonthlyCollectionRequest request) =>
        InvalidatingAsync(() => inner.RecordMonthlyCollectionAsync(request));

    public Task<Result<bool>> RecordSlaughterAsync(RecordMobileSlaughterRequest request) =>
        InvalidatingAsync(() => inner.RecordSlaughterAsync(request));

    public Task<Result<bool>> UpdateSlaughterAsync(UpdateMobileSlaughterRequest request) =>
        InvalidatingAsync(() => inner.UpdateSlaughterAsync(request));

    public Task<Result<TrmTripDto>> RecordTripAsync(RecordMobileTripRequest request) =>
        InvalidatingAsync(() => inner.RecordTripAsync(request));

    public Task<Result<TrmTransporterDto>> AddTransporterAsync(string name, string organization, string route, string plate) =>
        InvalidatingAsync(() => inner.AddTransporterAsync(name, organization, route, plate));

    public Task<Result<TpmVendorAttendanceDto>> AddTpmVendorAsync(AddMobileTpmVendorRequest request) =>
        InvalidatingAsync(() => inner.AddTpmVendorAsync(request));

    public Task<Result<bool>> MarkTpmVendorPaidAsync(MarkMobileTpmVendorPaidRequest request) =>
        InvalidatingAsync(() => inner.MarkTpmVendorPaidAsync(request));

    public Task<Result<bool>> IssueOnlinePaymentOrNumberAsync(Guid transactionId, string orNumber) =>
        InvalidatingAsync(() => inner.IssueOnlinePaymentOrNumberAsync(transactionId, orNumber));

    public Task<Result<SyncOfflineCollectionsResultDto>> SyncOfflineCollectionsAsync(SyncOfflineCollectionsCommand command) =>
        InvalidatingAsync(() => inner.SyncOfflineCollectionsAsync(command));

    // ── Pass-through (no collection-cache impact) ───────────────────────────
    public async Task<Result<bool>> UpdateProfileAsync(UpdateMobileProfileRequest request)
    {
        var result = await inner.UpdateProfileAsync(request);
        if (result.IsSuccess)
            await cache.RemoveByPrefixAsync("menu", "profile"); // name shows in both
        return result;
    }

    public Task<Result<bool>> HideSuggestionAsync(HideMobileSuggestionRequest request) =>
        inner.HideSuggestionAsync(request);

    public Task<Result<StallActivationCodeDto>> GenerateActivationCodeAsync(GenerateStallActivationCodeCommand command) =>
        inner.GenerateActivationCodeAsync(command);

    // ── Read-through core ───────────────────────────────────────────────────
    private async Task<Result<T>> ReadThroughAsync<T>(string key, Func<Task<Result<T>>> fetch)
    {
        // Offline → serve cache immediately; no point waiting out the HTTP timeout.
        if (!connectivity.IsOnline)
            return await ServeFromCacheAsync<T>(key);

        try
        {
            var result = await fetch();

            // Cache genuine successes.
            if (result.IsSuccess && result.Value is not null)
            {
                await cache.SetAsync(key, result.Value);
                return result;
            }

            // The online check passed but the server/tunnel answered with a TRANSIENT failure (5xx, or
            // 0 = no usable response). Prefer the last-known value over an error — but only if we actually
            // have one. Definitive client/auth failures (400/401/403/404) are returned untouched so they
            // are never masked by stale data.
            if (result.StatusCode == 0 || result.StatusCode >= 500)
            {
                var stale = await cache.GetAsync<T>(key);
                if (stale is not null)
                    return Result<T>.Success(stale);
            }

            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            // Online check passed but the server/tunnel was unreachable → fall back to last-known value.
            // Only connectivity-shaped exceptions are caught; real bugs (e.g. serialization) propagate.
            return await ServeFromCacheAsync<T>(key);
        }
    }

    private async Task<Result<T>> ServeFromCacheAsync<T>(string key)
    {
        var cached = await cache.GetAsync<T>(key);
        return cached is not null
            ? Result<T>.Success(cached)
            : Result<T>.Failure("You're offline and no saved data is available yet.", 0);
    }

    // Run a mutation, then drop the collection caches it may have changed (online-only path → cheap re-fetch).
    private async Task<Result<T>> InvalidatingAsync<T>(Func<Task<Result<T>>> mutate)
    {
        var result = await mutate();
        if (result.IsSuccess)
            await cache.RemoveByPrefixAsync(CollectionPrefixes);
        return result;
    }
}

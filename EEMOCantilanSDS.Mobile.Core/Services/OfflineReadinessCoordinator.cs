using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Abstractions;
using EEMOCantilanSDS.Mobile.Presentation;

namespace EEMOCantilanSDS.Mobile.Services;

/// <summary>
/// Warms existing read-through collection lists without entering individual screens.
/// This is preparation, never financial authorization or proof that a checkout can be posted offline.
/// Quotation, balances, source search and registration decisions remain server-authoritative.
/// </summary>
public sealed class OfflineReadinessCoordinator : IDisposable
{
    private readonly IMobileApiClient _api;
    private readonly IConnectivityMonitor _connectivity;
    private readonly object _gate = new();
    private CancellationTokenSource? _runCancellation;
    private MobileMenuDto? _menu;
    private IReadOnlyList<CollectorOperationCapabilityDto> _operations = [];
    private string? _lastKey;
    private DateTime _lastCompletedUtc;
    private int _generation;
    private bool _disposed;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(10);

    public OfflineReadinessCoordinator(IMobileApiClient api, IConnectivityMonitor connectivity)
    {
        _api = api;
        _connectivity = connectivity;
        _connectivity.ConnectivityRestored += OnConnectivityRestored;
    }

    public event Action? Changed;
    public bool IsPreparing { get; private set; }
    public int Completed { get; private set; }
    public int Total { get; private set; }
    public int Failed { get; private set; }

    public string StatusText => !_connectivity.IsOnline
        ? "Offline · saved lists only"
        : IsPreparing ? $"Preparing offline lists · {Completed}/{Total}"
        : Total == 0 ? "No assigned offline lists to prepare"
        : Failed > 0 ? $"Offline lists partially fetched · {Completed - Failed}/{Total}"
        : $"Offline lists fetched · {Completed}/{Total}";

    public void Start(MobileMenuDto menu, IReadOnlyList<CollectorOperationCapabilityDto> operations)
    {
        lock (_gate)
        {
            if (_disposed) return;
            var key = $"{menu.CollectorId:N}|{menu.Today:yyyy-MM-dd}|{string.Join(",", operations.Where(x => x.IsAssigned && x.IsCollectible).Select(x => x.OperationCode).OrderBy(x => x, StringComparer.Ordinal))}|{string.Join(",", menu.Facilities.Where(x => x.IsAssigned && x.IsAvailable).Select(x => x.Code).OrderBy(x => x))}";
            _menu = menu;
            _operations = operations.ToArray();
            var retryAfter = Failed > 0 ? TimeSpan.FromMinutes(1) : RefreshInterval;
            if (_lastKey == key && (IsPreparing || DateTime.UtcNow - _lastCompletedUtc < retryAfter))
                return;
            _lastKey = key;
            _runCancellation?.Cancel();
            _runCancellation?.Dispose();
            _runCancellation = new CancellationTokenSource();
            var token = _runCancellation.Token;
            var generation = ++_generation;
            Completed = Failed = 0;
            var steps = BuildSteps(menu, operations);
            Total = steps.Count;
            IsPreparing = _connectivity.IsOnline && Total > 0;
            Notify();
            if (IsPreparing)
                _ = PrepareAsync(steps, generation, token);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _generation++;
            _runCancellation?.Cancel();
            _runCancellation?.Dispose();
            _runCancellation = null;
            _menu = null;
            _operations = [];
            _lastKey = null;
            IsPreparing = false;
            Total = Completed = Failed = 0;
        }
        Notify();
    }

    private void OnConnectivityRestored()
    {
        MobileMenuDto? menu;
        IReadOnlyList<CollectorOperationCapabilityDto> operations;
        lock (_gate)
        {
            menu = _menu;
            operations = _operations;
            _lastCompletedUtc = DateTime.MinValue;
            _lastKey = null; // a reconnect must refresh partial/interrupted work, even while a former pass is in flight
        }
        if (menu is not null) Start(menu, operations);
    }

    private List<Func<Task<bool>>> BuildSteps(MobileMenuDto menu, IReadOnlyList<CollectorOperationCapabilityDto> operations)
    {
        var steps = new List<Func<Task<bool>>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(string key, Func<Task<bool>> action)
        {
            if (seen.Add(key)) steps.Add(action);
        }

        var year = menu.Today.Year;
        var month = menu.Today.Month;
        foreach (var facility in menu.Facilities.Where(x => x.IsAssigned && x.IsAvailable))
        {
            switch (facility.Archetype)
            {
                case BillingArchetype.DailyStall:
                    Add("npm-day", async () => (await _api.GetNpmCollectionAsync(year, month)).IsSuccess);
                    Add("npm-arrears", async () => (await _api.GetNpmArrearsAsync(year, month)).IsSuccess);
                    Add("npm-utility", async () => (await _api.GetNpmUtilityAsync(year, month)).IsSuccess);
                    break;
                case BillingArchetype.MonthlyRental:
                    var code = facility.Code;
                    Add($"monthly-{code}", async () => (await _api.GetMonthlyCollectionAsync(code, year, month)).IsSuccess);
                    break;
                case BillingArchetype.PerHead:
                    Add("slaughter-terms", async () => (await _api.GetSlaughterTermsAsync(menu.Today)).IsSuccess);
                    Add("slaughter-day", async () => (await _api.GetSlaughterCollectionAsync(year, month, menu.Today.Day)).IsSuccess);
                    break;
                case BillingArchetype.WeeklyMarket:
                    Add("taboan", async () => (await _api.GetTpmCollectionAsync()).IsSuccess);
                    break;
            }
        }

        foreach (var operation in operations.Where(x => x.IsAssigned && x.IsCollectible))
        {
            var code = operation.OperationCode;
            switch (code)
            {
                case "WCF":
                    Add("wcf-sources", async () => (await _api.GetWcfSourcesAsync(year, month)).IsSuccess);
                    Add("wcf-obligations", async () => (await _api.GetWcfObligationsAsync(year, month)).IsSuccess);
                    break;
                case "ECF":
                    Add("ecf-sources", async () => (await _api.GetMobileEcfSourcesAsync()).IsSuccess);
                    break;
                case "FISH_MEAT_VENDOR_FEE":
                case "WEIGHT_AND_MEASURE":
                    Add("vendor-sources", async () => (await _api.GetDirectVendorFeeSourcesAsync()).IsSuccess);
                    break;
                default:
                    var entry = GovernedServiceCatalog.Find(code);
                    if (entry is null) break;
                    var mode = entry.ModeAware ? GovernedServiceMode.DailyTransaction : (GovernedServiceMode?)null;
                    Add($"terms-{code}", async () => (await _api.GetOperationTermsAsync(code, mode)).IsSuccess);
                    break;
            }
        }
        return steps;
    }

    private async Task PrepareAsync(IReadOnlyList<Func<Task<bool>>> steps, int generation, CancellationToken cancellation)
    {
        using var throttle = new SemaphoreSlim(2);
        var tasks = steps.Select(async step =>
        {
            try
            {
                await throttle.WaitAsync(cancellation);
                try
                {
                    if (cancellation.IsCancellationRequested || !_connectivity.IsOnline) return;
                    var ok = await step();
                    lock (_gate)
                    {
                        if (generation != _generation || cancellation.IsCancellationRequested) return;
                        if (!ok) Failed++;
                        Completed++;
                    }
                    Notify();
                }
                finally { throttle.Release(); }
            }
            catch (OperationCanceledException) { }
            catch
            {
                lock (_gate)
                {
                    if (generation != _generation || cancellation.IsCancellationRequested) return;
                    Failed++;
                    Completed++;
                }
                Notify();
            }
        }).ToArray();
        await Task.WhenAll(tasks);
        lock (_gate)
        {
            if (generation != _generation) return;
            IsPreparing = false;
            _lastCompletedUtc = DateTime.UtcNow;
        }
        Notify();
    }

    private void Notify() => Changed?.Invoke();

    public void Dispose()
    {
        Stop();
        lock (_gate) _disposed = true;
        _connectivity.ConnectivityRestored -= OnConnectivityRestored;
    }
}

namespace EEMOCantilanSDS.Client.Services;

// Contracts Monitoring — what the page needs from the server, expressed in presentation terms.
//
// The page never works out a status, an expiry or a remaining-day count: every one of those arrives from the canonical
// monitoring API and is only displayed. This file is the seam between that API and the page. Until the API contract is
// published and bound, the registered source reports "unavailable" and the page says so; it does not invent rows.

/// <summary>The narrowing the person has chosen. Everything here is sent to the server; the page filters nothing itself.</summary>
public sealed record ContractsMonitoringQuery(
    string? Search = null,
    string? Operation = null,
    string? Status = null,
    int? ExpiryWindow = null,
    string? OccupancyBasis = null,
    int Page = 1,
    int PageSize = ContractsMonitoringQuery.DefaultPageSize)
{
    public const int DefaultPageSize = 25;
}

/// <summary>Counts for the summary strip, as the server reports them for everything matching the search/operation/basis filters
/// (not the status or window). Active already includes Expiring soon; the 7-day count is a subset of the 30-day count.</summary>
public sealed record ContractsMonitoringSummary(int Active, int ExpiringIn7Days, int ExpiringIn30Days, int Expired, int NoContract,
    int Ended, int Unknown, int NotYetEffective, int Total);

/// <summary>One choice in a filter dropdown (an operation, a status). The value goes back to the server unchanged.</summary>
public sealed record ContractsMonitoringOption(string Value, string Label);

/// <summary>A labelled fact shown in the details drawer. Only facts the server provided are listed.</summary>
public sealed record ContractsMonitoringFact(string Label, string Value);

/// <summary>One monitored holder/space. Dates and day counts are the server's; null means "not recorded".</summary>
public sealed record ContractsMonitoringRow(
    string Id,
    string Occupant,
    string Operation,
    string Space,
    string? ContractBasis,
    string? Duration,
    DateOnly? Expiry,
    string Status,
    int? RemainingDays,
    IReadOnlyList<ContractsMonitoringFact> Facts,
    string? SourceHref);

public sealed record ContractsMonitoringPage(
    ContractsMonitoringSummary Summary,
    IReadOnlyList<ContractsMonitoringRow> Rows,
    int TotalRows,
    int Page,
    int PageSize,
    IReadOnlyList<ContractsMonitoringOption> Operations);

/// <summary>The outcome of a load: a page, or a reason there is none. A failure is never turned into an empty page.</summary>
public sealed record ContractsMonitoringResult(ContractsMonitoringPage? Page, ContractsMonitoringFailure? Failure = null)
{
    public static ContractsMonitoringResult Ok(ContractsMonitoringPage page) => new(page);
    public static ContractsMonitoringResult Fail(ContractsMonitoringFailure failure) => new(null, failure);
}

public enum ContractsMonitoringFailure
{
    /// <summary>The call failed or timed out; trying again may work.</summary>
    Error,
    /// <summary>The signed-in account may not see this register.</summary>
    Unauthorized,
    /// <summary>The monitoring source is not connected in this environment.</summary>
    Unavailable
}

public interface IContractsMonitoringSource
{
    Task<ContractsMonitoringResult> LoadAsync(ContractsMonitoringQuery query, CancellationToken cancellationToken = default);
}

/// <summary>
/// Registered until the monitoring API is bound. It returns no data on purpose, so the running portal shows an honest
/// "not connected" state instead of sample contracts.
/// </summary>
public sealed class UnavailableContractsMonitoringSource : IContractsMonitoringSource
{
    public Task<ContractsMonitoringResult> LoadAsync(ContractsMonitoringQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(ContractsMonitoringResult.Fail(ContractsMonitoringFailure.Unavailable));
}

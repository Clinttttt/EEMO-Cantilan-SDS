using System.Globalization;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Monitoring;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Client.Services;

/// <summary>
/// The real source of the Contracts Monitoring page: <c>GET /api/monitoring/contracts</c> through the existing authenticated
/// <see cref="IReportsApiClient"/>. It translates the page's choices into the API request and the API's rows into the page's
/// rows, and nothing else - status, expiry, remaining days and every count are the server's and are never recomputed here.
/// </summary>
public sealed class ReportsContractsMonitoringSource(
    IReportsApiClient reports,
    FacilityState facilities,
    OperationNameService operationNames) : IContractsMonitoringSource
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    // Transaction facilities are not lease registers; the API would return nothing for them, so they are not offered.
    private static readonly FacilityCode[] NotMonitored = [FacilityCode.SLH, FacilityCode.TRM, FacilityCode.TPM];

    public async Task<ContractsMonitoringResult> LoadAsync(ContractsMonitoringQuery query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Both are loaded once per circuit and shared; they supply the municipality's own operation names for the filter.
        await facilities.EnsureLoadedAsync();
        await operationNames.EnsureLoadedAsync();
        cancellationToken.ThrowIfCancellationRequested();

        var result = await reports.GetContractsMonitoringAsync(ToRequest(query));
        cancellationToken.ThrowIfCancellationRequested();

        if (!result.IsSuccess || result.Value is null)
        {
            return ContractsMonitoringResult.Fail(result.StatusCode is 401 or 403
                ? ContractsMonitoringFailure.Unauthorized
                : ContractsMonitoringFailure.Error);
        }
        return ContractsMonitoringResult.Ok(ToPage(result.Value, OperationOptions()));
    }

    public static ContractsMonitoringRequest ToRequest(ContractsMonitoringQuery q) => new()
    {
        Search = string.IsNullOrWhiteSpace(q.Search) ? null : q.Search.Trim(),
        Operation = string.IsNullOrWhiteSpace(q.Operation) ? null : q.Operation,
        Status = string.IsNullOrWhiteSpace(q.Status) ? null : q.Status,
        OccupancyBasis = string.IsNullOrWhiteSpace(q.OccupancyBasis) ? null : q.OccupancyBasis,
        ExpiryWindow = q.ExpiryWindow,
        Page = q.Page,
        PageSize = q.PageSize
    };

    /// <summary>The operations the register can contain, from the tenant's own catalog - never from the rows on the current page.</summary>
    private IReadOnlyList<ContractsMonitoringOption> OperationOptions()
    {
        var options = facilities.All
            .Where(c => !NotMonitored.Contains(c))
            .Select(c => new ContractsMonitoringOption(c.ToString(), facilities.NameOf(c)))
            .ToList();
        // Same names the API prints on the rows: the municipality's own, else the established wording.
        options.Add(new("KM", operationNames.NameFor(CollectorOperationCodes.KanmanggaySpaceRental, "Kanmanggay")));
        options.Add(new("VF", operationNames.NameFor(CollectorOperationCodes.VegetableFruitSpaceRental, "Vegetable / Fruits")));
        return options;
    }

    public static ContractsMonitoringPage ToPage(ContractsMonitoringPageDto dto, IReadOnlyList<ContractsMonitoringOption> operations)
    {
        var s = dto.Summary;
        return new ContractsMonitoringPage(
            new ContractsMonitoringSummary(s.ActiveCount, s.ExpiringIn7DaysCount, s.ExpiringIn30DaysCount, s.ExpiredCount,
                s.NoContractCount, s.EndedCount, s.UnknownCount, s.NotYetEffectiveCount, s.TotalMonitoredOccupancies),
            dto.Items.Select(ToRow).ToList(),
            dto.TotalCount, dto.Page, dto.PageSize, operations);
    }

    public static ContractsMonitoringRow ToRow(ContractMonitoringRowDto r)
    {
        var occupant = Text(r.ActualOccupant) ?? Text(r.ContractName) ?? "Not recorded";
        var facts = new List<ContractsMonitoringFact>
        {
            new("Actual occupant", Text(r.ActualOccupant) ?? "Not recorded"),
            new("Name on contract", Text(r.ContractName) ?? "Not recorded"),
            new("Contract reference", Text(r.ContractReference) ?? "Not recorded"),
            new("Occupancy basis", BasisLabel(r.OccupancyBasis) ?? "Not recorded"),
            new("Occupancy started", Date(r.OccupancyStart)),
            new("Contract effectivity", Date(r.EffectivityDate)),
            new("Duration", DurationLabel(r) ?? "Not recorded"),
            new("Expiry", ExpiryLabel(r)),
            new("Approved charge", ApprovedCharge(r)),
        };
        // Shown only when the signed document states a different (or the only) figure; the approved charge is never replaced by it.
        if (r.ContractStatedAmount is { } stated && stated != r.ApprovedCharge)
            facts.Add(new("Amount stated in contract", Peso(stated) + FrequencySuffix(r.ChargeFrequency)));
        if (r.AreaSqm is not null || Text(r.AreaLocation) is not null)
            facts.Add(new("Area / location", string.Join(" · ", new[]
            {
                r.AreaSqm is { } a ? a.ToString("0.##", Invariant) + " sqm" : null, Text(r.AreaLocation)
            }.Where(x => x is not null))));
        if (r.Status == ContractMonitoringStatus.Ended)
            facts.Add(new("Closed on", r.ClosureDate is { } c ? Date(c) : "Not recorded"));
        facts.Add(new("Source record", $"{r.OperationDisplayName} · {SourceLabel(r)}"));

        return new ContractsMonitoringRow(
            Id: $"{r.SourceType}:{r.SourceRecordId}",
            Occupant: occupant,
            Operation: r.OperationDisplayName,
            Space: Text(r.SpaceNumber) ?? "Not recorded",
            ContractBasis: BasisLabel(r.OccupancyBasis),
            Duration: DurationLabel(r),
            Expiry: r.ExpiryDate,
            Status: StatusLabel(r.Status),
            RemainingDays: r.DaysUntilExpiry,
            Facts: facts,
            SourceHref: SourceHref(r),
            StatusKey: r.Status.ToString());
    }

    /// <summary>
    /// A direct route to the record only where the portal already has one: a facility stall's profile, or a Kanmanggay /
    /// Vegetable-Fruit account's profile. Anything else gets no link, and the drawer says so.
    /// </summary>
    public static string? SourceHref(ContractMonitoringRowDto r)
    {
        if (r.SourceType == ContractMonitoringSourceType.FacilityContract)
            return r.FacilityCode is { } code && r.StallId is { } stall && stall != Guid.Empty
                ? $"/profile/{code.ToString().ToLowerInvariant()}/{stall}"
                : null;
        return r.ObligationKind switch
        {
            ObligationKind.KanmanggaySpaceRental => $"/operations/kanmanggay/profile/{r.SourceRecordId}",
            ObligationKind.VegetableFruitSpaceRental => $"/operations/vegetable-fruit/profile/{r.SourceRecordId}",
            _ => null
        };
    }

    private static string SourceLabel(ContractMonitoringRowDto r) =>
        r.SourceType == ContractMonitoringSourceType.FacilityContract ? "Stall contract" : "Space account";

    private static string? Text(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    private static string Date(DateOnly? d) => d is { } v ? v.ToString("MMM d, yyyy", Invariant) : "Not recorded";
    private static string Peso(decimal v) => "₱" + v.ToString("N2", Invariant);

    private static string FrequencySuffix(ContractMonitoringChargeFrequency f) => f switch
    {
        ContractMonitoringChargeFrequency.Monthly => " / month",
        ContractMonitoringChargeFrequency.Daily => " / day",
        _ => string.Empty
    };

    private static string ApprovedCharge(ContractMonitoringRowDto r) =>
        r.ApprovedCharge is { } v ? Peso(v) + FrequencySuffix(r.ChargeFrequency)
        : r.ChargeFrequency == ContractMonitoringChargeFrequency.Daily ? "Charged daily; no monthly charge recorded"
        : "Not recorded";

    private static string? BasisLabel(OccupancyArrangement? b) => b switch
    {
        OccupancyArrangement.SignedContract => "Signed contract",
        OccupancyArrangement.SpaceOnly => "Space only",
        OccupancyArrangement.Extension => "Extension",
        null => null,
        _ => "Not recorded"
    };

    // Space-only and extension occupancy has no term by design; that is not missing evidence.
    private static string? DurationLabel(ContractMonitoringRowDto r) =>
        r.DurationYears is { } y ? (y == 1 ? "1 year" : y.ToString(Invariant) + " years")
        : r.Status == ContractMonitoringStatus.NoContract ? "No contract term"
        : null;

    private static string ExpiryLabel(ContractMonitoringRowDto r) =>
        r.ExpiryDate is { } d ? Date(d) : r.Status == ContractMonitoringStatus.NoContract ? "No expiry" : "Not recorded";

    private static string StatusLabel(ContractMonitoringStatus s) => s switch
    {
        ContractMonitoringStatus.Active => "Active",
        ContractMonitoringStatus.ExpiringSoon => "Expiring soon",
        ContractMonitoringStatus.Expired => "Expired",
        ContractMonitoringStatus.NoContract => "No contract",
        ContractMonitoringStatus.Ended => "Ended",
        ContractMonitoringStatus.NotYetEffective => "Not yet effective",
        _ => "Unknown"
    };
}

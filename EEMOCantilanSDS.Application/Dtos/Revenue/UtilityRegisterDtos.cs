using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Revenue;

/// <summary>
/// One page of the ECF / WCF source register for a billing month. Filtering, the exact total and the paging are all done by the
/// server; <see cref="Status"/> is one of "Unprepared", "Unpaid", "Partial", "Settled", "Direct" or "NeedsReview". The facility
/// and section are applied before the header totals; search and status narrow the rows only.
/// </summary>
public sealed record UtilityRegisterRequest(UtilityKind Kind, int Year, int Month, FacilityCode? Facility = null,
    string? Section = null, string? Status = null, string? Search = null, int Skip = 0, int Take = 25);

/// <summary>
/// One utility source (a market space with a verified service) and where its period stands. <see cref="Mode"/> is "Prepared"
/// (the office approved an amount), "Direct" (the period was frozen as direct collection) or "None" (nothing yet). A prepared
/// source has an <see cref="Approved"/> amount and an <see cref="Outstanding"/> balance; a direct one has neither - only the
/// money actually <see cref="Collected"/>. Nothing here is a Collection: the money is the canonical Collections' alone.
/// </summary>
public sealed record UtilityRegisterRowDto(Guid StallId, Guid? UtilityBillId, string StallNo, FacilityCode Facility, string FacilityName,
    string Section, string? Payer, decimal? Approved, decimal Collected, decimal Outstanding, string Mode, string Status,
    SettlementAuthority Authority, long SourceVersion, bool CanPrepare, string? PrepareLock,
    // A collector's direct amount is not settled here until the Head confirms it: <see cref="Collected"/> excludes it and this carries it.
    decimal AwaitingConfirmation = 0m);

/// <summary>
/// Period totals for the selected facility scope. <see cref="Assessed"/> counts approved amounts only; <see cref="Collected"/> is money
/// actually applied to the period (canonical Collections net of corrections, plus legacy-recorded payment on a legacy part);
/// <see cref="Outstanding"/> is the unpaid balance of prepared amounts; <see cref="DirectCollected"/> is the part of Collected that
/// had no approved amount behind it.
/// </summary>
public sealed record UtilityRegisterTotalsDto(decimal Assessed, decimal Collected, decimal Outstanding, decimal DirectCollected, int Sources,
    decimal AwaitingConfirmation = 0m);

public sealed record UtilityFacilityOptionDto(FacilityCode Code, string Name);

public sealed record UtilityRegisterPageDto(IReadOnlyList<UtilityRegisterRowDto> Rows, int Total, UtilityRegisterTotalsDto Totals,
    IReadOnlyList<UtilityFacilityOptionDto> Facilities, IReadOnlyList<string> Sections);

/// <summary>The read side of the utility workspaces: bounded, server-filtered, tenant-scoped, office only.</summary>
public interface IUtilityRegisterReader
{
    Task<Result<UtilityRegisterPageDto>> GetAsync(UtilityRegisterRequest request, CancellationToken ct = default);
}

/// <summary>One approved amount for one source of the period. <see cref="ExpectedVersion"/> is the source version the register showed.</summary>
public sealed record UtilityPrepareItem(Guid StallId, decimal ApprovedAmount, long? ExpectedVersion = null);

/// <summary>Prepares approved amounts for several sources of one billing month. Independent amounts: nothing is split or totalled across payors.</summary>
public sealed record UtilityPrepareRequest(UtilityKind Kind, int Year, int Month, IReadOnlyList<UtilityPrepareItem> Items);

/// <summary>What happened to one source. A refused source says why and changes nothing.</summary>
public sealed record UtilityPrepareOutcome(Guid StallId, bool Prepared, string? Reason);

public sealed record UtilityPrepareResult(IReadOnlyList<UtilityPrepareOutcome> Outcomes)
{
    public int PreparedCount => Outcomes.Count(x => x.Prepared);
    public int RefusedCount => Outcomes.Count(x => !x.Prepared);
}

using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Lifecycle;

// Read-only lifecycle adapters. They read each source's own records through the same tenant-scoped repositories and context the existing
// commands use, and they write nothing. Mutations stay on the existing source endpoints until replay, concurrency and version handling
// have a safe design (see SHARED_ACCOUNT_LIFECYCLE_ARCHITECTURE_20261020.md). This file is on the Application EF-boundary list on
// purpose: the section-closure and obligation-account reads have no repository seam.

/// <summary>Permanent stalls (NPM, NCC, TCC, ...): a physical Stall with Contract terms. Wraps the current lifecycle; changes none of it.</summary>
public sealed class StallLifecycle(
    IStallRepository stalls,
    IAppDbContext context,
    IClock clock,
    NpmRentMonthWorkflow? monthly = null) : IAccountLifecycle
{
    public bool Handles(LifecycleSource source) => source == LifecycleSource.Stall;

    public async Task<AccountLifecycleDto?> GetAsync(LifecycleSource source, Guid accountId, string? actorRole, CancellationToken ct)
    {
        // The stall repository reads through the tenant and soft-delete query filters: another municipality's stall is simply not found.
        var stall = await stalls.GetByIdWithContractsAsync(accountId, ct);
        if (stall is null) return null;
        var facility = await stalls.GetFacilityCodeByStallIdAsync(accountId, ct);
        var today = clock.PhilippineToday;

        var contracts = stall.Contracts.OrderBy(c => c.EffectivityDate).ThenBy(c => c.CreatedAt).ToList();
        var current = contracts.LastOrDefault(c => c.IsActive) ?? contracts.LastOrDefault();
        var closed = stall.Status == StallStatus.Closed;
        var expired = stall.IsContractExpired(today);

        var sectionClosed = false;
        if (closed && !string.IsNullOrWhiteSpace(stall.CustomSectionName))
        {
            // The same check ToggleStallStatus makes before it resumes a stall.
            sectionClosed = await context.FacilitySectionClosures.AsNoTracking()
                .AnyAsync(c => c.FacilityCode == FacilityCode.NPM && c.SectionName.ToLower() == stall.CustomSectionName!.Trim().ToLower(), ct);
        }
        var evidence = monthly is not null && await monthly.HasActivatedStallAsync(stall.Id, ct);

        // Re-let: a past (inactive) term exists and somebody else's occupancy is in force now: the register's StallReLet.
        var reLet = contracts.Any(c => !c.IsActive) && stall.Occupancies(today).Any(o => o.IsCurrent && o.Contract.IsActive);
        var facts = new StallLifecycleFacts(closed, expired, reLet, sectionClosed, evidence);

        var history = new List<LifecycleHistoryEntry>();
        foreach (var c in contracts)
        {
            history.Add(new(c.EffectivityDate, "TermStarted", $"{c.ActualOccupant} · {c.DurationYears} year{(c.DurationYears == 1 ? "" : "s")}"));
            if (c.EndedOn is { } ended) history.Add(new(ended, "TermEnded", c.ActualOccupant));
        }
        if (stall.ClosedAt is { } closedOn && closed) history.Add(new(closedOn, "Closed", "Account closed"));

        return new AccountLifecycleDto(LifecycleSource.Stall, stall.Id, stall.StallNo, facility?.ToString() ?? string.Empty, current?.ActualOccupant,
            closed ? LifecycleState.Closed : expired ? LifecycleState.TermExpired : LifecycleState.Active,
            current?.EffectivityDate, closed ? stall.ClosedAt : current?.EndedOn,
            contracts.LastOrDefault(c => c.IsActive)?.ExpiryDate, LifecycleRules.ForStall(facts, actorRole),
            history.OrderBy(h => h.Date).ToList());
    }
}

/// <summary>
/// Kanmanggay and Vegetable/Fruit monthly space accounts, and Fiesta/Araw event lots, from <see cref="ObligationAccount"/>.
/// Read-only: Close is reported where the source workflow allows it; Resume/Renew/Reassign/Remove are reported unavailable.
/// </summary>
public sealed class SpaceAccountLifecycle(IAppDbContext context, ICurrentMunicipalityAccessor municipality, SpaceLifecycleWorkflow workflow)
    : IAccountLifecycle, IAccountLifecycleExecutor
{
    public bool Handles(LifecycleSource source) => source is LifecycleSource.SpaceRental or LifecycleSource.EventLot;

    // Execution lives in the workflow; this adapter only routes a source that is really a monthly space to it, so an event lot can never be
    // resumed or renewed through the shared contract.
    public async Task<Result<LifecyclePreviewDto>> PreviewAsync(LifecycleSource source, Guid accountId, LifecycleCommand command, CancellationToken ct) =>
        await IsOwner(source, accountId, ct) ? await workflow.PreviewAsync(accountId, command, ct) : Result<LifecyclePreviewDto>.NotFound();

    public async Task<Result<LifecycleOutcome>> ExecuteAsync(LifecycleSource source, Guid accountId, LifecycleExecution execution, CancellationToken ct) =>
        await IsOwner(source, accountId, ct) ? await workflow.ExecuteAsync(accountId, execution, ct) : Result<LifecycleOutcome>.NotFound();

    private async Task<bool> IsOwner(LifecycleSource source, Guid accountId, CancellationToken ct)
    {
        var tenant = municipality.MunicipalityId;
        if (tenant == Guid.Empty) return false;
        var kind = await context.ObligationAccounts.AsNoTracking().Where(x => x.MunicipalityId == tenant && x.Id == accountId).Select(x => (ObligationKind?)x.Kind).SingleOrDefaultAsync(ct);
        return kind is { } k && (k == ObligationKind.FiestaArawLotRental ? LifecycleSource.EventLot : ObligationAccount.IsMonthlySpaceKind(k) ? LifecycleSource.SpaceRental : (LifecycleSource?)null) == source;
    }

    public async Task<AccountLifecycleDto?> GetAsync(LifecycleSource source, Guid accountId, string? actorRole, CancellationToken ct)
    {
        var tenant = municipality.MunicipalityId;
        if (tenant == Guid.Empty) return null;
        var account = await context.ObligationAccounts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.MunicipalityId == tenant && x.Id == accountId, ct);
        if (account is null) return null;

        // Only the space kinds have a lifecycle; a Fish/Meat vendor fee is anchored to an NPM stall and is not one. The requested source
        // must also match the account's own kind, so an event lot can never be read (or later renewed) as a monthly lease.
        var owner = account.Kind == ObligationKind.FiestaArawLotRental ? LifecycleSource.EventLot
            : ObligationAccount.IsMonthlySpaceKind(account.Kind) ? LifecycleSource.SpaceRental : (LifecycleSource?)null;
        if (owner is null || owner != source) return null;

        var view = await workflow.FactsAsync(account.Id, ct);
        if (view is not { } known) return null;
        var history = new List<LifecycleHistoryEntry>
        {
            new(account.ActiveFrom, owner == LifecycleSource.EventLot ? "EventDate" : "AccountOpened", account.ActualOccupant ?? "Account opened")
        };
        // Descriptive lease evidence only: it never bills, renews or ends the account.
        DateOnly? termExpiry = null;
        if (owner == LifecycleSource.SpaceRental && account.ContractEffectiveDate is { } effective && account.ContractYears is { } years)
        {
            history.Add(new(effective, "ContractRecorded", $"{years} year{(years == 1 ? "" : "s")} (descriptive)"));
            termExpiry = DomainRules.TermLastDay(effective, years);
        }
        if (account.ActiveTo is { } to) history.Add(new(to, to >= known.Facts.Today ? "Ends" : "Closed", to >= known.Facts.Today ? "Account ends" : "Account closed"));

        if (owner == LifecycleSource.SpaceRental)
        {
            // Lifecycle evidence of this occupancy (successor links, renewals, retirement), in the same timeline.
            var events = await context.SpaceLifecycleEvents.AsNoTracking().Where(x => x.MunicipalityId == tenant && (x.SourceAccountId == account.Id || x.ResultAccountId == account.Id))
                .OrderBy(x => x.RecordedAtUtc).ToListAsync(ct);
            foreach (var e in events)
                history.Add(new(e.EffectiveDate, e.Action.ToString(), e.ResultAccountId == account.Id ? "Began as the successor of the previous occupancy" : e.Reason));
        }
        var state = RentalAccountStates.Classify(account.ActiveTo, known.Facts.Retired, known.Facts.HasSuccessor, known.Facts.TermLastDay, known.Facts.Today) switch
        {
            RentalAccountState.Retired => LifecycleState.Retired, RentalAccountState.Superseded => LifecycleState.Superseded, RentalAccountState.Closed => LifecycleState.Closed,
            RentalAccountState.Ending => LifecycleState.Ending, RentalAccountState.TermExpired => LifecycleState.TermExpired, _ => LifecycleState.Active
        };
        return new AccountLifecycleDto(owner.Value, account.Id, account.SubjectLabel, account.Kind.ToString(), known.Holder,
            state, account.ActiveFrom, account.ActiveTo, termExpiry, LifecycleRules.ForSpace(known.Facts, actorRole), history.OrderBy(h => h.Date).ToList());
    }
}

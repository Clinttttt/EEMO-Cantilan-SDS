using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>The governed configurable services (IA-044/ADR-006) and their fixed, non-collector-defined identity.</summary>
public static class GovernedServiceCatalog
{
    public sealed record Entry(
        string Code, string Name, string ClassificationCode, bool ModeAware,
        IReadOnlyList<GovernedServiceBasis> AllowedBases);

    private static readonly GovernedServiceBasis[] Both =
        [GovernedServiceBasis.FixedAmount, GovernedServiceBasis.DirectApprovedAmount];

    // Services whose office may collect more than one approved fee under the same classification (Market Fees: e.g. a
    // comfort room at a named location; Transfer Large Cattle: more than one approved amount). The option is selected,
    // never typed; classification and instrument stay the service's.
    private static readonly GovernedServiceBasis[] BothOrOptions =
        [GovernedServiceBasis.FixedAmount, GovernedServiceBasis.DirectApprovedAmount, GovernedServiceBasis.ApprovedFeeOption];

    public static readonly IReadOnlyList<Entry> All =
    [
        new(CollectorOperationCodes.MarketFees, "Market Fees", RevenueClassificationCodes.MarketFees, false, BothOrOptions),
        new(CollectorOperationCodes.LandingBerthing, "Landing / Berthing", RevenueClassificationCodes.LandingBerthing, false, Both),
        new(CollectorOperationCodes.TransferLargeCattle, "Transfer Large Cattle", RevenueClassificationCodes.TransferLargeCattle, false, BothOrOptions),
        // A whole payment and a daily transaction are different amounts under different instruments; one fixed amount
        // cannot describe both, so this service records an approved direct amount (with an optional ceiling).
        new(CollectorOperationCodes.VegetableFruitSpaceRental, "Vegetable / Fruit Space Rental",
            RevenueClassificationCodes.VegetableFruitSpaceRental, true, [GovernedServiceBasis.DirectApprovedAmount]),
        // Transportation / Parking (IA-030, IA-050): a Cash Ticket day-to-day collection whose amount is the approved,
        // effective-dated rate of the vehicle class the collector selects. The rate table is Head-configured.
        new(CollectorOperationCodes.Transportation, "Transportation / Parking", RevenueClassificationCodes.TransportationParking,
            false, [GovernedServiceBasis.VehicleClassRate]),
        // Tabo and the current Slaughterhouse transaction already have an office-defined amount (vendor market-day fee;
        // approved per-head rate x heads). The governed setting is only their prospective canonical-collection switch; the
        // amount is never read from it or typed (FeeScheduleCollectionWorkflow computes it from the existing rules).
        new(CollectorOperationCodes.Tabo, "Tabo", RevenueClassificationCodes.Tabo, false, [GovernedServiceBasis.DirectApprovedAmount]),
        new(CollectorOperationCodes.Slaughterhouse, "Slaughterhouse", RevenueClassificationCodes.Slaughterhouse, false,
            [GovernedServiceBasis.DirectApprovedAmount]),
        // The NPM daily stall fee keeps its existing stall-rent classification and its own market rules (occupancy, closures,
        // rent goal, month-end adjustment). The setting is only the prospective switch that makes its money canonical.
        new(CollectorOperationCodes.NpmDaily, "NPM daily stall fee", RevenueClassificationCodes.PermanentStallRent, false,
            [GovernedServiceBasis.DirectApprovedAmount]),
    ];

    public static Entry? Find(string? code) => All.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.Ordinal));

    public static RevenuePolicyContext ContextFor(Entry entry, GovernedServiceMode? mode) => !entry.ModeAware
        ? RevenuePolicyContext.Default
        : mode == GovernedServiceMode.WholePayment ? RevenuePolicyContext.VegetableWholePayment
        : RevenuePolicyContext.VegetableDailyTransaction;
}

/// <summary>
/// Head/Admin setup, Collector Mobile posting and office activity for the governed configurable services. The
/// collector states transaction facts only; classification, instrument, amount rule and document requirements are
/// resolved from approved tenant configuration, and money is written by the one canonical posting coordinator.
/// </summary>
public sealed class GovernedServiceWorkflow(
    IAppDbContext db,
    ICurrentUserService currentUser,
    ICurrentMunicipalityAccessor municipality,
    IClock? clock = null)
{
    private const string MobileOrigin = "MobileGovernedService";
    private const int IntentVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private DateOnly BusinessToday => clock?.PhilippineToday ?? PhilippineTime.Today;
    private DateTime UtcNow => clock?.UtcNow ?? DateTime.UtcNow;

    // ── Setup (Head/Admin read, Head configure) ────────────────────────────────────────────────────────

    public Task<Result<IReadOnlyList<GovernedServiceDefinitionDto>>> GetDefinitionsAsync(CancellationToken ct = default) =>
        Run<IReadOnlyList<GovernedServiceDefinitionDto>>(async actor =>
        {
            if (actor.Role == "Collector") return Result<IReadOnlyList<GovernedServiceDefinitionDto>>.Forbidden();
            var list = new List<GovernedServiceDefinitionDto>();
            foreach (var entry in GovernedServiceCatalog.All)
                list.Add(await BuildDefinitionAsync(actor.TenantId, entry, ct));
            return Result<IReadOnlyList<GovernedServiceDefinitionDto>>.Success(list);
        }, ct);

    public Task<Result<GovernedServiceDefinitionDto>> ConfigureAsync(
        string operationCode, ConfigureGovernedServiceRequest request, CancellationToken ct = default) =>
        Run<GovernedServiceDefinitionDto>(async actor =>
        {
            // Configuration authority is the Head's (as for revenue classification policy); Admin reads only.
            if (actor.Role != "SuperAdmin") return Result<GovernedServiceDefinitionDto>.Forbidden();
            var entry = GovernedServiceCatalog.Find(operationCode);
            if (entry is null)
                return Result<GovernedServiceDefinitionDto>.Failure("This operation is not a governed configurable service.", ResultStatus.NotFound);
            if (!entry.AllowedBases.Contains(request.Basis))
                return Result<GovernedServiceDefinitionDto>.Failure("This service does not support the chosen amount basis.", ResultStatus.Invalid);
            if (request.EffectiveDate < new DateOnly(2020, 1, 1) || request.EffectiveDate > BusinessToday.AddDays(366))
                return Result<GovernedServiceDefinitionDto>.Failure("Choose a realistic effective date (not more than a year ahead).", ResultStatus.Invalid);

            var service = await db.GovernedServices.SingleOrDefaultAsync(x =>
                x.MunicipalityId == actor.TenantId && x.OperationCode == entry.Code, ct);
            GovernedServiceSetting setting;
            try
            {
                if (service is null)
                {
                    service = GovernedService.Create(actor.TenantId, entry.Code, actor.Username, UtcNow);
                    db.GovernedServices.Add(service);
                }
                setting = GovernedServiceSetting.Create(actor.TenantId, service.Id, request.EffectiveDate, request.Basis,
                    request.FixedAmount, request.MaximumAmount, request.IsEnabled, request.MobileEnabled, actor.Username, UtcNow);
            }
            catch (ArgumentException ex)
            {
                db.ChangeTracker.Clear();
                return Result<GovernedServiceDefinitionDto>.Failure(ex.Message, ResultStatus.Invalid);
            }
            db.GovernedServiceSettings.Add(setting);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                return Result<GovernedServiceDefinitionDto>.Failure("The service setup changed concurrently. Reload and try again.", ResultStatus.Conflict);
            }
            return Result<GovernedServiceDefinitionDto>.Success(await BuildDefinitionAsync(actor.TenantId, entry, ct));
        }, ct);

    /// <summary>The active vehicle classes that have an approved rate in force on a date, with that rate. A class without one is not offered.</summary>
    private async Task<IReadOnlyList<VehicleClassTermDto>> CurrentVehicleClassTermsAsync(Guid tenantId, DateOnly date, CancellationToken ct)
    {
        var classes = await db.VehicleClasses.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && x.IsActive).ToListAsync(ct);
        if (classes.Count == 0) return [];
        var ids = classes.Select(x => x.Id).ToArray();
        var rates = (await db.VehicleClassRates.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && ids.Contains(x.VehicleClassId)).ToListAsync(ct))
            .ToLookup(x => x.VehicleClassId);
        return classes.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(c => (Class: c, Rate: VehicleClassRate.Resolve(rates[c.Id], date)))
            .Where(x => x.Rate is not null)
            .Select(x => new VehicleClassTermDto(x.Class.Code, x.Class.DisplayName, x.Rate!.Amount)).ToList();
    }

    /// <summary>"Comfort Room — Transport Terminal": the option's name with its location, when it has one.</summary>
    private static string FeeOptionLabel(GovernedServiceFeeOption option) =>
        string.IsNullOrWhiteSpace(option.Location) || option.DisplayName.Contains(option.Location, StringComparison.OrdinalIgnoreCase)
            ? option.DisplayName
            : $"{option.DisplayName} — {option.Location}";

    /// <summary>The fee options offered on a date that have an approved rule in force then. An option without one is not offered.</summary>
    private async Task<IReadOnlyList<FeeOptionTermDto>> CurrentFeeOptionTermsAsync(Guid tenantId, Guid serviceId, DateOnly date, CancellationToken ct)
    {
        var options = (await db.GovernedServiceFeeOptions.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && x.GovernedServiceId == serviceId).ToListAsync(ct))
            .Where(x => x.IsOfferedOn(date)).ToList();
        if (options.Count == 0) return [];
        var ids = options.Select(x => x.Id).ToArray();
        var rates = (await db.GovernedServiceFeeOptionRates.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && ids.Contains(x.FeeOptionId)).ToListAsync(ct)).ToLookup(x => x.FeeOptionId);
        return options.Select(o => (Option: o, Rate: GovernedServiceFeeOptionRate.Resolve(rates[o.Id], date)))
            .Where(x => x.Rate is not null)
            .OrderBy(x => FeeOptionLabel(x.Option), StringComparer.OrdinalIgnoreCase)
            .Select(x => new FeeOptionTermDto(x.Option.Id, x.Option.DisplayName, x.Option.Location, x.Rate!.Basis,
                x.Rate.FixedAmount, x.Rate.MaximumAmount)).ToList();
    }

    // ── Approved fee options (Head configures; Head/Admin read) ─────────────────────────────────────────

    public Task<Result<IReadOnlyList<GovernedServiceFeeOptionDto>>> GetFeeOptionsAsync(string operationCode, CancellationToken ct = default) =>
        Run<IReadOnlyList<GovernedServiceFeeOptionDto>>(async actor =>
        {
            if (actor.Role == "Collector") return Result<IReadOnlyList<GovernedServiceFeeOptionDto>>.Forbidden();
            var entry = GovernedServiceCatalog.Find(operationCode);
            if (entry is null || !entry.AllowedBases.Contains(GovernedServiceBasis.ApprovedFeeOption))
                return Result<IReadOnlyList<GovernedServiceFeeOptionDto>>.Failure("This service has no fee options.", ResultStatus.NotFound);
            var service = await db.GovernedServices.AsNoTracking().SingleOrDefaultAsync(x =>
                x.MunicipalityId == actor.TenantId && x.OperationCode == entry.Code, ct);
            if (service is null) return Result<IReadOnlyList<GovernedServiceFeeOptionDto>>.Success([]);
            return Result<IReadOnlyList<GovernedServiceFeeOptionDto>>.Success(await FeeOptionDtosAsync(actor.TenantId, service.Id, ct));
        }, ct);

    public Task<Result<IReadOnlyList<GovernedServiceFeeOptionDto>>> AddFeeOptionAsync(
        string operationCode, AddFeeOptionRequest request, CancellationToken ct = default) =>
        Run<IReadOnlyList<GovernedServiceFeeOptionDto>>(async actor =>
        {
            if (actor.Role != "SuperAdmin") return Result<IReadOnlyList<GovernedServiceFeeOptionDto>>.Forbidden();
            var entry = GovernedServiceCatalog.Find(operationCode);
            if (entry is null || !entry.AllowedBases.Contains(GovernedServiceBasis.ApprovedFeeOption))
                return Result<IReadOnlyList<GovernedServiceFeeOptionDto>>.Failure("This service has no fee options.", ResultStatus.NotFound);
            if (EffectiveDateProblem(request.EffectiveDate) is { } dateProblem)
                return Result<IReadOnlyList<GovernedServiceFeeOptionDto>>.Failure(dateProblem, ResultStatus.Invalid);
            var service = await db.GovernedServices.SingleOrDefaultAsync(x =>
                x.MunicipalityId == actor.TenantId && x.OperationCode == entry.Code, ct);
            try
            {
                if (service is null)
                {
                    service = GovernedService.Create(actor.TenantId, entry.Code, actor.Username, UtcNow);
                    db.GovernedServices.Add(service);
                }
                var option = GovernedServiceFeeOption.Create(actor.TenantId, service.Id, request.DisplayName, request.Code,
                    request.Location, request.Description, actor.Username, UtcNow);
                var name = option.DisplayName;
                var location = option.Location;
                if (await db.GovernedServiceFeeOptions.AnyAsync(x => x.MunicipalityId == actor.TenantId
                        && x.GovernedServiceId == service.Id && x.DisplayName == name && x.Location == location, ct))
                {
                    db.ChangeTracker.Clear();
                    return Result<IReadOnlyList<GovernedServiceFeeOptionDto>>.Failure("A fee option with this name and location already exists.", ResultStatus.Conflict);
                }
                db.GovernedServiceFeeOptions.Add(option);
                db.GovernedServiceFeeOptionRates.Add(GovernedServiceFeeOptionRate.Create(actor.TenantId, option.Id, request.EffectiveDate,
                    request.Basis, request.FixedAmount, request.MaximumAmount, actor.Username, UtcNow));
            }
            catch (ArgumentException ex)
            {
                db.ChangeTracker.Clear();
                return Result<IReadOnlyList<GovernedServiceFeeOptionDto>>.Failure(ex.Message, ResultStatus.Invalid);
            }
            await db.SaveChangesAsync(ct);
            return Result<IReadOnlyList<GovernedServiceFeeOptionDto>>.Success(await FeeOptionDtosAsync(actor.TenantId, service.Id, ct));
        }, ct);

    public Task<Result<IReadOnlyList<GovernedServiceFeeOptionDto>>> ScheduleFeeOptionRateAsync(
        string operationCode, Guid feeOptionId, ScheduleFeeOptionRateRequest request, CancellationToken ct = default) =>
        Run<IReadOnlyList<GovernedServiceFeeOptionDto>>(async actor =>
        {
            if (actor.Role != "SuperAdmin") return Result<IReadOnlyList<GovernedServiceFeeOptionDto>>.Forbidden();
            var (service, option, problem) = await FindFeeOptionAsync(actor, operationCode, feeOptionId, tracked: false, ct);
            if (problem is not null) return Result<IReadOnlyList<GovernedServiceFeeOptionDto>>.Failure(problem, ResultStatus.NotFound);
            // A new rule applies from today or later: a rule already in force (and every amount posted under it) is never rewritten.
            if (request.EffectiveDate < BusinessToday || EffectiveDateProblem(request.EffectiveDate) is not null)
                return Result<IReadOnlyList<GovernedServiceFeeOptionDto>>.Failure(
                    "Schedule the change from today or a later date (not more than a year ahead).", ResultStatus.Invalid);
            if (option!.RetiredFrom is { } retired && request.EffectiveDate >= retired)
                return Result<IReadOnlyList<GovernedServiceFeeOptionDto>>.Failure("The option is retired from that date.", ResultStatus.Conflict);
            if (await db.GovernedServiceFeeOptionRates.AnyAsync(x => x.MunicipalityId == actor.TenantId
                    && x.FeeOptionId == option.Id && x.EffectiveDate == request.EffectiveDate, ct))
                return Result<IReadOnlyList<GovernedServiceFeeOptionDto>>.Failure(
                    "A rule is already scheduled for that date. Choose another date.", ResultStatus.Conflict);
            try
            {
                db.GovernedServiceFeeOptionRates.Add(GovernedServiceFeeOptionRate.Create(actor.TenantId, option.Id, request.EffectiveDate,
                    request.Basis, request.FixedAmount, request.MaximumAmount, actor.Username, UtcNow));
            }
            catch (ArgumentException ex)
            {
                db.ChangeTracker.Clear();
                return Result<IReadOnlyList<GovernedServiceFeeOptionDto>>.Failure(ex.Message, ResultStatus.Invalid);
            }
            await db.SaveChangesAsync(ct);
            return Result<IReadOnlyList<GovernedServiceFeeOptionDto>>.Success(await FeeOptionDtosAsync(actor.TenantId, service!.Id, ct));
        }, ct);

    public Task<Result<IReadOnlyList<GovernedServiceFeeOptionDto>>> RetireFeeOptionAsync(
        string operationCode, Guid feeOptionId, RetireFeeOptionRequest request, CancellationToken ct = default) =>
        Run<IReadOnlyList<GovernedServiceFeeOptionDto>>(async actor =>
        {
            if (actor.Role != "SuperAdmin") return Result<IReadOnlyList<GovernedServiceFeeOptionDto>>.Forbidden();
            var (service, option, problem) = await FindFeeOptionAsync(actor, operationCode, feeOptionId, tracked: true, ct);
            if (problem is not null) return Result<IReadOnlyList<GovernedServiceFeeOptionDto>>.Failure(problem, ResultStatus.NotFound);
            try { option!.Retire(request.FromDate, BusinessToday, actor.Username); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                db.ChangeTracker.Clear();
                return Result<IReadOnlyList<GovernedServiceFeeOptionDto>>.Failure(ex.Message, ResultStatus.Invalid);
            }
            await db.SaveChangesAsync(ct);
            return Result<IReadOnlyList<GovernedServiceFeeOptionDto>>.Success(await FeeOptionDtosAsync(actor.TenantId, service!.Id, ct));
        }, ct);

    /// <summary>
    /// Posted money of one service in a business-date period, by the fee option recorded on each line (the frozen snapshot,
    /// never the option's current name or amount). The sum equals the service's total: each line is counted once.
    /// </summary>
    public Task<Result<IReadOnlyList<FeeOptionTotalDto>>> GetFeeOptionTotalsAsync(
        string operationCode, DateOnly from, DateOnly to, CancellationToken ct = default) =>
        Run<IReadOnlyList<FeeOptionTotalDto>>(async actor =>
        {
            if (actor.Role == "Collector") return Result<IReadOnlyList<FeeOptionTotalDto>>.Forbidden();
            var entry = GovernedServiceCatalog.Find(operationCode);
            if (entry is null) return Result<IReadOnlyList<FeeOptionTotalDto>>.Failure("Unknown service.", ResultStatus.NotFound);
            if (from > to || to.DayNumber - from.DayNumber > 366)
                return Result<IReadOnlyList<FeeOptionTotalDto>>.Failure("Choose a valid period of no more than 367 days.", ResultStatus.Invalid);
            var service = await db.GovernedServices.AsNoTracking().SingleOrDefaultAsync(x =>
                x.MunicipalityId == actor.TenantId && x.OperationCode == entry.Code, ct);
            if (service is null) return Result<IReadOnlyList<FeeOptionTotalDto>>.Success([]);
            var lines = await (
                from line in db.CollectionLines.AsNoTracking()
                join collection in db.Collections.AsNoTracking()
                    on new { line.MunicipalityId, Id = line.CollectionId } equals new { collection.MunicipalityId, collection.Id }
                where line.MunicipalityId == actor.TenantId && line.SourceKind == CollectionSourceKind.GovernedService
                    && line.SourceId == service.Id && collection.BusinessDate >= @from && collection.BusinessDate <= to
                select new { line.CalculationSnapshot, line.Amount }).ToListAsync(ct);
            var totals = lines.Select(x => (Facts: ReadSnapshot(x.CalculationSnapshot), x.Amount))
                .GroupBy(x => x.Facts?.FeeOptionId)
                .Select(g => new FeeOptionTotalDto(g.Key,
                    g.Key is null ? $"{entry.Name} (no fee option recorded)"
                        : g.Select(x => x.Facts?.FeeOptionName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "Fee option",
                    g.Count(), g.Sum(x => x.Amount)))
                .OrderByDescending(x => x.Amount).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
            return Result<IReadOnlyList<FeeOptionTotalDto>>.Success(totals);
        }, ct);

    private string? EffectiveDateProblem(DateOnly date) =>
        date < new DateOnly(2020, 1, 1) || date > BusinessToday.AddDays(366)
            ? "Choose a realistic effective date (not more than a year ahead)." : null;

    private async Task<(GovernedService? Service, GovernedServiceFeeOption? Option, string? Problem)> FindFeeOptionAsync(
        Actor actor, string operationCode, Guid feeOptionId, bool tracked, CancellationToken ct)
    {
        var entry = GovernedServiceCatalog.Find(operationCode);
        if (entry is null || !entry.AllowedBases.Contains(GovernedServiceBasis.ApprovedFeeOption))
            return (null, null, "This service has no fee options.");
        var service = await db.GovernedServices.AsNoTracking().SingleOrDefaultAsync(x =>
            x.MunicipalityId == actor.TenantId && x.OperationCode == entry.Code, ct);
        if (service is null) return (null, null, "The fee option was not found.");
        var query = tracked ? db.GovernedServiceFeeOptions : db.GovernedServiceFeeOptions.AsNoTracking();
        var option = await query.SingleOrDefaultAsync(x =>
            x.MunicipalityId == actor.TenantId && x.GovernedServiceId == service.Id && x.Id == feeOptionId, ct);
        return option is null ? (service, null, "The fee option was not found.") : (service, option, null);
    }

    private async Task<IReadOnlyList<GovernedServiceFeeOptionDto>> FeeOptionDtosAsync(Guid tenantId, Guid serviceId, CancellationToken ct)
    {
        var today = BusinessToday;
        var options = await db.GovernedServiceFeeOptions.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && x.GovernedServiceId == serviceId).ToListAsync(ct);
        var ids = options.Select(x => x.Id).ToArray();
        var rates = (await db.GovernedServiceFeeOptionRates.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && ids.Contains(x.FeeOptionId)).ToListAsync(ct)).ToLookup(x => x.FeeOptionId);
        return options.Select(o =>
        {
            var current = GovernedServiceFeeOptionRate.Resolve(rates[o.Id], today);
            var status = !o.IsOfferedOn(today) ? "Retired"
                : current is null ? (rates[o.Id].Any() ? "Scheduled" : "No amount rule")
                : o.RetiredFrom is not null ? "Retiring" : "Active";
            var history = rates[o.Id].OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.CreatedAtUtc)
                .Select(x => new FeeOptionRateVersionDto(x.EffectiveDate, x.Basis, x.FixedAmount, x.MaximumAmount, x.CreatedBy, x.CreatedAtUtc))
                .ToList();
            return new GovernedServiceFeeOptionDto(o.Id, o.Code, o.DisplayName, o.Location, o.Description,
                current?.Basis, current?.FixedAmount, current?.MaximumAmount, current?.EffectiveDate, status, o.RetiredFrom, history, o.RetiredBy);
        }).OrderBy(x => x.Status == "Retired").ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Location).ToList();
    }

    private async Task<GovernedServiceDefinitionDto> BuildDefinitionAsync(
        Guid tenantId, GovernedServiceCatalog.Entry entry, CancellationToken ct)
    {
        var today = BusinessToday;
        var service = await db.GovernedServices.AsNoTracking().SingleOrDefaultAsync(x =>
            x.MunicipalityId == tenantId && x.OperationCode == entry.Code, ct);
        GovernedServiceSetting? setting = null;
        if (service is not null)
        {
            var versions = await db.GovernedServiceSettings.AsNoTracking()
                .Where(x => x.MunicipalityId == tenantId && x.GovernedServiceId == service.Id).ToListAsync(ct);
            setting = GovernedServiceSetting.Resolve(versions, today);
        }

        var issues = new List<string>();
        var instruments = new List<GovernedServiceInstrumentDto>();
        var modes = entry.ModeAware
            ? new GovernedServiceMode?[] { GovernedServiceMode.WholePayment, GovernedServiceMode.DailyTransaction }
            : new GovernedServiceMode?[] { null };
        foreach (var mode in modes)
        {
            var resolved = await ResolvePolicyAsync(tenantId, entry, mode, today, ct);
            instruments.Add(new(mode, resolved?.Policy.PermittedInstrumentType, resolved?.Policy.DisplayName));
            if (resolved is null)
                issues.Add(entry.ModeAware
                    ? $"No effective {(mode == GovernedServiceMode.WholePayment ? "whole-payment" : "daily-transaction")} instrument policy."
                    : "No effective revenue classification policy with an approved instrument.");
        }

        if (setting?.Basis == GovernedServiceBasis.VehicleClassRate
            && (await CurrentVehicleClassTermsAsync(tenantId, today, ct)).Count == 0)
            issues.Add("No active vehicle class has an approved rate in force.");
        if (setting?.Basis == GovernedServiceBasis.ApprovedFeeOption
            && (await CurrentFeeOptionTermsAsync(tenantId, service!.Id, today, ct)).Count == 0)
            issues.Add("No fee option is offered with an approved amount rule in force.");

        GovernedServiceSetupState state;
        if (setting is null)
        {
            state = GovernedServiceSetupState.SetupRequired;
            issues.Insert(0, "The amount rule has not been set up.");
        }
        else if (!setting.IsEnabled) state = GovernedServiceSetupState.Disabled;
        else state = issues.Count == 0 ? GovernedServiceSetupState.Active : GovernedServiceSetupState.SetupRequired;

        return new(entry.Code, entry.Name, entry.ClassificationCode, entry.ModeAware, entry.AllowedBases, state,
            setting?.Basis, setting?.FixedAmount, setting?.MaximumAmount, setting?.MobileEnabled ?? false,
            setting?.EffectiveDate, instruments, issues);
    }

    // ── Collector reads ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The approved terms in force today for an operation the collector is assigned to. It answers "not collectible" as a
    /// failure with the reason, rather than a guessed zero or a default amount.
    /// </summary>
    public Task<Result<GovernedServiceTermsDto>> GetTermsAsync(
        string operationCode, GovernedServiceMode? mode, CancellationToken ct = default, DateOnly? businessDate = null) =>
        Run<GovernedServiceTermsDto>(async actor =>
        {
            var entry = GovernedServiceCatalog.Find(operationCode);
            if (actor.Role != "Collector" || entry is null || !await IsAssignedAsync(actor, entry.Code, ct))
                return Result<GovernedServiceTermsDto>.Forbidden();
            if (entry.ModeAware != mode.HasValue || mode is { } m && !Enum.IsDefined(m))
                return Result<GovernedServiceTermsDto>.Failure(
                    entry.ModeAware ? "Choose whole payment or daily transaction." : "This service has no transaction modes.", ResultStatus.Invalid);
            var service = await db.GovernedServices.AsNoTracking().SingleOrDefaultAsync(x =>
                x.MunicipalityId == actor.TenantId && x.OperationCode == entry.Code, ct);
            var versions = service is null ? [] : await db.GovernedServiceSettings.AsNoTracking()
                .Where(x => x.MunicipalityId == actor.TenantId && x.GovernedServiceId == service.Id).ToListAsync(ct);
            var setting = GovernedServiceSetting.Resolve(versions, (businessDate ?? BusinessToday));
            if (setting is null || !setting.IsEnabled || !setting.MobileEnabled)
                return Result<GovernedServiceTermsDto>.Failure(
                    "This operation is not set up for Collector Mobile today.", ResultStatus.Conflict);
            var resolved = await ResolvePolicyAsync(actor.TenantId, entry, mode, (businessDate ?? BusinessToday), ct);
            if (resolved?.Policy.PermittedInstrumentType is not { } instrument)
                return Result<GovernedServiceTermsDto>.Failure(
                    "No approved instrument policy is in effect for this operation today.", ResultStatus.Conflict);
            var classTerms = setting.Basis == GovernedServiceBasis.VehicleClassRate
                ? await CurrentVehicleClassTermsAsync(actor.TenantId, (businessDate ?? BusinessToday), ct) : null;
            IReadOnlyList<FeeOptionTermDto>? optionTerms = null;
            if (setting.Basis == GovernedServiceBasis.ApprovedFeeOption)
            {
                optionTerms = await CurrentFeeOptionTermsAsync(actor.TenantId, service!.Id, (businessDate ?? BusinessToday), ct);
                if (optionTerms.Count == 0)
                    return Result<GovernedServiceTermsDto>.Failure(
                        "No approved fee option is offered for this operation today.", ResultStatus.Conflict);
            }
            return Result<GovernedServiceTermsDto>.Success(new(entry.Code, entry.Name, entry.ModeAware, setting.Basis,
                setting.FixedAmount, setting.MaximumAmount, instrument, false, classTerms, optionTerms));
        }, ct);

    // ── Mobile posting ─────────────────────────────────────────────────────────────────────────────────

    public Task<Result<GovernedServiceOutcomeDto>> PostMobileAsync(
        GovernedServicePostRequest request, CancellationToken ct = default) =>
        Run<GovernedServiceOutcomeDto>(actor => PostCoreAsync(actor, request, ct), ct);

    private async Task<Result<GovernedServiceOutcomeDto>> PostCoreAsync(
        Actor actor, GovernedServicePostRequest request, CancellationToken ct)
    {
        if (actor.Role != "Collector") return Result<GovernedServiceOutcomeDto>.Forbidden();
        if (request.ClientOperationId == Guid.Empty)
            return Result<GovernedServiceOutcomeDto>.Failure("A valid ClientOperationId is required.", ResultStatus.Invalid);
        // Tabo / Slaughterhouse amounts come from their existing fee rules, never from a collector-typed amount.
        if (CollectorOperationCodes.IsFeeSchedule(request.OperationCode)) return Result<GovernedServiceOutcomeDto>.Forbidden();

        var normalized = NormalizeIntent(actor, request);
        var fingerprint = PostingOperation.ComputeIntentFingerprint(IntentVersion, normalized, MobileOrigin, ActorId(actor));
        var prior = await FindOperationAsync(actor.TenantId, request.ClientOperationId, ct);
        if (prior is not null)
            return await ResolvePriorAsync(prior, fingerprint, actor, ct, request.ReceivedAmount);

        var entry = GovernedServiceCatalog.Find(request.OperationCode);
        if (entry is null)
            return await RecordTerminalAsync(actor, request, normalized, "OPERATION_UNSUPPORTED",
                "This operation is not a governed configurable service.", ct);
        if (!await IsAssignedAsync(actor, entry.Code, ct))
            return await RecordTerminalAsync(actor, request, normalized, "COLLECTOR_OPERATION_NOT_ASSIGNED",
                "An explicit operation assignment for an active collector is required.", ct);
        if (request.SchemaVersion != 1)
            return await RecordTerminalAsync(actor, request, normalized, "PAYLOAD_VERSION_UNSUPPORTED",
                "This collection payload version is not supported.", ct);
        if (request.ReceivedAmount <= 0m
            || decimal.Round(request.ReceivedAmount, 2, MidpointRounding.ToZero) != request.ReceivedAmount)
            return await RecordTerminalAsync(actor, request, normalized, "INVALID_INTENT",
                "A positive received amount is required.", ct);
        if (request.PayerName?.Trim().Length > 200 || request.Reference?.Trim().Length > 200)
            return await RecordTerminalAsync(actor, request, normalized, "INVALID_INTENT",
                "Payer and reference text must not exceed 200 characters.", ct);
        if (request.BusinessDate > BusinessToday)
            return await RecordTerminalAsync(actor, request, normalized, "FUTURE_BUSINESS_DATE",
                "Collection BusinessDate cannot be later than the current Philippine business date.", ct);
        if (entry.ModeAware != request.Mode.HasValue || request.Mode is { } m && !Enum.IsDefined(m))
            return await RecordTerminalAsync(actor, request, normalized, "INVALID_MODE",
                entry.ModeAware ? "Choose whole payment or daily transaction." : "This service has no transaction modes.", ct);

        try
        {
            var service = await db.GovernedServices.AsNoTracking().SingleOrDefaultAsync(x =>
                x.MunicipalityId == actor.TenantId && x.OperationCode == entry.Code, ct);
            var versions = service is null ? [] : await db.GovernedServiceSettings.AsNoTracking()
                .Where(x => x.MunicipalityId == actor.TenantId && x.GovernedServiceId == service.Id).ToListAsync(ct);
            var setting = GovernedServiceSetting.Resolve(versions, request.BusinessDate);
            if (service is null || setting is null)
                return await RecordTerminalAsync(actor, request, normalized, "SERVICE_SETUP_REQUIRED",
                    "This service has no approved amount rule for the business date. No Collection was created.", ct);
            if (!setting.IsEnabled)
                return await RecordTerminalAsync(actor, request, normalized, "SERVICE_DISABLED",
                    "This service is disabled for new transactions.", ct);
            if (!setting.MobileEnabled)
                return await RecordTerminalAsync(actor, request, normalized, "MOBILE_CHANNEL_DISABLED",
                    "This service is not enabled for Collector Mobile.", ct);
            var resolved = await ResolvePolicyAsync(actor.TenantId, entry, request.Mode, request.BusinessDate, ct);
            if (resolved is null)
                return await RecordTerminalAsync(actor, request, normalized, "POLICY_NOT_EFFECTIVE",
                    "No effective approved instrument policy exists for this operation and business date.", ct);
            var instrument = resolved.Policy.PermittedInstrumentType!.Value;
            // Transportation / Parking: the collector states the vehicle class; the amount is that class's approved rate in
            // force on the business date, never a typed or remembered figure. Any other service takes no class.
            VehicleClass? vehicleClass = null;
            VehicleClassRate? vehicleRate = null;
            var classCode = request.VehicleClassCode?.Trim().ToUpperInvariant();
            if (setting.Basis == GovernedServiceBasis.VehicleClassRate)
            {
                if (string.IsNullOrEmpty(classCode))
                    return await RecordTerminalAsync(actor, request, normalized, "VEHICLE_CLASS_REQUIRED",
                        "Choose the vehicle class. Its approved rate is the amount.", ct);
                vehicleClass = await db.VehicleClasses.AsNoTracking().SingleOrDefaultAsync(x =>
                    x.MunicipalityId == actor.TenantId && x.Code == classCode && x.IsActive, ct);
                if (vehicleClass is null)
                    return await RecordTerminalAsync(actor, request, normalized, "VEHICLE_CLASS_UNKNOWN",
                        "This vehicle class is not an approved, active class for this office.", ct);
                var rates = await db.VehicleClassRates.AsNoTracking().Where(x =>
                    x.MunicipalityId == actor.TenantId && x.VehicleClassId == vehicleClass.Id).ToListAsync(ct);
                vehicleRate = VehicleClassRate.Resolve(rates, request.BusinessDate);
                if (vehicleRate is null)
                    return await RecordTerminalAsync(actor, request, normalized, "VEHICLE_CLASS_RATE_NOT_EFFECTIVE",
                        "This vehicle class has no approved rate in force for the business date.", ct);
                if (request.ReceivedAmount != vehicleRate.Amount)
                    return await RecordTerminalAsync(actor, request, normalized, "AMOUNT_NOT_APPROVED",
                        "The amount is not the approved rate for this vehicle class.", ct);
            }
            else if (!string.IsNullOrEmpty(classCode))
                return await RecordTerminalAsync(actor, request, normalized, "INVALID_INTENT",
                    "This service does not take a vehicle class.", ct);

            // Approved fee options: the collector selects an option the office configured; its rule in force on the business
            // date decides the amount (a fixed amount must be matched exactly; a direct amount only where that option is
            // configured for it). A retired or unknown option, or one without a rule, is refused — never priced by guess.
            GovernedServiceFeeOption? feeOption = null;
            GovernedServiceFeeOptionRate? feeRate = null;
            if (setting.Basis == GovernedServiceBasis.ApprovedFeeOption)
            {
                if (request.FeeOptionId is not { } optionId || optionId == Guid.Empty)
                    return await RecordTerminalAsync(actor, request, normalized, "FEE_OPTION_REQUIRED",
                        "Choose the approved fee being collected.", ct);
                feeOption = await db.GovernedServiceFeeOptions.AsNoTracking().SingleOrDefaultAsync(x =>
                    x.MunicipalityId == actor.TenantId && x.GovernedServiceId == service.Id && x.Id == optionId, ct);
                if (feeOption is null || !feeOption.IsOfferedOn(request.BusinessDate))
                    return await RecordTerminalAsync(actor, request, normalized, "FEE_OPTION_UNKNOWN",
                        "This fee is not an approved, offered fee option for this operation on the business date.", ct);
                var optionRates = await db.GovernedServiceFeeOptionRates.AsNoTracking().Where(x =>
                    x.MunicipalityId == actor.TenantId && x.FeeOptionId == feeOption.Id).ToListAsync(ct);
                feeRate = GovernedServiceFeeOptionRate.Resolve(optionRates, request.BusinessDate);
                if (feeRate is null)
                    return await RecordTerminalAsync(actor, request, normalized, "FEE_OPTION_RATE_NOT_EFFECTIVE",
                        "This fee option has no approved amount rule in force for the business date.", ct);
                if (feeRate.CheckAmount(request.ReceivedAmount) is { } optionProblem)
                    return await RecordTerminalAsync(actor, request, normalized, optionProblem,
                        optionProblem == "AMOUNT_ABOVE_CEILING"
                            ? "The amount exceeds the approved ceiling for this fee."
                            : "The amount is not the approved amount for this fee.", ct);
            }
            else if (request.FeeOptionId is not null)
                return await RecordTerminalAsync(actor, request, normalized, "INVALID_INTENT",
                    "This service does not take a fee option.", ct);

            if (setting.Basis is not (GovernedServiceBasis.VehicleClassRate or GovernedServiceBasis.ApprovedFeeOption)
                && setting.CheckAmount(request.ReceivedAmount) is { } amountProblem)
                return await RecordTerminalAsync(actor, request, normalized, amountProblem,
                    amountProblem == "AMOUNT_ABOVE_CEILING"
                        ? "The amount exceeds the approved ceiling for this service."
                        : "The amount is not the approved amount for this service.", ct);

            var snapshot = JsonSerializer.Serialize(new GovernedSnapshot(
                1, actor.TenantId, service.Id, entry.Code, setting.Id, setting.EffectiveDate, setting.Basis,
                setting.FixedAmount, setting.MaximumAmount, request.Mode, instrument, entry.ClassificationCode,
                resolved.Policy.Id, resolved.Policy.EffectiveDate,
                vehicleClass is null ? request.Reference?.Trim()
                    : string.IsNullOrWhiteSpace(request.Reference) ? vehicleClass.DisplayName : $"{vehicleClass.DisplayName} · {request.Reference.Trim()}",
                request.PayerName?.Trim(),
                vehicleClass?.Code, vehicleClass?.DisplayName, vehicleRate?.Id, vehicleRate?.EffectiveDate, vehicleRate?.Amount,
                feeOption?.Id, feeOption is null ? null : FeeOptionLabel(feeOption), feeOption?.Code, feeRate?.Id,
                feeRate?.EffectiveDate, feeRate?.Basis, feeRate?.FixedAmount, feeRate?.MaximumAmount), JsonOptions);
            // An immediate activity charge: an approved source identity and frozen evidence, no fabricated receivable.
            var line = new CollectionLineDraft(resolved.Classification, resolved.Policy, request.ReceivedAmount,
                CollectionSourceKind.GovernedService, service.Id, null, snapshot, null);
            var collection = await new CanonicalCollectionPostingCoordinator(db).PostAsync(
                actor.TenantId, request.ClientOperationId, IntentVersion, normalized, MobileOrigin, ActorId(actor),
                actor.Username, actor.Role, request.BusinessDate, actor.Username, [line], null,
                collectorId: actor.UserId, payerName: string.IsNullOrWhiteSpace(request.PayerName) ? null : request.PayerName.Trim(), ct: ct);
            return Result<GovernedServiceOutcomeDto>.Success(new(collection.Id, collection.ReferenceCode,
                collection.BusinessDate, collection.TotalAmount, instrument, "Posted", false));
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var winner = await FindOperationAsync(actor.TenantId, request.ClientOperationId, ct);
            if (winner is not null) return await ResolvePriorAsync(winner, fingerprint, actor, ct, request.ReceivedAmount);
            return await RecordTerminalAsync(actor, request, normalized, "POST_CONFLICT",
                "The operation identity conflicts with another posting; no Collection was partially posted.", ct);
        }
    }

    private async Task<Result<GovernedServiceOutcomeDto>> RecordTerminalAsync(
        Actor actor, GovernedServicePostRequest request, string normalized,
        string code, string message, CancellationToken ct)
    {
        db.PostingOperations.Add(PostingOperation.Record(actor.TenantId, request.ClientOperationId,
            IntentVersion, normalized, MobileOrigin, ActorId(actor), PostingOperationStatus.Rejected,
            code, message, null, null, DateTime.UtcNow));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var prior = await FindOperationAsync(actor.TenantId, request.ClientOperationId, ct);
            if (prior is not null)
                return await ResolvePriorAsync(prior,
                    PostingOperation.ComputeIntentFingerprint(IntentVersion, normalized, MobileOrigin, ActorId(actor)), actor, ct);
            throw;
        }
        return Result<GovernedServiceOutcomeDto>.Failure(message, ResultStatus.Conflict);
    }

    private async Task<Result<GovernedServiceOutcomeDto>> ResolvePriorAsync(
        PostingOperation prior, string fingerprint, Actor actor, CancellationToken ct, decimal? requestedAmount = null)
    {
        if (prior.Origin != MobileOrigin || prior.ActorId != ActorId(actor) || (prior.IntentFingerprint != fingerprint && prior.AccountableDocumentId is null))   // a pre-SRC operation bound a physical document into its intent; it still replays
            return Result<GovernedServiceOutcomeDto>.Failure(
                "IDEMPOTENCY CONFLICT: this ClientOperationId is already bound to a different posting intent.", ResultStatus.Conflict);
        if (prior.Status != PostingOperationStatus.Succeeded || prior.CollectionId is not { } collectionId)
            return Result<GovernedServiceOutcomeDto>.Failure(
                prior.OutcomeDetails ?? prior.OutcomeCode ?? "The original posting was rejected.", ResultStatus.Conflict);
        var collection = await db.Collections.AsNoTracking().Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.MunicipalityId == actor.TenantId && x.Id == collectionId, ct);
        if (collection is null)
            return Result<GovernedServiceOutcomeDto>.Failure("The recorded Collection outcome is unavailable.", ResultStatus.Conflict);
        // A pre-SRC operation cannot be compared by fingerprint (its intent bound a physical document), so it replays only
        // for the same money: a different amount under the same ClientOperationId is still a conflict.
        if (prior.IntentFingerprint != fingerprint && requestedAmount is { } amount && amount != collection.TotalAmount)
            return Result<GovernedServiceOutcomeDto>.Failure(
                "IDEMPOTENCY CONFLICT: this ClientOperationId is already bound to a different posting intent.", ResultStatus.Conflict);
        var instrument = ReadSnapshot(collection.Lines.OrderBy(x => x.Id).FirstOrDefault()?.CalculationSnapshot)?.Instrument
            ?? RevenueInstrumentType.CashTicket;
        return Result<GovernedServiceOutcomeDto>.Success(new(collection.Id, collection.ReferenceCode,
            collection.BusinessDate, collection.TotalAmount, instrument, "Posted", true));
    }

    // ── Office activity ────────────────────────────────────────────────────────────────────────────────

    public Task<Result<IReadOnlyList<GovernedServiceActivityDto>>> GetActivityAsync(
        string operationCode, DateOnly from, DateOnly to, CancellationToken ct = default) =>
        Run<IReadOnlyList<GovernedServiceActivityDto>>(async actor =>
        {
            if (actor.Role == "Collector") return Result<IReadOnlyList<GovernedServiceActivityDto>>.Forbidden();
            var entry = GovernedServiceCatalog.Find(operationCode);
            if (entry is null) return Result<IReadOnlyList<GovernedServiceActivityDto>>.Failure("Unknown service.", ResultStatus.NotFound);
            if (from > to || to.DayNumber - from.DayNumber > 366)
                return Result<IReadOnlyList<GovernedServiceActivityDto>>.Failure("Choose a valid collection period of no more than 367 days.", ResultStatus.Invalid);
            var service = await db.GovernedServices.AsNoTracking().SingleOrDefaultAsync(x =>
                x.MunicipalityId == actor.TenantId && x.OperationCode == entry.Code, ct);
            if (service is null) return Result<IReadOnlyList<GovernedServiceActivityDto>>.Success([]);

            var lines = await db.CollectionLines.AsNoTracking().Where(x =>
                x.MunicipalityId == actor.TenantId && x.SourceKind == CollectionSourceKind.GovernedService
                && x.SourceId == service.Id).ToListAsync(ct);
            var ids = lines.Select(x => x.CollectionId).Distinct().ToArray();
            var collections = await db.Collections.AsNoTracking().Where(x =>
                x.MunicipalityId == actor.TenantId && ids.Contains(x.Id)
                && x.BusinessDate >= from && x.BusinessDate <= to)
                .OrderByDescending(x => x.RecordedAtUtc).ToListAsync(ct);
            var collectionIds = collections.Select(x => x.Id).ToArray();
            var collectorIds = collections.Where(x => x.CollectorId.HasValue).Select(x => x.CollectorId!.Value).Distinct().ToArray();
            var collectors = await db.CollectorUsers.AsNoTracking().Where(x =>
                x.MunicipalityId == actor.TenantId && collectorIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
            var corrections = await db.CollectionCorrections.AsNoTracking().Where(x =>
                x.MunicipalityId == actor.TenantId && collectionIds.Contains(x.OriginalCollectionId))
                .Select(x => new { x.OriginalCollectionId, x.FinancialEffectAmount, x.CorrectionType }).ToListAsync(ct);

            var activity = collections.Select(collection =>
            {
                var line = lines.First(x => x.CollectionId == collection.Id);
                var facts = ReadSnapshot(line.CalculationSnapshot);
                var effects = corrections.Where(x => x.OriginalCollectionId == collection.Id).ToList();
                var disposition = effects.Any(x => x.FinancialEffectAmount < 0m) ? "Reversed"
                    : effects.Any(x => x.CorrectionType == CollectionCorrectionType.DocumentCorrection) ? "Document corrected" : "Posted";
                return new GovernedServiceActivityDto(collection.Id, collection.BusinessDate, collection.RecordedAtUtc,
                    collection.ReferenceCode, facts?.Instrument, facts?.Mode,
                    collection.PayerName, facts?.Reference, line.Amount,
                    collection.CollectorId is { } id ? collectors.GetValueOrDefault(id) : null, disposition, facts?.FeeOptionName);
            }).ToList();
            return Result<IReadOnlyList<GovernedServiceActivityDto>>.Success(activity);
        }, ct);

    /// <summary>
    /// The calling collector's own posted collections through governed operations. Read from the canonical Collection,
    /// never from the device queue, so a collection that was rejected or is awaiting reconciliation is not listed as money.
    /// </summary>
    public Task<Result<IReadOnlyList<GovernedServiceRecordDto>>> GetCollectorRecordsAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default) =>
        Run<IReadOnlyList<GovernedServiceRecordDto>>(async actor =>
        {
            if (actor.Role != "Collector") return Result<IReadOnlyList<GovernedServiceRecordDto>>.Forbidden();
            if (from > to || to.DayNumber - from.DayNumber > 366)
                return Result<IReadOnlyList<GovernedServiceRecordDto>>.Failure("Choose a valid period of no more than 367 days.", ResultStatus.Invalid);
            var rows = await (
                from collection in db.Collections.AsNoTracking()
                join line in db.CollectionLines.AsNoTracking() on collection.Id equals line.CollectionId
                join service in db.GovernedServices.AsNoTracking()
                    on new { line.MunicipalityId, Id = line.SourceId } equals new { service.MunicipalityId, Id = (Guid?)service.Id }
                where collection.MunicipalityId == actor.TenantId && collection.CollectorId == actor.UserId
                    && collection.BusinessDate >= @from && collection.BusinessDate <= to
                    && line.SourceKind == CollectionSourceKind.GovernedService
                orderby collection.RecordedAtUtc descending
                select new { collection.Id, collection.BusinessDate, collection.RecordedAtUtc, collection.PayerName, collection.ReferenceCode,
                    service.OperationCode, line.CalculationSnapshot, line.Amount }).ToListAsync(ct);
            var ids = rows.Select(x => x.Id).ToArray();
            var corrections = await db.CollectionCorrections.AsNoTracking().Where(x =>
                x.MunicipalityId == actor.TenantId && ids.Contains(x.OriginalCollectionId))
                .Select(x => new { x.OriginalCollectionId, x.FinancialEffectAmount }).ToListAsync(ct);
            var records = rows.Select(x =>
            {
                var facts = ReadSnapshot(x.CalculationSnapshot);
                return new GovernedServiceRecordDto(x.Id, x.BusinessDate, x.RecordedAtUtc, x.OperationCode,
                    GovernedServiceCatalog.Find(x.OperationCode)?.Name ?? x.OperationCode,
                    x.ReferenceCode, facts?.Instrument, facts?.Mode, x.PayerName, facts?.Reference,
                    x.Amount, corrections.Any(c => c.OriginalCollectionId == x.Id && c.FinancialEffectAmount < 0m) ? "Reversed" : "Posted",
                    facts?.FeeOptionName);
            }).ToList();
            return Result<IReadOnlyList<GovernedServiceRecordDto>>.Success(records);
        }, ct);

    // ── Shared helpers ─────────────────────────────────────────────────────────────────────────────────

    private async Task<PolicyFacts?> ResolvePolicyAsync(
        Guid tenantId, GovernedServiceCatalog.Entry entry, GovernedServiceMode? mode, DateOnly asOf, CancellationToken ct)
    {
        var classification = await db.RevenueClassifications.SingleOrDefaultAsync(x =>
            x.MunicipalityId == tenantId && x.SemanticCode == entry.ClassificationCode && x.IsActive, ct);
        if (classification is null) return null;
        var context = GovernedServiceCatalog.ContextFor(entry, mode);
        var policy = await db.RevenueClassificationPolicies.Where(x =>
                x.MunicipalityId == tenantId && x.RevenueClassificationId == classification.Id
                && x.BusinessContext == context && x.EffectiveDate <= asOf)
            .OrderByDescending(x => x.EffectiveDate).FirstOrDefaultAsync(ct);
        return policy?.PermittedInstrumentType is null ? null : new PolicyFacts(classification, policy);
    }

    /// <summary>Assignment is permission only, but it is required, and the collector must be active in this tenant.</summary>
    private async Task<bool> IsAssignedAsync(Actor actor, string operationCode, CancellationToken ct) =>
        actor.Role == "Collector"
        && await db.CollectorUsers.AsNoTracking().AnyAsync(x =>
            x.MunicipalityId == actor.TenantId && x.Id == actor.UserId && x.IsActive, ct)
        && await db.CollectorOperationAssignments.AsNoTracking().AnyAsync(x =>
            x.MunicipalityId == actor.TenantId && x.CollectorId == actor.UserId && x.OperationCode == operationCode, ct);

    private Task<PostingOperation?> FindOperationAsync(Guid tenantId, Guid operationId, CancellationToken ct) =>
        db.PostingOperations.AsNoTracking().SingleOrDefaultAsync(x =>
            x.MunicipalityId == tenantId && x.ClientOperationId == operationId, ct);

    private static string NormalizeIntent(Actor actor, GovernedServicePostRequest request)
    {
        var json = NormalizeBaseIntent(actor, request);
        if (string.IsNullOrWhiteSpace(request.VehicleClassCode) && request.FeeOptionId is null) return json;
        // A class or fee option is part of the intent only when stated, so every earlier intent keeps its exact fingerprint.
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        if (!string.IsNullOrWhiteSpace(request.VehicleClassCode))
            node["VehicleClassCode"] = request.VehicleClassCode.Trim().ToUpperInvariant();
        if (request.FeeOptionId is { } optionId)
            node["FeeOptionId"] = optionId.ToString("D");
        return node.ToJsonString(JsonOptions);
    }

    private static string NormalizeBaseIntent(Actor actor, GovernedServicePostRequest request) =>
        JsonSerializer.Serialize(new
        {
            SchemaVersion = request.SchemaVersion,
            OperationType = "GovernedServiceCollection",
            TenantId = actor.TenantId,
            ActorId = ActorId(actor),
            OperationCode = request.OperationCode?.Trim(),
            Mode = request.Mode?.ToString(),
            BusinessDate = request.BusinessDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ReceivedAmount = request.ReceivedAmount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            PayerName = string.IsNullOrWhiteSpace(request.PayerName) ? null : request.PayerName.Trim(),
            Reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim()
        }, JsonOptions);

    private static string ActorId(Actor actor) => actor.UserId.ToString("N");

    /// <summary>The optional free-text reference a collector recorded, read from the frozen calculation snapshot.</summary>
    public static string? ReadReference(string? snapshotJson) => ReadSnapshot(snapshotJson)?.Reference;

    private static GovernedSnapshot? ReadSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<GovernedSnapshot>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }

    private async Task<Result<T>> Run<T>(Func<Actor, Task<Result<T>>> action, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId || userId == Guid.Empty)
            return Result<T>.Unauthorized();
        if (currentUser.Role is not ("Admin" or "SuperAdmin" or "Collector"))
            return Result<T>.Forbidden();
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenantId)
            return Result<T>.Forbidden();
        var actor = new Actor(userId,
            tenantId, currentUser.Username ?? "Office User", currentUser.Role!);
        try { return await action(actor); }
        catch (DbUpdateConcurrencyException) { return Result<T>.Failure("The service or document changed concurrently.", ResultStatus.Conflict); }
        catch (DbUpdateException) { return Result<T>.Failure("The operation conflicts with another saved transaction.", ResultStatus.Conflict); }
    }

    private sealed record Actor(Guid UserId, Guid TenantId, string Username, string Role);
    private sealed record PolicyFacts(RevenueClassification Classification, RevenueClassificationPolicy Policy);
    private sealed record GovernedSnapshot(
        int SchemaVersion, Guid MunicipalityId, Guid ServiceId, string OperationCode, Guid SettingId,
        DateOnly SettingEffectiveDate, GovernedServiceBasis Basis, decimal? FixedAmount, decimal? MaximumAmount,
        GovernedServiceMode? Mode, RevenueInstrumentType Instrument, string ClassificationCode,
        Guid PolicyId, DateOnly PolicyEffectiveDate, string? Reference, string? PayerName,
        string? VehicleClassCode = null, string? VehicleClassName = null, Guid? VehicleClassRateId = null,
        DateOnly? VehicleClassRateEffectiveDate = null, decimal? VehicleClassRate = null,
        Guid? FeeOptionId = null, string? FeeOptionName = null, string? FeeOptionCode = null, Guid? FeeOptionRateId = null,
        DateOnly? FeeOptionRateEffectiveDate = null, GovernedServiceBasis? FeeOptionBasis = null,
        decimal? FeeOptionFixedAmount = null, decimal? FeeOptionMaximumAmount = null);
}

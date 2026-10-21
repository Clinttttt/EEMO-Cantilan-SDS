using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Authorization;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common.Billing;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EEMOCantilanSDS.Application.Common.Payments;

/// <summary>
/// Specialized month authority, using the existing canonical coordinator. Not registered or discoverable until
/// every legacy ingress/reader/correction is coordinated. It never creates daily rows, auto-reconciles an opening,
/// creates a second vendor, issues a document, or invents an unapproved assessment/rate.
/// </summary>
public sealed partial class NpmRentMonthWorkflow(IAppDbContext db, ICurrentMunicipalityAccessor tenant,
    ICurrentUserService user, ICollectorRepository collectors, IFeeRateResolver rates, IClock clock,
    NpmRentMonthRuntime runtime, NpmPartPaymentOptions partPayments, INpmMarketClosureRepository closures,
    NpmMonthSettlementService? assessments = null, IEemoCacheInvalidator? cache = null,
    ILogger<NpmRentMonthWorkflow>? logger = null, NpmOpeningMonthPolicies? openingPolicies = null)
{
    public const string Origin = "NpmRentMonth";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private Guid Tenant => tenant.MunicipalityId;
    public bool Enabled => runtime.Allows(Tenant)
        && db.ChangeTracker.Context.Model.FindEntityType(typeof(CollectionAllocation))?.FindProperty(nameof(CollectionAllocation.NpmRentMonthId)) is not null
        && db.ChangeTracker.Context.Model.FindEntityType(typeof(NpmRentMonth))?.FindProperty(nameof(NpmRentMonth.SettlementSealedAtUtc)) is not null
        && db.ChangeTracker.Context.Model.FindEntityType(typeof(NpmRentMonth))?.FindProperty(nameof(NpmRentMonth.SettlementActivatedAtUtc)) is not null
        && db.ChangeTracker.Context.Model.FindEntityType(typeof(NpmRentMonth))?.FindProperty(nameof(NpmRentMonth.SettlementActivatedByUserId)) is not null;

    /// <summary>Call only after the owning transaction commits, including outer session/batch transactions.</summary>
    public async Task InvalidateAfterCommitAsync(CancellationToken ct = default)
    {
        if (!Enabled || cache is null) return;
        try
        {
            var code = await db.Municipalities.AsNoTracking().Where(x => x.Id == Tenant).Select(x => x.TenantCode).SingleAsync(ct);
            await cache.InvalidateTenantAsync(code, ct);
        }
        catch (Exception ex)
        {
            // The financial transaction already committed. Cache failure must never claim it rolled back.
            logger?.LogWarning("NPM committed successfully but cache refresh failed for tenant {TenantId}: {ErrorType}", Tenant, ex.GetType().Name);
        }
    }

    private async Task<bool> Authorized(CancellationToken ct) => Enabled && user.IsAuthenticated
        && user.UserId is { } actor && actor != Guid.Empty && !string.IsNullOrWhiteSpace(user.Username) && user.MunicipalityId == Tenant
        && user.Role is "SuperAdmin" or "Admin" or "Collector"
        && await NpmSettlementAccess.MaySettleMarketCollectionsAsync(user, collectors, ct);

    /// <summary>
    /// Route a stall's specific period, not the whole municipality. Older NPM occupancies
    /// retain their original daily/whole authority after a prospective policy is enabled.
    /// A prepared/activated monthly source, or an occupancy registered under the approved
    /// prospective rule, must NEVER fall back to legacy if financial activation is pending.
    /// </summary>
    public async Task<bool> MustUseMonthlyAuthorityAsync(Guid stallId, DateOnly period, CancellationToken ct = default)
    {
        if (period.Day != 1 || stallId == Guid.Empty || Tenant == Guid.Empty) return false;
        if (await db.NpmRentMonths.AsNoTracking().AnyAsync(x =>
                x.MunicipalityId == Tenant && x.StallId == stallId && x.PeriodStart == period, ct))
            return true;
        if (!Enabled) return false;

        var stall = await db.Stalls.AsNoTracking().Include(x => x.Facility).Include(x => x.Contracts)
            .SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.Id == stallId, ct);
        if (stall?.Facility is not { Code: FacilityCode.NPM, MonthBasis: NpmMonthBasis.RentGoal } facility
            || facility.IsMonthlyRentMarket || stall.IsCustomSection) return false;
        var occupant = stall.OccupancyAnsweringForMonth(period.Year, period.Month, clock.PhilippineToday)?.Contract;
        if (occupant is null || occupant.EffectivityDate > period.AddMonths(1).AddDays(-1)) return false;
        return (openingPolicies?.Approvals ?? []).Any(p => p.MunicipalityId == Tenant
            && p.FacilityId == facility.Id && p.EffectiveFrom <= occupant.EffectivityDate
            && p.ApprovedAtUtc.Kind == DateTimeKind.Utc && p.ApprovedAtUtc <= occupant.CreatedAt);
    }

    /// <summary>Receipt evidence also blocks legacy fallback if a source's state is inconsistent.</summary>
    public Task<bool> IsMonthActivatedAsync(Guid stallId, DateOnly period, CancellationToken ct = default) =>
        db.NpmRentMonths.AsNoTracking().AnyAsync(x => x.MunicipalityId == Tenant && x.StallId == stallId && x.PeriodStart == period
            && (x.Status == NpmRentMonthPreparationStatus.FinanciallyActivated || db.CollectionAllocations.Any(a => a.MunicipalityId == Tenant
                && a.SourceKind == CollectionSourceKind.NpmRentMonth && a.SourceId == x.Id)), ct);

    public Task<bool> HasActivatedPeriodAsync(DateOnly period, CancellationToken ct = default) =>
        db.NpmRentMonths.AsNoTracking().AnyAsync(x => x.MunicipalityId == Tenant && x.PeriodStart == period
            && (x.Status == NpmRentMonthPreparationStatus.FinanciallyActivated || db.CollectionAllocations.Any(a => a.MunicipalityId == Tenant
                && a.SourceKind == CollectionSourceKind.NpmRentMonth && a.SourceId == x.Id)), ct);

    public Task<bool> HasActivatedStallAsync(Guid stallId, CancellationToken ct = default) =>
        db.NpmRentMonths.AsNoTracking().AnyAsync(x => x.MunicipalityId == Tenant && x.StallId == stallId
            && (x.Status == NpmRentMonthPreparationStatus.FinanciallyActivated || db.CollectionAllocations.Any(a => a.MunicipalityId == Tenant
                && a.SourceKind == CollectionSourceKind.NpmRentMonth && a.SourceId == x.Id)), ct);

    public Task<bool> HasActivatedFromAsync(DateOnly effectiveFrom, CancellationToken ct = default) =>
        db.NpmRentMonths.AsNoTracking().AnyAsync(x => x.MunicipalityId == Tenant
            && x.PeriodStart >= new DateOnly(effectiveFrom.Year, effectiveFrom.Month, 1)
            && (x.Status == NpmRentMonthPreparationStatus.FinanciallyActivated || db.CollectionAllocations.Any(a => a.MunicipalityId == Tenant
                && a.SourceKind == CollectionSourceKind.NpmRentMonth && a.SourceId == x.Id)), ct);

    /// <summary>
    /// Current source position for existing authorized repository projections. Callers retain their source-access
    /// guards. No municipality parameter or new public read endpoint is accepted here. Never falls back for an activated month.
    /// </summary>
    public async Task<NpmRentMonthPosition?> ReadPositionAsync(Guid stallId, DateOnly period, CancellationToken ct = default)
    {
        if (!await IsMonthActivatedAsync(stallId, period, ct)) return null;
        if (!Enabled) throw new InvalidOperationException("An activated NPM month cannot fall back to legacy financial authority.");
        return (await Load(stallId, period, ct)).Position;
    }

    public async Task<Result<NpmMonthPayable>> QuoteEarnedAsync(Guid stallId, DateOnly period, CancellationToken ct = default)
    {
        if (!await Authorized(ct)) return Result<NpmMonthPayable>.Forbidden();
        try
        {
            var payable = await ReadEarnedPositionAsync(stallId, period, ct)
                ?? throw new InvalidOperationException("No activated month source exists.");
            return Result<NpmMonthPayable>.Success(payable);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or JsonException)
        { return Result<NpmMonthPayable>.Failure("RECONCILIATION_REQUIRED: " + ex.Message, ResultStatus.Conflict); }
    }

    /// <summary>Tenant-scoped projection for existing authorized readers, not collection authorization.</summary>
    public async Task<NpmMonthPayable?> ReadEarnedPositionAsync(Guid stallId, DateOnly period, CancellationToken ct = default)
    {
        if (!await IsMonthActivatedAsync(stallId, period, ct)) return null;
        var (month, stall, position, _, _, _) = await Load(stallId, period, ct);
        using var policy = JsonDocument.Parse(month.RateEvidenceJson);
        var liability = await EarnedLiability(month, stall, position, clock.PhilippineToday, policy, ct);
        var amount = Math.Min(position.Outstanding, Math.Max(0m, liability - position.LegacyCash - position.CanonicalCash));
        return new(0, amount, 0, position.Assessment, position.LegacyCash + position.CanonicalCash, position.Credits, month.Id, liability);
    }

    private sealed record LoadedMonth(NpmRentMonth Month, Stall Stall, NpmRentMonthPosition Position, string EvidenceHash,
        List<CollectionAllocation> Allocations, List<CollectionCorrectionAllocation> Corrections);

    private async Task<LoadedMonth> Load(
        Guid stallId, DateOnly period, CancellationToken ct, ReportEvidence? report = null)
    {
        if (!Enabled || period.Day != 1 || period.Year is < 2000 or > 2100)
            throw new InvalidOperationException("Monthly settlement is unavailable or the period is invalid.");
        var month = (report is null ? await db.NpmRentMonths.SingleOrDefaultAsync(x => x.MunicipalityId == Tenant
            && x.StallId == stallId && x.PeriodStart == period, ct) : report.Months.SingleOrDefault(x => x.StallId == stallId && x.PeriodStart == period))
            ?? throw new InvalidOperationException("No explicitly reconciled month source exists.");
        if (!month.CanAcceptCollections || month.SettlementActivatedAtUtc is null || month.SettlementActivatedByUserId is null)
            throw new InvalidOperationException("Month review is complete but financial activation has not been authorized.");
        var stall = report is null ? await db.Stalls.Include(x => x.Facility).Include(x => x.Contracts)
            .SingleAsync(x => x.MunicipalityId == Tenant && x.Id == stallId, ct) : report.Stalls.Single(x => x.Id == stallId);
        var owner = StallOccupancy.AnsweringForMonth(stall.Occupancies(clock.PhilippineToday), period.Year, period.Month);
        if (stall.Facility?.Code != FacilityCode.NPM || stall.Facility.IsMonthlyRentMarket || stall.FacilityId != month.FacilityId || owner?.Contract.Id != month.ContractId)
            throw new InvalidOperationException("Month ownership changed or is ambiguous; reconcile it before collection.");
        if (OpeningEvidence(month) is { } opening
            && (owner.Start != opening.OccupancyStart || owner.BillableEnd < period.AddMonths(1).AddDays(-1)
                || stall.Occupancies(clock.PhilippineToday).Count(x => x.Overlaps(period, period.AddMonths(1).AddDays(-1))) != 1))
            throw new InvalidOperationException("Prospective month ownership changed; handover or early termination requires explicit Office reconciliation.");
        if (report is null ? await db.PaymentRecords.AsNoTracking().AnyAsync(x => x.MunicipalityId == Tenant && x.StallId == stallId
            && x.BillingYear == period.Year && x.BillingMonth == period.Month && x.Status != PaymentStatus.Unpaid, ct)
            : report.Payments.Any(x => x.StallId == stallId && x.BillingYear == period.Year && x.BillingMonth == period.Month && x.Status != PaymentStatus.Unpaid))
            throw new InvalidOperationException("Legacy monthly payment evidence is outside the reviewed daily opening manifest. Reconcile that original evidence explicitly; no automatic conversion is permitted.");

        var manifest = ParseOpening(month.ReconciliationEvidence);
        var end = period.AddMonths(1);
        var days = report is null ? await db.DailyCollections.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.StallId == stallId
            && x.CollectionDate >= period && x.CollectionDate < end).ToListAsync(ct)
            : report.Days.Where(x => x.StallId == stallId && x.CollectionDate >= period && x.CollectionDate < end).ToList();
        var ids = days.Select(x => x.Id).ToArray();
        var allocations = report is null ? await db.CollectionAllocations.AsNoTracking().Where(x => x.MunicipalityId == Tenant
            && (x.SourceKind == CollectionSourceKind.NpmRentMonth && x.SourceId == month.Id
                || x.SourceKind == CollectionSourceKind.DailyCollection && x.SourcePart == CollectionSourcePart.DailyFee && ids.Contains(x.SourceId)))
            .OrderBy(x => x.Id).ToListAsync(ct)
            : report.Allocations.Where(x => x.SourceKind == CollectionSourceKind.NpmRentMonth && x.SourceId == month.Id
                || x.SourceKind == CollectionSourceKind.DailyCollection && ids.Contains(x.SourceId)).OrderBy(x => x.Id).ToList();
        var canonicalDays = allocations.Where(x => x.SourceKind == CollectionSourceKind.DailyCollection).Select(x => x.SourceId).Distinct().Order().ToArray();
        if (days.Any(x => x.SettlementAuthorityState == SettlementAuthority.Canonical && x.CollectedFee > 0m && !canonicalDays.Contains(x.Id)))
            throw new InvalidOperationException("Canonical daily evidence is missing its original rent allocation.");
        var legacyDays = days.Where(x => x.SettlementAuthorityState != SettlementAuthority.Canonical && x.CollectedFee > 0m).OrderBy(x => x.Id).ToArray();
        if (!manifest.CanonicalDailyIds.Order().SequenceEqual(canonicalDays)
            || !manifest.LegacyDailyIds.Order().SequenceEqual(legacyDays.Select(x => x.Id)))
            throw new InvalidOperationException("Historical rent evidence is incomplete or changed; no automatic opening conversion is permitted.");
        if (days.Any(x => (x.CollectedFee > 0m || allocations.Any(a => a.SourceId == x.Id))
            && (x.CollectionDate < owner.Start || x.CollectionDate > owner.BillableEnd)))
            throw new InvalidOperationException("Historical payment crosses an occupancy handover; explicit reconciliation is required.");
        if (manifest.LegacyFingerprint != Hash(legacyDays.Select(x => new { x.Id, x.CollectionDate, x.CollectedFee }).ToArray()))
            throw new InvalidOperationException("Reviewed legacy cash evidence changed; reconcile it explicitly.");

        var receipts = legacyDays.Select(x => new NpmRentCashEvidence(x.Id, Tenant, stall.Id, month.ContractId,
            period, NpmRentCashEvidenceKind.LegacyDaily, x.CollectedFee, x.Id)).ToList();
        foreach (var a in allocations)
            receipts.Add(new(a.Id, Tenant, stall.Id, month.ContractId, period,
                a.SourceKind == CollectionSourceKind.NpmRentMonth ? NpmRentCashEvidenceKind.CanonicalMonthAllocation
                    : NpmRentCashEvidenceKind.CanonicalDailyAllocation, a.Amount,
                a.SourceKind == CollectionSourceKind.DailyCollection ? a.SourceId : null));
        var allocationIds = allocations.Select(x => x.Id).ToArray();
        var corrections = report is null ? await db.CollectionCorrectionAllocations.AsNoTracking().Where(x => x.MunicipalityId == Tenant
            && allocationIds.Contains(x.OriginalAllocationId)).OrderBy(x => x.Id).ToListAsync(ct)
            : report.Corrections.Where(x => allocationIds.Contains(x.OriginalAllocationId)).OrderBy(x => x.Id).ToList();
        var effects = corrections.Select(x => new NpmRentCorrectionEvidence(x.Id, x.OriginalAllocationId, Tenant, x.FinancialEffectAmount)).ToArray();
        var position = NpmRentMonthPosition.From(month, receipts, effects);
        return new(month, stall, position, Hash(new { receipts, effects, month.PolicyReference, month.RateEvidenceJson, month.CreditEvidenceJson }), allocations, corrections);
    }

    public async Task<Result<NpmRentMonthQuote>> QuoteAsync(Guid stallId, DateOnly period, DateOnly businessDate,
        NpmRentPaymentScope scope, CancellationToken ct = default, DateOnly? installmentDate = null)
    {
        if (!await Authorized(ct)) return Result<NpmRentMonthQuote>.Forbidden();
        if (businessDate == default || businessDate > clock.PhilippineToday || !Enum.IsDefined(scope))
            return Result<NpmRentMonthQuote>.Failure("Invalid collection date or scope.", ResultStatus.Invalid);
        try
        {
            var (month, stall, position, evidence, allocations, corrections) = await Load(stallId, period, ct);
            if (businessDate < DateOnly.FromDateTime(EEMOCantilanSDS.Domain.Common.PhilippineTime.ToPhilippineTime(month.SettlementActivatedAtUtc!.Value)))
                return Result<NpmRentMonthQuote>.Failure("RECONCILIATION_REQUIRED: A pre-activation cash date cannot be posted through the new month source.", ResultStatus.Conflict);
            if (scope == NpmRentPaymentScope.PartPayment && !partPayments.Allows(month.ContractId))
                return Result<NpmRentMonthQuote>.Failure("General partial payments are not enabled.", ResultStatus.Conflict);
            using var policyEvidence = JsonDocument.Parse(month.RateEvidenceJson);
            if (scope != NpmRentPaymentScope.DailyInstallment && installmentDate is not null)
                return Result<NpmRentMonthQuote>.Failure("Only a daily installment has an installment date.", ResultStatus.Invalid);
            if (period > new DateOnly(businessDate.Year, businessDate.Month, 1)
                && !Approved(policyEvidence, scope == NpmRentPaymentScope.WholeMonth ? "advanceWholeMonthApproved" : "advancePartPaymentsApproved"))
                return Result<NpmRentMonthQuote>.Failure("Advance collection requires approved source policy evidence.", ResultStatus.Conflict);
            var amount = position.WholePayment;
            decimal? approvedDailyInstallment = null;
            if (scope == NpmRentPaymentScope.PartPayment)
            {
                if (!Approved(policyEvidence, "partPaymentsApproved") || businessDate != clock.PhilippineToday)
                    return Result<NpmRentMonthQuote>.Failure("An approved partial-payment policy and earned assessment reader are required.", ResultStatus.Conflict);
                amount = await EarnedAmount(month, stall, position, businessDate, policyEvidence, ct);
                if (OpeningEvidence(month) is null && !policyEvidence.RootElement.TryGetProperty("monthlyObligationDueFrom", out _))
                {
                    if (assessments is null) throw new InvalidOperationException("The approved earned collection reader is unavailable.");
                    var limit = await assessments.ResolveEarnedCollectionLimitAsync(stall, period.Year, period.Month, ct);
                    amount = Math.Min(amount, Math.Max(0, limit - position.LegacyCash - position.CanonicalCash));
                }
            }
            if (scope == NpmRentPaymentScope.DailyInstallment)
            {
                var day = installmentDate ?? businessDate;
                if (!Approved(policyEvidence, "dailyInstallmentsApproved") || period.Year != day.Year || period.Month != day.Month || day > businessDate)
                    return Result<NpmRentMonthQuote>.Failure("No approved daily installment arrangement applies.", ResultStatus.Conflict);
                var owner = StallOccupancy.AnsweringForMonth(stall.Occupancies(clock.PhilippineToday), period.Year, period.Month)!;
                var oldDay = await db.DailyCollections.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.StallId == stallId && x.CollectionDate == day, ct);
                var unavailable = day < owner.Start || day > owner.BillableEnd || oldDay is { IsPaid: true } or { IsAbsent: true } or { IsPartlyPaid: true }
                    || (await closures.GetByMonthAsync(day.Year, day.Month, ct)).Any(x => x.ClosureDate == day);
                var sameDayNet = 0m;
                foreach (var a in allocations.Where(x => x.SourceKind == CollectionSourceKind.NpmRentMonth))
                {
                    using var recorded = JsonDocument.Parse(a.SourceSnapshot ?? "");
                    if (recorded.RootElement.TryGetProperty("installmentDate", out var date) && date.ValueKind == JsonValueKind.String
                        && date.GetString() == day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                        sameDayNet += a.Amount + corrections.Where(x => x.OriginalAllocationId == a.Id).Sum(x => x.FinancialEffectAmount);
                }
                var fee = OpeningEvidence(month)?.ApprovedDailyInstallment
                    ?? NpmDailyFee.ForStall(stall, await rates.GetSnapshotAsync(ct), day);
                approvedDailyInstallment = fee;
                amount = !unavailable && fee > sameDayNet ? Math.Min(position.Outstanding, fee - sameDayNet) : 0m;
                installmentDate = day;
            }
            var classification = await db.RevenueClassifications.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant
                && x.SemanticCode == RevenueClassificationCodes.PermanentStallRent && x.IsActive, ct);
            var policy = classification is null ? null : await db.RevenueClassificationPolicies.AsNoTracking().Where(x => x.MunicipalityId == Tenant
                && x.RevenueClassificationId == classification.Id && x.BusinessContext == RevenuePolicyContext.Default && x.EffectiveDate <= businessDate)
                .OrderByDescending(x => x.EffectiveDate).FirstOrDefaultAsync(ct);
            if (policy?.PermittedInstrumentType is not { } instrument)
                return Result<NpmRentMonthQuote>.Failure("No approved effective stall-rent collection policy.", ResultStatus.Conflict);
            var token = Hash(new { Tenant, month.Id, month.ContractId, month.SettlementVersion, businessDate, scope,
                position.Assessment, position.Credits, position.CanonicalCash, position.LegacyCash, position.HistoricalExcess, amount, evidence,
                policyId = policy.Id, instrument, installmentDate, approvedDailyInstallment });
            return Result<NpmRentMonthQuote>.Success(new(month.Id, stallId, month.ContractId, period, businessDate, scope,
                position.Version, position.Assessment, position.Credits, position.LegacyCash + position.CanonicalCash,
                position.Outstanding, amount, position.HistoricalExcess, token, instrument, installmentDate, approvedDailyInstallment, OpeningEvidence(month)));
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or ArgumentException)
        {
            return Result<NpmRentMonthQuote>.Failure("RECONCILIATION_REQUIRED: " + ex.Message, ResultStatus.Conflict);
        }
    }

    public async Task<Result<NpmRentMonthPaymentOutcome>> PostAsync(NpmRentMonthPayment request, CancellationToken ct = default)
    {
        if (!await Authorized(ct)) return Result<NpmRentMonthPaymentOutcome>.Forbidden();
        if (request.ClientOperationId == Guid.Empty || request.Amount <= 0m || decimal.Round(request.Amount, 2) != request.Amount
            || string.IsNullOrWhiteSpace(request.QuoteToken))
            return Result<NpmRentMonthPaymentOutcome>.Failure("A valid received amount and reviewed quote are required.", ResultStatus.Invalid);
        var actor = user.UserId!.Value.ToString("N");
        var intent = JsonSerializer.Serialize(new { version = 1, Tenant, actor, request.StallId, request.PeriodStart,
            request.BusinessDate, request.Scope, request.Amount, request.QuoteToken, request.InstallmentDate }, Json)
            + NpmReceiptEvidence.IntentSuffix(request.OfficialReceiptNumber);
        var fingerprint = PostingOperation.ComputeIntentFingerprint(1, intent, Origin, actor);
        var prior = await db.PostingOperations.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.ClientOperationId == request.ClientOperationId, ct);
        if (prior is not null)
        {
            if (prior.Origin != Origin || prior.ActorId != actor || prior.IntentFingerprint != fingerprint || prior.CollectionId is null
                || prior.Status != PostingOperationStatus.Succeeded)
                return Result<NpmRentMonthPaymentOutcome>.Failure("IDEMPOTENCY CONFLICT: operation intent changed.", ResultStatus.Conflict);
            var saved = await db.Collections.AsNoTracking().SingleAsync(x => x.MunicipalityId == Tenant && x.Id == prior.CollectionId, ct);
            return Result<NpmRentMonthPaymentOutcome>.Success(new(saved.Id, saved.ReferenceCode, saved.TotalAmount, true));
        }
        await using var transaction = db.HasActiveTransaction ? null : await db.BeginSerializableTransactionAsync(ct);
        var quote = await QuoteAsync(request.StallId, request.PeriodStart, request.BusinessDate, request.Scope, ct, request.InstallmentDate);
        if (!quote.IsSuccess) return Result<NpmRentMonthPaymentOutcome>.Failure(quote.Error!, quote.Status);
        var q = quote.Value!;
        if (q.Token != request.QuoteToken || request.Amount > q.EligibleAmount || q.HistoricalExcess != 0
            || request.Scope == NpmRentPaymentScope.WholeMonth && request.Amount != q.Outstanding
            || request.Scope == NpmRentPaymentScope.DailyInstallment && request.Amount != q.EligibleAmount)
            return Result<NpmRentMonthPaymentOutcome>.Failure("RECONCILIATION_REQUIRED: quote or eligible balance changed.", ResultStatus.Conflict);
        var staged = await BuildPaymentDraftAsync(request, q, ct);
        var receipt = await NpmReceiptEvidence.ResolveAsync(db, user, Tenant, q.Instrument, request.OfficialReceiptNumber, ct);
        if (!receipt.IsSuccess) return Result<NpmRentMonthPaymentOutcome>.Failure(receipt.Error!, receipt.Status);
        try
        {
            var collection = await new CanonicalCollectionPostingCoordinator(db, clock).PostAsync(Tenant, request.ClientOperationId, 1,
                intent, Origin, actor, user.Username!, user.Role!, request.BusinessDate, user.Username!, [staged.Line], receipt.Value,
                collectorId: user.CollectorId, payerName: staged.Payor,
                beforeCommit: (_, recordedAt) => staged.RecordEvidence(recordedAt),
                ct: ct, npmMonthPermit: new(Tenant, request.ClientOperationId, q.MonthId));
            if (transaction is not null)
            {
                await transaction.CommitAsync(ct);
                await InvalidateAfterCommitAsync(ct);
            }
            return Result<NpmRentMonthPaymentOutcome>.Success(new(collection.Id, collection.ReferenceCode, collection.TotalAmount, false));
        }
        catch (Exception ex) when (ex is DbUpdateException or System.Data.Common.DbException
            or InvalidOperationException { InnerException: DbUpdateException or System.Data.Common.DbException })
        {
            db.ChangeTracker.Clear();
            return Result<NpmRentMonthPaymentOutcome>.Failure("RECONCILIATION_REQUIRED: concurrent payment or uncertain response; retry the same operation identity.", ResultStatus.Conflict);
        }
    }

    internal sealed record StagedMonthPayment(Guid MonthId, CollectionLineDraft Line, string Payor, Action<DateTime> RecordEvidence);

    internal async Task<Result<StagedMonthPayment>> StageReviewedPaymentAsync(NpmRentMonthPayment request, CancellationToken ct)
    {
        var quote = await QuoteAsync(request.StallId, request.PeriodStart, request.BusinessDate, request.Scope, ct, request.InstallmentDate);
        if (!quote.IsSuccess) return Result<StagedMonthPayment>.Failure(quote.Error!, quote.Status);
        var q = quote.Value!;
        if (request.ClientOperationId == Guid.Empty || request.Amount <= 0 || decimal.Round(request.Amount, 2) != request.Amount
            || q.Token != request.QuoteToken || request.Amount > q.EligibleAmount || q.HistoricalExcess != 0
            || request.Scope == NpmRentPaymentScope.WholeMonth && request.Amount != q.Outstanding
            || request.Scope == NpmRentPaymentScope.DailyInstallment && request.Amount != q.EligibleAmount)
            return Result<StagedMonthPayment>.Failure("RECONCILIATION_REQUIRED: quote or eligible balance changed.", ResultStatus.Conflict);
        return Result<StagedMonthPayment>.Success(await BuildPaymentDraftAsync(request, q, ct));
    }

    private async Task<StagedMonthPayment> BuildPaymentDraftAsync(NpmRentMonthPayment request, NpmRentMonthQuote q, CancellationToken ct)
    {
        var source = await db.NpmRentMonths.SingleAsync(x => x.MunicipalityId == Tenant && x.Id == q.MonthId, ct);
        var responsibleContract = await db.Contracts.AsNoTracking().SingleAsync(x => x.MunicipalityId == Tenant
            && x.Id == source.ContractId && x.StallId == source.StallId, ct);
        var sourceStall = await db.Stalls.AsNoTracking().Include(x => x.Facility)
            .SingleAsync(x => x.MunicipalityId == Tenant && x.Id == source.StallId, ct);
        var classification = await db.RevenueClassifications.SingleAsync(x => x.MunicipalityId == Tenant
            && x.SemanticCode == RevenueClassificationCodes.PermanentStallRent && x.IsActive, ct);
        var policy = await db.RevenueClassificationPolicies.Where(x => x.MunicipalityId == Tenant && x.RevenueClassificationId == classification.Id
            && x.BusinessContext == RevenuePolicyContext.Default && x.EffectiveDate <= request.BusinessDate).OrderByDescending(x => x.EffectiveDate).FirstAsync(ct);
        var snapshot = JsonSerializer.Serialize(new { schemaVersion = 1, source.Id, source.StallId, source.ContractId, source.PeriodStart,
            source.AssessedAmount, source.CreditedAmount, source.PolicyReference, source.RateEvidenceJson, source.CreditEvidenceJson,
            actualOccupant = responsibleContract.ActualOccupant, spaceNo = sourceStall.StallNo,
            sectionKey = sourceStall.Section?.ToString() ?? (sourceStall.CustomSectionName is { } customSection ? "custom:" + customSection : null),
            facilityCode = sourceStall.Facility!.Code, facilityName = sourceStall.Facility.Name,
            q.Version, scope = request.Scope.ToString(),
            installmentDate = q.InstallmentDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            approvedDailyInstallment = q.ApprovedDailyInstallment, applied = request.Amount }, Json);
        var line = new CollectionLineDraft(classification, policy, request.Amount, CollectionSourceKind.NpmRentMonth, source.Id, null,
            snapshot, [new(CollectionSourceKind.NpmRentMonth, source.Id, request.Amount, SourceSnapshot: snapshot)]);
        return new(source.Id, line, responsibleContract.ActualOccupant, recordedAt => db.AuditLogs.Add(source.RecordSettlementChange(q.Version,
            user.UserId!.Value, user.Username!, user.Role!, recordedAt, "Canonical month payment " + request.ClientOperationId)));
    }

    private async Task<decimal> EarnedAmount(NpmRentMonth month, Stall stall, NpmRentMonthPosition position,
        DateOnly businessDate, JsonDocument policy, CancellationToken ct)
        => Math.Min(position.Outstanding, Math.Max(0m, await EarnedLiability(month, stall, position, businessDate, policy, ct)
            - position.LegacyCash - position.CanonicalCash));

    private async Task<decimal> EarnedLiability(NpmRentMonth month, Stall stall, NpmRentMonthPosition position,
        DateOnly businessDate, JsonDocument policy, CancellationToken ct)
    {
        if (OpeningEvidence(month) is { } opening)
            return position.EarnedInstallmentLiability(opening.ApprovedDailyInstallment, opening.OccupancyStart, businessDate);
        if (policy.RootElement.TryGetProperty("monthlyObligationDueFrom", out var due))
        {
            if (due.ValueKind != JsonValueKind.String || !DateOnly.TryParseExact(due.GetString(), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dueDate))
                throw new InvalidOperationException("Approved monthly due-date evidence is malformed.");
            return businessDate >= dueDate ? position.Assessment - position.Credits : 0m;
        }
        if (!Approved(policy, "dailyInstallmentsApproved"))
            throw new InvalidOperationException("Monthly-only earned balances need an approved due-date policy.");
        if (month.PeriodStart.AddMonths(1) <= businessDate) return position.Assessment - position.Credits;
        if (assessments is null) throw new InvalidOperationException("The existing earned assessment reader is unavailable.");
        var earned = await assessments.ResolveAssessmentAsync(stall, month.PeriodStart.Year, month.PeriodStart.Month, false, ct);
        return Math.Min(position.Assessment - position.Credits, Math.Max(0m, earned.Obligation - earned.Credits));
    }

    private static bool Approved(JsonDocument doc, string key) => doc.RootElement.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.True;
    internal static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json))));
    private static NpmRentMonthOpeningEvidence ParseOpening(string? text)
    {
        var result = JsonSerializer.Deserialize<NpmRentMonthOpeningEvidence>(text ?? "", Json);
        if (result is null || result.SchemaVersion != 1 || result.LegacyDailyIds is null || result.CanonicalDailyIds is null
            || result.LegacyDailyIds.Any(x => x == Guid.Empty) || result.CanonicalDailyIds.Any(x => x == Guid.Empty)
            || result.LegacyDailyIds.Distinct().Count() != result.LegacyDailyIds.Length
            || result.CanonicalDailyIds.Distinct().Count() != result.CanonicalDailyIds.Length
            || result.LegacyDailyIds.Intersect(result.CanonicalDailyIds).Any())
            throw new InvalidOperationException("Explicit reviewed opening manifest is required.");
        return result;
    }
}

public sealed record NpmRentMonthOpeningEvidence(int SchemaVersion, Guid[] LegacyDailyIds, Guid[] CanonicalDailyIds, string LegacyFingerprint);

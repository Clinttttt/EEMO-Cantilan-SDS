using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>Authoritative Water/WCF adapter and entry workflow over UtilityBill SourcePart.Water.</summary>
public sealed class WcfCollectionWorkflow(
    IAppDbContext db,
    ICurrentUserService currentUser,
    ICurrentMunicipalityAccessor municipality,
    IClock? clock = null)
{
    private const string WebOrigin = "WebWcf";
    private const string MobileOrigin = "MobileWcf";
    private const int IntentVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private DateOnly BusinessToday => clock?.PhilippineToday ?? PhilippineTime.Today;

    public Task<Result<IReadOnlyList<WcfObligationQuoteDto>>> GetObligationsAsync(
        int throughYear, int throughMonth, CancellationToken ct = default) => Run(async actor =>
    {
        if (actor.Role == "Collector"
            && !await CollectorHasOperationAssignmentAsync(actor.TenantId, actor.UserId, CollectorOperationCodes.Wcf, ct))
            return Result<IReadOnlyList<WcfObligationQuoteDto>>.Forbidden();
        if (throughYear is < 2000 or > 2200 || throughMonth is < 1 or > 12)
            return Result<IReadOnlyList<WcfObligationQuoteDto>>.Failure("A valid billing year and month are required.", ResultStatus.Invalid);
        var policy = await ResolvePolicyAsync(actor.TenantId, BusinessToday, ct);
        var bills = await UtilityBillQuery(actor.TenantId, tracked: false)
            .Where(x => x.Stall!.Facility!.Code == FacilityCode.NPM
                && (x.BillingYear < throughYear
                || (x.BillingYear == throughYear && x.BillingMonth <= throughMonth)))
            .OrderBy(x => x.BillingYear).ThenBy(x => x.BillingMonth).ThenBy(x => x.Stall!.StallNo)
            .ToListAsync(ct);
        var result = new List<WcfObligationQuoteDto>();
        foreach (var bill in bills)
        {
            var facts = await BuildFactsAsync(bill, policy, actor.TenantId, BusinessToday, ct);
            if (facts.Quote.OutstandingAmount > 0m
                || facts.Quote.SettlementAuthority == SettlementAuthority.PendingCutover)
                result.Add(facts.Quote);
        }
        return Result<IReadOnlyList<WcfObligationQuoteDto>>.Success(result);
    }, ct, requireNpmAuthorityForCollector: false);

    /// <summary>
    /// The Water sources a Head/Admin may set up for one billing period: NPM stalls with an occupancy answering for that
    /// month (the current Water source context), each with its Water part as it stands for the period.
    /// </summary>
    public Task<Result<IReadOnlyList<WcfSetupSourceDto>>> GetSetupSourcesAsync(
        int billingYear, int billingMonth, CancellationToken ct = default) => Run(async actor =>
    {
        if (actor.Role is not ("Admin" or "SuperAdmin"))
            return Result<IReadOnlyList<WcfSetupSourceDto>>.Forbidden();
        if (billingYear is < 2000 or > 2200 || billingMonth is < 1 or > 12)
            return Result<IReadOnlyList<WcfSetupSourceDto>>.Failure("A valid billing year and month are required.", ResultStatus.Invalid);

        var stalls = await db.Stalls.AsNoTracking()
            .Include(x => x.Facility)
            .Include(x => x.Contracts).ThenInclude(x => x.Payor)
            .Where(x => x.MunicipalityId == actor.TenantId && x.Facility!.Code == FacilityCode.NPM)
            .OrderBy(x => x.StallNo)
            .ToListAsync(ct);
        var stallIds = stalls.Select(x => x.Id).ToArray();
        var bills = await db.UtilityBills.AsNoTracking()
            .Where(x => x.MunicipalityId == actor.TenantId && stallIds.Contains(x.StallId)
                && x.BillingYear == billingYear && x.BillingMonth == billingMonth)
            .ToDictionaryAsync(x => x.StallId, ct);

        var rows = new List<WcfSetupSourceDto>();
        foreach (var stall in stalls)
        {
            var contract = stall.OccupancyAnsweringForMonth(billingYear, billingMonth, BusinessToday)?.Contract;
            if (contract is null) continue;   // no payor answers for this month, so there is no Water obligation to set up
            var payer = contract.PayorId is not null && contract.Payor?.MunicipalityId == actor.TenantId
                ? contract.Payor.DisplayName
                : string.IsNullOrWhiteSpace(contract.ActualOccupant) ? null : contract.ActualOccupant.Trim();
            bills.TryGetValue(stall.Id, out var bill);
            var locked = bill is null ? null : WaterLockReason(bill);
            rows.Add(new WcfSetupSourceDto(
                stall.Id, stall.StallNo, SectionOf(stall), payer,
                bill?.Id, bill is null || bill.WaterCharge == 0m ? null : bill.WaterCharge,
                bill is null ? null : bill.WaterCalculationBasis == UtilityCalculationBasis.DirectApproved ? "DirectApproved" : "Metered",
                bill?.WaterSettlementAuthorityState, bill?.WaterAmountPaid ?? 0m, locked is null, locked));
        }
        return Result<IReadOnlyList<WcfSetupSourceDto>>.Success(rows);
    }, ct);

    /// <summary>
    /// Establishes, or revises before any settlement, the direct approved Water amount for one source and period (IA-053).
    /// Uses the one stall/month UtilityBill — creating it only when none exists — and changes only its Water part, so an
    /// Electricity amount on the same bill is never overwritten. A settled, partly settled or cutover Water part is frozen.
    /// A new Water part starts under Legacy settlement authority: Mobile collection requires the approved cutover.
    /// </summary>
    public Task<Result<WcfSetupSourceDto>> EstablishObligationAsync(
        WcfObligationSetupRequest request, CancellationToken ct = default) => Run(async actor =>
    {
        if (actor.Role is not ("Admin" or "SuperAdmin"))
            return Result<WcfSetupSourceDto>.Forbidden();
        if (request.BillingYear is < 2000 or > 2200 || request.BillingMonth is < 1 or > 12)
            return Result<WcfSetupSourceDto>.Failure("A valid billing year and month are required.", ResultStatus.Invalid);
        if (new DateOnly(request.BillingYear, request.BillingMonth, 1) > new DateOnly(BusinessToday.Year, BusinessToday.Month, 1))
            return Result<WcfSetupSourceDto>.Failure("A Water obligation cannot be set up for a period that has not begun.", ResultStatus.Invalid);
        if (request.ApprovedAmount <= 0m || request.ApprovedAmount > 1_000_000m
            || decimal.Round(request.ApprovedAmount, 2, MidpointRounding.ToZero) != request.ApprovedAmount)
            return Result<WcfSetupSourceDto>.Failure("Enter a positive approved amount with at most two decimal places.", ResultStatus.Invalid);

        var stall = await db.Stalls
            .Include(x => x.Facility)
            .Include(x => x.Contracts).ThenInclude(x => x.Payor)
            .SingleOrDefaultAsync(x => x.MunicipalityId == actor.TenantId && x.Id == request.StallId, ct);
        if (stall is null)
            return Result<WcfSetupSourceDto>.NotFound();
        // The current Water source is an NPM-bound UtilityBill: NPM is its context here, never a collector authorization.
        if (stall.Facility?.Code != FacilityCode.NPM)
            return Result<WcfSetupSourceDto>.Failure("The current Water source applies to New Public Market stalls only.", ResultStatus.Invalid);
        var contract = stall.OccupancyAnsweringForMonth(request.BillingYear, request.BillingMonth, BusinessToday)?.Contract;
        if (contract is null)
            return Result<WcfSetupSourceDto>.Failure("No occupancy answers for this stall and month, so there is no payor to owe the Water amount.", ResultStatus.Invalid);

        var (previous, current, rate) = UtilityBill.DirectApprovedReadings(request.ApprovedAmount);
        var bill = await db.UtilityBills.SingleOrDefaultAsync(x => x.MunicipalityId == actor.TenantId
            && x.StallId == stall.Id && x.BillingYear == request.BillingYear && x.BillingMonth == request.BillingMonth, ct);
        if (bill is null)
        {
            bill = UtilityBill.Create(stall.Id, request.BillingYear, request.BillingMonth,
                0m, 0m, 0m, previous, current, rate, actor.Username);
            bill.SetCalculationBasis(UtilityCalculationBasis.DirectApproved, UtilityCalculationBasis.DirectApproved);
            db.UtilityBills.Add(bill);
        }
        else
        {
            if (WaterLockReason(bill) is { } reason)
                return Result<WcfSetupSourceDto>.Failure(reason, ResultStatus.Conflict);
            // Electricity is passed through exactly as it stands, so this writer can never change it.
            bill.UpdateReadings(bill.ElecPreviousReading, bill.ElecCurrentReading, bill.ElecRatePerKwh,
                previous, current, rate, bill.Remarks, actor.Username);
            bill.SetCalculationBasis(bill.ElecCalculationBasis, UtilityCalculationBasis.DirectApproved);
        }
        await db.SaveChangesAsync(ct);

        var payer = contract.PayorId is not null && contract.Payor?.MunicipalityId == actor.TenantId
            ? contract.Payor.DisplayName
            : string.IsNullOrWhiteSpace(contract.ActualOccupant) ? null : contract.ActualOccupant.Trim();
        return Result<WcfSetupSourceDto>.Success(new WcfSetupSourceDto(
            stall.Id, stall.StallNo, SectionOf(stall), payer, bill.Id, bill.WaterCharge, "DirectApproved",
            bill.WaterSettlementAuthorityState, bill.WaterAmountPaid, WaterLockReason(bill) is null, WaterLockReason(bill)));
    }, ct);

    /// <summary>
    /// Every eligible Water source for a billing period — NPM stalls with an occupancy answering for that month — with where
    /// its Water part stands for Collector Mobile: no amount yet (the collector may enter one once WCF is enabled), an
    /// office-prepared amount to collect against, settled, or needing the office (legacy settlement or migration).
    /// </summary>
    public Task<Result<IReadOnlyList<WcfMobileSourceDto>>> GetMobileSourcesAsync(
        int billingYear, int billingMonth, CancellationToken ct = default) => Run(async actor =>
    {
        if (actor.Role == "Collector"
            && !await CollectorHasOperationAssignmentAsync(actor.TenantId, actor.UserId, CollectorOperationCodes.Wcf, ct))
            return Result<IReadOnlyList<WcfMobileSourceDto>>.Forbidden();
        if (billingYear is < 2000 or > 2200 || billingMonth is < 1 or > 12)
            return Result<IReadOnlyList<WcfMobileSourceDto>>.Failure("A valid billing year and month are required.", ResultStatus.Invalid);

        var active = await IsWcfActiveAsync(actor.TenantId, ct);
        var policy = await ResolvePolicyAsync(actor.TenantId, BusinessToday, ct);
        var stalls = await db.Stalls.AsNoTracking()
            .Include(x => x.Facility)
            .Include(x => x.Contracts).ThenInclude(x => x.Payor)
            .Where(x => x.MunicipalityId == actor.TenantId && x.Facility!.Code == FacilityCode.NPM)
            .OrderBy(x => x.StallNo)
            .ToListAsync(ct);
        var stallIds = stalls.Select(x => x.Id).ToArray();
        var bills = (await UtilityBillQuery(actor.TenantId, tracked: false)
                .Where(x => stallIds.Contains(x.StallId) && x.BillingYear == billingYear && x.BillingMonth == billingMonth)
                .ToListAsync(ct))
            .ToDictionary(x => x.StallId);

        var rows = new List<WcfMobileSourceDto>();
        foreach (var stall in stalls)
        {
            var contract = stall.OccupancyAnsweringForMonth(billingYear, billingMonth, BusinessToday)?.Contract;
            if (contract is null) continue;
            var payorId = contract.PayorId is not null && contract.Payor?.MunicipalityId == actor.TenantId ? contract.PayorId : null;
            var payer = payorId is not null ? contract.Payor!.DisplayName
                : string.IsNullOrWhiteSpace(contract.ActualOccupant) ? null : contract.ActualOccupant.Trim();
            bills.TryGetValue(stall.Id, out var bill);

            string state;
            decimal? prepared = null;
            decimal settled = 0m, outstanding = 0m;
            var canCollect = false;
            var canDirect = false;
            if (bill is null || (bill.WaterCharge == 0m && bill.WaterSettlementAuthorityState == SettlementAuthority.Legacy
                && bill.WaterStatus == PaymentStatus.Unpaid && bill.WaterAmountPaid == 0m))
            {
                state = WcfSourceState.NoAmount;
                canDirect = active;
            }
            else if (bill.WaterSettlementAuthorityState == SettlementAuthority.Canonical)
            {
                var facts = await BuildFactsAsync(bill, policy, actor.TenantId, BusinessToday, ct);
                prepared = bill.WaterCharge;
                settled = facts.Quote.CumulativeSettledEvidence;
                outstanding = facts.Quote.OutstandingAmount;
                state = outstanding > 0m ? WcfSourceState.Prepared : WcfSourceState.Settled;
                canCollect = outstanding > 0m;
            }
            else if (bill.WaterSettlementAuthorityState == SettlementAuthority.Legacy
                && bill.WaterStatus == PaymentStatus.Unpaid && bill.WaterAmountPaid == 0m)
            {
                // Prepared with no legacy money: becomes canonical at its first collection once WCF is enabled.
                prepared = bill.WaterCharge;
                outstanding = bill.WaterCharge;
                state = WcfSourceState.Prepared;
                canCollect = active;
            }
            else
            {
                prepared = bill.WaterCharge;
                settled = bill.WaterAmountPaid;
                outstanding = Math.Max(0m, bill.WaterCharge - bill.WaterAmountPaid);
                state = outstanding > 0m ? WcfSourceState.NeedsOffice : WcfSourceState.Settled;
            }

            rows.Add(new WcfMobileSourceDto(stall.Id, stall.StallNo, SectionOf(stall), payorId, payer,
                billingYear, billingMonth, bill?.Id, bill?.WaterSourceVersion ?? 0, prepared, settled, outstanding,
                state, canCollect, canDirect));
        }
        return Result<IReadOnlyList<WcfMobileSourceDto>>.Success(rows);
    }, ct, requireNpmAuthorityForCollector: false);

    /// <summary>The system safety bound on one Water amount; not a rate and not a business ceiling.</summary>
    private const decimal MaxDirectAmount = 1_000_000m;

    /// <summary>
    /// Establishes the Water amount a collector states for an eligible source and the current billing period (WCF direct
    /// entry). Uses the one stall/month UtilityBill, creating it only when none exists; Electricity is passed through as it
    /// stands. An office-prepared amount always wins: if one exists, nothing is changed and the issued ticket goes to review.
    /// Nothing is saved here — the bill joins the posting's single unit of work, so no assessment is left orphaned.
    /// </summary>
    private async Task<(UtilityBill? Bill, string? Code, string? Message)> EstablishDirectSourceAsync(
        Actor actor, WcfCollectionPostRequest request, CancellationToken ct)
    {
        if (request.BillingYear != request.BusinessDate.Year || request.BillingMonth != request.BusinessDate.Month)
            return (null, "PERIOD_NOT_CURRENT", "A direct Water amount is recorded for the business month of the collection only.");
        if (!await IsWcfActiveAsync(actor.TenantId, ct))
            return (null, "WCF_NOT_ACTIVE", "WCF Mobile collection has not been enabled by the office, so a direct Water amount cannot be recorded yet.");

        var stall = await db.Stalls
            .Include(x => x.Facility)
            .Include(x => x.Contracts).ThenInclude(x => x.Payor)
            .SingleOrDefaultAsync(x => x.MunicipalityId == actor.TenantId && x.Id == request.StallId, ct);
        // The current Water source is an NPM-bound UtilityBill: NPM is source context here, not a collector authorization.
        if (stall is null || stall.Facility?.Code != FacilityCode.NPM)
            return (null, "SOURCE_NOT_FOUND", "The selected Water source is not available in this tenant.");
        if (stall.OccupancyAnsweringForMonth(request.BillingYear!.Value, request.BillingMonth!.Value, request.BusinessDate) is null)
            return (null, "SOURCE_NOT_ELIGIBLE", "No occupancy answers for the selected source in this period.");

        var (previous, current, rate) = UtilityBill.DirectApprovedReadings(request.ReceivedAmount);
        var bill = await UtilityBillQuery(actor.TenantId, tracked: true).SingleOrDefaultAsync(x =>
            x.StallId == stall.Id && x.BillingYear == request.BillingYear && x.BillingMonth == request.BillingMonth, ct);
        if (bill is null)
        {
            bill = UtilityBill.Create(stall.Id, request.BillingYear.Value, request.BillingMonth.Value,
                0m, 0m, 0m, previous, current, rate, actor.Username);
            bill.SetCalculationBasis(UtilityCalculationBasis.DirectApproved, UtilityCalculationBasis.DirectApproved);
            db.UtilityBills.Add(bill);   // the tracked stall above is fixed up as its source context
            return (bill, null, null);
        }
        if (bill.WaterCharge > 0m)
            return (null, "PREPARED_AMOUNT_EXISTS",
                "An office-prepared Water amount already exists for this source and period; it was not changed. The issued Cash Ticket requires office review.");
        if (bill.WaterSettlementAuthorityState != SettlementAuthority.Legacy
            || bill.WaterStatus != PaymentStatus.Unpaid || bill.WaterAmountPaid > 0m)
            return (null, "SOURCE_NOT_ELIGIBLE", "This Water source already has settlement history; it was not changed.");
        bill.UpdateReadings(bill.ElecPreviousReading, bill.ElecCurrentReading, bill.ElecRatePerKwh,
            previous, current, rate, bill.Remarks, actor.Username);
        bill.SetCalculationBasis(bill.ElecCalculationBasis, UtilityCalculationBasis.DirectApproved);
        return (bill, null, null);
    }

    private Task<bool> IsWcfActiveAsync(Guid tenantId, CancellationToken ct) =>
        db.CollectorOperationActivations.AsNoTracking().AnyAsync(x =>
            x.MunicipalityId == tenantId && x.OperationCode == CollectorOperationCodes.Wcf, ct);

    /// <summary>
    /// Prospective canonical authority for a Water part that has never been settled under the legacy path: once WCF Mobile
    /// collection is enabled, such a part becomes Canonical at its first collection, through the existing cutover record
    /// (opening position: the assessment, nothing settled). The record's evidence states the activation, who entered the
    /// amount (office-prepared or collector direct entry) and the posting operation. A part with any legacy settlement is
    /// left Legacy — that is the explicit migration path. Nothing is saved here.
    /// </summary>
    private async Task<bool> TryActivateProspectivelyAsync(
        UtilityBill bill, Actor actor, Guid clientOperationId, bool directEntry, CancellationToken ct)
    {
        if (bill.WaterSettlementAuthorityState != SettlementAuthority.Legacy
            || bill.WaterStatus != PaymentStatus.Unpaid || bill.WaterAmountPaid > 0m || bill.WaterCharge <= 0m)
            return false;
        var activation = await db.CollectorOperationActivations.AsNoTracking().SingleOrDefaultAsync(x =>
            x.MunicipalityId == actor.TenantId && x.OperationCode == CollectorOperationCodes.Wcf, ct);
        if (activation is null) return false;

        var now = clock?.UtcNow ?? DateTime.UtcNow;
        bill.MarkWaterPendingCutover();
        var evidence = JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            Kind = "ProspectiveWcfActivation",
            ActivationId = activation.Id,
            ActivationEffectiveFrom = activation.EffectiveFrom.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            AmountOrigin = directEntry ? "CollectorDirectEntry" : "OfficePrepared",
            ClientOperationId = clientOperationId,
            Actor = actor.Username,
            ActorId = ActorId(actor),
            RecordedAtUtc = now
        }, JsonOptions);
        var cutover = CollectionSettlementCutover.Freeze(actor.TenantId, CollectionSourceKind.UtilityBill, bill.Id,
            CollectionSourcePart.Water, bill.WaterSourceVersion, now, bill.WaterCharge, 0m, bill.WaterCharge,
            evidence, actor.UserId, now);
        db.CollectionSettlementCutovers.Add(cutover);
        bill.ActivateCanonicalWaterSettlement(cutover);
        return true;
    }

    // Why a Water part can no longer be revised here, or null while it is still a Legacy, unsettled assessment.
    private static string? WaterLockReason(UtilityBill bill) =>
        bill.WaterSettlementAuthorityState != SettlementAuthority.Legacy
            ? "This Water obligation has entered cutover or canonical settlement; its approved amount is frozen."
            : bill.WaterStatus != PaymentStatus.Unpaid || bill.WaterAmountPaid > 0m
                ? "Settlement has begun on this Water obligation; its approved amount is frozen."
                : null;

    private static string SectionOf(Stall stall) => stall.Section is { } marketSection
        ? stall.Facility!.SectionLabel(marketSection) ?? stall.CustomSectionName ?? string.Empty
        : stall.CustomSectionName ?? string.Empty;

    public Task<Result<IReadOnlyList<CashTicketDocumentDto>>> GetAvailableCashTicketsAsync(
        CancellationToken ct = default) => Run(async actor =>
    {
        if (actor.Role == "Collector"
            && !await CollectorHasOperationAssignmentAsync(actor.TenantId, actor.UserId, CollectorOperationCodes.Wcf, ct))
            return Result<IReadOnlyList<CashTicketDocumentDto>>.Forbidden();
        var documents = await db.AccountableDocuments.AsNoTracking()
            .Where(x => x.MunicipalityId == actor.TenantId
                && x.InstrumentType == RevenueInstrumentType.CashTicket
                && (actor.Role == "Collector"
                    ? x.State == AccountableDocumentState.Assigned && x.AssignedUserId == actor.UserId
                    : x.State == AccountableDocumentState.InOffice))
            .OrderBy(x => x.SerialNumber)
            .Select(x => new CashTicketDocumentDto(x.Id, x.DocumentNumber, x.State, x.AssignedUserId, x.SerialNumber))
            .ToListAsync(ct);
        return Result<IReadOnlyList<CashTicketDocumentDto>>.Success(documents);
    }, ct, requireNpmAuthorityForCollector: false);

    public Task<Result<IReadOnlyList<WcfReconciliationExceptionDto>>> GetReconciliationExceptionsAsync(
        CancellationToken ct = default) => Run(async actor =>
    {
        var operations = await db.PostingOperations.AsNoTracking()
            .Where(x => x.MunicipalityId == actor.TenantId
                && x.Status == PostingOperationStatus.ReconciliationRequired
                && (x.Origin == MobileOrigin || x.Origin == WebOrigin
                    || x.Origin == "MobileLegacyWaterAfterCutover"
                    || x.Origin == "WebLegacyWaterAfterCutover"))
            .OrderByDescending(x => x.RecordedAtUtc)
            .Take(200)
            .ToListAsync(ct);
        var documentIds = operations.Where(x => x.AccountableDocumentId.HasValue)
            .Select(x => x.AccountableDocumentId!.Value).Distinct().ToArray();
        var documentNumbers = await db.AccountableDocuments.AsNoTracking()
            .Where(x => x.MunicipalityId == actor.TenantId && documentIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DocumentNumber, ct);
        var exceptions = operations.Select(x => new WcfReconciliationExceptionDto(
            x.ClientOperationId, x.Origin, x.RecordedAtUtc,
            x.OutcomeCode ?? "RECONCILIATION_REQUIRED",
            x.OutcomeDetails ?? "Office reconciliation is required.",
            x.AccountableDocumentId,
            x.AccountableDocumentId is { } id ? documentNumbers.GetValueOrDefault(id) : null)).ToList();
        return Result<IReadOnlyList<WcfReconciliationExceptionDto>>.Success(exceptions);
    }, ct);

    public Task<Result<WcfCollectionOutcomeDto>> PostWebAsync(
        WcfCollectionPostRequest request, CancellationToken ct = default) =>
        Run(actor => PostCoreAsync(actor, request, mobile: false, ct), ct);

    public Task<Result<WcfCollectionOutcomeDto>> PostMobileAsync(
        WcfCollectionPostRequest request, CancellationToken ct = default) =>
        Run(actor => PostCoreAsync(actor, request, mobile: true, ct), ct,
            requireNpmAuthorityForCollector: false);

    /// <summary>
    /// Stops old cumulative UtilityBill payloads from mutating a Water source once its scoped cutover
    /// starts. Their submitted evidence remains bound to the original ClientOperationId for office review.
    /// </summary>
    public Task<Result<LegacyWaterSyncResolution>> ReconcileLegacyMobileWaterAsync(
        SyncOfflineOperationDto request, CancellationToken ct = default, bool fromWeb = false) => Run(async actor =>
    {
        if (request.Kind != OfflineOperationKind.NpmUtility
            || request.UtilityBillId is not { } billId)
            return Result<LegacyWaterSyncResolution>.Success(new(false, null));

        var bill = await UtilityBillQuery(actor.TenantId, tracked: true)
            .SingleOrDefaultAsync(x => x.Id == billId, ct);
        if (bill is null || bill.WaterSettlementAuthorityState == SettlementAuthority.Legacy)
            return Result<LegacyWaterSyncResolution>.Success(new(false, null));
        if (request.WaterStatus is null
            && request.WaterPartialAmount is null
            && string.IsNullOrWhiteSpace(request.WaterORNumber))
            return Result<LegacyWaterSyncResolution>.Success(new(false, null, PreserveWaterSource: true));

        if (request.WaterStatus is { } suppliedStatus)
        {
            var suppliedPartial = suppliedStatus == PaymentStatus.Partial
                ? request.WaterPartialAmount ?? 0m : 0m;
            if (suppliedStatus == PaymentStatus.Partial && suppliedPartial >= bill.WaterCharge && bill.WaterCharge > 0m)
            {
                suppliedStatus = PaymentStatus.Paid;
                suppliedPartial = 0m;
            }
            var suppliedDocument = suppliedStatus == PaymentStatus.Unpaid ? null
                : string.IsNullOrWhiteSpace(request.WaterORNumber) ? bill.WaterORNumber : request.WaterORNumber.Trim();
            if (suppliedStatus == bill.WaterStatus
                && suppliedPartial == bill.WaterPartialAmount
                && suppliedDocument == bill.WaterORNumber)
                return Result<LegacyWaterSyncResolution>.Success(new(false, null));
        }

        var operationId = request.ClientOperationId == Guid.Empty
            ? CreateLegacyOperationId(actor.TenantId, ActorId(actor), request)
            : request.ClientOperationId;
        request = request with { ClientOperationId = operationId };
        var origin = fromWeb ? "WebLegacyWaterAfterCutover" : "MobileLegacyWaterAfterCutover";
        var normalized = JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            OperationType = "LegacyCumulativeWaterPayloadAfterCutover",
            TenantId = actor.TenantId,
            ActorId = ActorId(actor),
            request.ClientOperationId,
            request.BusinessDate,
            UtilityBillId = billId,
            SourcePart = CollectionSourcePart.Water.ToString(),
            request.WaterStatus,
            request.WaterPartialAmount,
            DocumentField = "LegacyWaterORNumber",
            SubmittedDocumentNumber = request.WaterORNumber?.Trim(),
            CurrentWaterSourceVersion = bill.WaterSourceVersion,
            CurrentBillingYear = bill.BillingYear,
            CurrentBillingMonth = bill.BillingMonth,
            CurrentAssessment = bill.WaterCharge,
            CurrentLegacyPaidEvidence = bill.WaterAmountPaid,
            CurrentWaterReadings = new { bill.WaterPreviousReading, bill.WaterCurrentReading, bill.WaterConsumption, bill.WaterRatePerCubicMeter }
        }, JsonOptions);
        var fingerprint = PostingOperation.ComputeIntentFingerprint(IntentVersion, normalized, origin, ActorId(actor));
        var prior = await FindOperationAsync(actor.TenantId, request.ClientOperationId, ct);
        if (prior is not null)
        {
            if (prior.Origin != origin || prior.ActorId != ActorId(actor) || prior.IntentFingerprint != fingerprint)
                return Result<LegacyWaterSyncResolution>.Failure(
                    "IDEMPOTENCY CONFLICT: this ClientOperationId is already bound to another posting intent.", ResultStatus.Conflict);
            return Result<LegacyWaterSyncResolution>.Success(new(true,
                prior.OutcomeDetails ?? "The old Water payload is preserved for reconciliation."));
        }

        AccountableDocument? physicalDocument = null;
        if (!string.IsNullOrWhiteSpace(request.WaterORNumber))
        {
            physicalDocument = await db.AccountableDocuments.SingleOrDefaultAsync(x =>
                x.MunicipalityId == actor.TenantId
                && x.InstrumentType == RevenueInstrumentType.CashTicket
                && x.DocumentNumber == request.WaterORNumber.Trim(), ct);
            if (physicalDocument is not null
                && await CanQuarantinePhysicalDocumentAsync(physicalDocument, actor, fromWeb, ct)
                && (physicalDocument.ClientOperationId is null || physicalDocument.ClientOperationId == operationId))
                physicalDocument.MarkPhysicalIssueReconciliationRequired(
                    request.ClientOperationId, clock?.UtcNow ?? DateTime.UtcNow, actor.Username);
        }

        var details = "An older cumulative Water payload arrived after cutover. No Collection or balance was created. "
            + $"Preserved Water status {request.WaterStatus}, cumulative amount evidence {request.WaterPartialAmount?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? "not supplied"}, "
            + $"and legacy document field '{request.WaterORNumber ?? "not supplied"}'. Office reconciliation is required.";
        db.PostingOperations.Add(PostingOperation.Record(actor.TenantId, request.ClientOperationId,
            IntentVersion, normalized, origin, ActorId(actor), PostingOperationStatus.ReconciliationRequired,
            "OLD_MOBILE_PAYLOAD_AFTER_CUTOVER", details, null, physicalDocument?.Id,
            clock?.UtcNow ?? DateTime.UtcNow));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var winner = await FindOperationAsync(actor.TenantId, request.ClientOperationId, ct);
            if (winner is not null && winner.Origin == origin && winner.ActorId == ActorId(actor)
                && winner.IntentFingerprint == fingerprint)
                return Result<LegacyWaterSyncResolution>.Success(new(true,
                    winner.OutcomeDetails ?? "The old Water payload is preserved for reconciliation."));
            return Result<LegacyWaterSyncResolution>.Failure(
                "The old Water operation conflicts with a recorded ClientOperationId; reconcile its original evidence.", ResultStatus.Conflict);
        }
        return Result<LegacyWaterSyncResolution>.Success(new(true, details));
    }, ct);

    public Task<Result<IReadOnlyList<WcfCollectionActivityDto>>> GetActivityAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default) => Run(async actor =>
    {
        if (from > to || to.DayNumber - from.DayNumber > 366)
            return Result<IReadOnlyList<WcfCollectionActivityDto>>.Failure("Choose a valid collection period of no more than 367 days.", ResultStatus.Invalid);
        var matchingIds = db.CollectionLines.AsNoTracking()
            .Where(x => x.MunicipalityId == actor.TenantId
                && x.SourceKind == CollectionSourceKind.UtilityBill
                && x.SourcePart == CollectionSourcePart.Water)
            .Select(x => x.CollectionId);
        var collections = await db.Collections.AsNoTracking()
            .Where(x => x.MunicipalityId == actor.TenantId
                && x.BusinessDate >= from && x.BusinessDate <= to && matchingIds.Contains(x.Id))
            .OrderByDescending(x => x.RecordedAtUtc).ToListAsync(ct);
        var ids = collections.Select(x => x.Id).ToArray();
        var lines = await db.CollectionLines.AsNoTracking()
            .Where(x => x.MunicipalityId == actor.TenantId && ids.Contains(x.CollectionId))
            .OrderBy(x => x.CollectionId).ThenBy(x => x.Id).ToListAsync(ct);
        var lineIds = lines.Select(x => x.Id).ToArray();
        var allocations = await db.CollectionAllocations.AsNoTracking()
            .Where(x => x.MunicipalityId == actor.TenantId && lineIds.Contains(x.CollectionLineId))
            .ToListAsync(ct);
        var documents = await db.AccountableDocuments.AsNoTracking()
            .Where(x => x.MunicipalityId == actor.TenantId
                && x.CollectionId.HasValue && ids.Contains(x.CollectionId.Value))
            .ToListAsync(ct);
        var policyIds = lines.Select(x => x.RevenueClassificationPolicyId).Distinct().ToArray();
        var names = await db.RevenueClassificationPolicies.AsNoTracking()
            .Where(x => x.MunicipalityId == actor.TenantId && policyIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        var correctionEffects = await db.CollectionCorrections.AsNoTracking()
            .Where(x => x.MunicipalityId == actor.TenantId && ids.Contains(x.OriginalCollectionId))
            .Select(x => new { x.OriginalCollectionId, x.FinancialEffectAmount, x.CorrectionType })
            .ToListAsync(ct);
        var activity = collections.Select(collection =>
        {
            var collectionLines = lines.Where(x => x.CollectionId == collection.Id).ToList();
            var detail = collectionLines.Select(line =>
            {
                var lineAllocations = allocations.Where(x => x.CollectionLineId == line.Id).ToList();
                var snapshots = lineAllocations.Where(x => x.SourceSnapshot != null)
                    .Select(x => x.SourceSnapshot!).ToList();
                var evidence = lineAllocations.Select(x => ReadPeriod(x.SourceSnapshot))
                    .FirstOrDefault(x => x.Year.HasValue || x.Month.HasValue);
                return new WcfCollectionActivityLineDto(
                    names.GetValueOrDefault(line.RevenueClassificationPolicyId, "Collection item"),
                    line.Amount, evidence.Year, evidence.Month,
                    lineAllocations.Count == 0 ? null : "UtilityBill / Water",
                    line.CalculationSnapshot, snapshots);
            }).ToList();
            var corrections = correctionEffects.Where(x => x.OriginalCollectionId == collection.Id).ToList();
            var disposition = corrections.Any(x => x.FinancialEffectAmount < 0m)
                ? "Reversed"
                : corrections.Any(x => x.CorrectionType == CollectionCorrectionType.DocumentCorrection)
                    ? "Document corrected" : "Posted";
            return new WcfCollectionActivityDto(
                collection.Id, collection.BusinessDate, collection.RecordedAtUtc,
                documents.FirstOrDefault(x => x.CollectionId == collection.Id)?.DocumentNumber ?? "Cash Ticket unavailable",
                collection.PayerName, collection.TotalAmount, detail.Count, disposition, detail);
        }).ToList();
        return Result<IReadOnlyList<WcfCollectionActivityDto>>.Success(activity);
    }, ct);

    private async Task<Result<WcfCollectionOutcomeDto>> PostCoreAsync(
        Actor actor, WcfCollectionPostRequest request, bool mobile, CancellationToken ct)
    {
        if (mobile && actor.Role != "Collector")
            return Result<WcfCollectionOutcomeDto>.Forbidden();
        if (!mobile && actor.Role == "Collector")
            return Result<WcfCollectionOutcomeDto>.Forbidden();
        if (request.ClientOperationId == Guid.Empty)
            return Result<WcfCollectionOutcomeDto>.Failure("A valid ClientOperationId is required.", ResultStatus.Invalid);

        var origin = mobile ? MobileOrigin : WebOrigin;
        var normalized = NormalizeIntent(actor, request);
        var fingerprint = PostingOperation.ComputeIntentFingerprint(IntentVersion, normalized, origin, ActorId(actor));
        var prior = await FindOperationAsync(actor.TenantId, request.ClientOperationId, ct);
        if (prior is not null)
            return await ResolvePriorAsync(prior, fingerprint, origin, actor, ct);

        var document = request.AccountableDocumentId == Guid.Empty ? null
            : await db.AccountableDocuments.SingleOrDefaultAsync(x =>
                x.MunicipalityId == actor.TenantId && x.Id == request.AccountableDocumentId, ct);
        // Routine WCF field collection belongs to the assigned collector on Collector Mobile; Head/Admin Web is
        // monitoring and reconciliation only. A Web intent already bound to an operation still replays above, but a
        // new one is durably rejected. The office ticket was never issued by this request, so it stays in stock.
        if (!mobile)
            return await RecordTerminalAsync(actor, request, normalized, origin, document, mobile,
                "WEB_CHANNEL_RETIRED",
                "Routine WCF collection is recorded by the assigned collector on Collector Mobile. Web WCF posting is retired; no Collection was created.", ct);
        // WCF is a utility operation (IA-053): the collector is authorized by the WCF operation assignment alone. NPM is
        // only the current Water source's context, checked on the source below, never a collector authorization.
        if (mobile && !await CollectorHasOperationAssignmentAsync(
                actor.TenantId, actor.UserId, CollectorOperationCodes.Wcf, ct))
            return await RecordTerminalAsync(actor, request, normalized, origin, document, mobile,
                "COLLECTOR_OPERATION_NOT_ASSIGNED",
                "An explicit WCF operation assignment is required. Any physically issued Cash Ticket is retained for office reconciliation.", ct);
        var collectorOwnsDocument = mobile && document?.State == AccountableDocumentState.Assigned
            && document.AssignedUserId == actor.UserId;
        var validCustody = mobile
            ? collectorOwnsDocument
            : actor.Role is "Admin" or "SuperAdmin"
                && document?.State == AccountableDocumentState.InOffice;

        if (request.SchemaVersion != 1)
            return await RecordTerminalAsync(actor, request, normalized, origin, document, mobile,
                "PAYLOAD_VERSION_UNSUPPORTED", "This WCF collection payload version is not supported.", ct);
        // A direct entry (Collector Mobile only) names a source and period instead of a prepared UtilityBill.
        var direct = request.UtilityBillId == Guid.Empty;
        if (request.AccountableDocumentId == Guid.Empty
            || request.ReceivedAmount <= 0m
            || request.ReceivedAmount > MaxDirectAmount
            || decimal.Round(request.ReceivedAmount, 2, MidpointRounding.ToZero) != request.ReceivedAmount
            || (direct
                ? !mobile || request.StallId is not { } stallId || stallId == Guid.Empty
                    || request.BillingYear is not (>= 2000 and <= 2200) || request.BillingMonth is not (>= 1 and <= 12)
                : request.WaterSourceVersion <= 0))
            return await RecordTerminalAsync(actor, request, normalized, origin, document, mobile,
                "INVALID_INTENT", "WCF requires a positive received amount, a Water source (or a source and period for a direct amount), and Cash Ticket identity.", ct);
        if (request.BusinessDate > BusinessToday)
            return await RecordTerminalAsync(actor, request, normalized, origin, document, mobile,
                "FUTURE_BUSINESS_DATE", "Collection BusinessDate cannot be later than the current Philippine business date.", ct);
        if (mobile && (request.IssuedAtUtc is not { Kind: DateTimeKind.Utc } issuedAt || issuedAt > DateTime.UtcNow))
            return await RecordTerminalAsync(actor, request, normalized, origin, document, mobile,
                "INVALID_ISSUE_TIME", "The physical Cash Ticket issue time must be a valid past or present UTC timestamp.", ct);
        if (document is null)
            return await RecordTerminalAsync(actor, request, normalized, origin, null, mobile,
                "DOCUMENT_NOT_FOUND", "The Cash Ticket identity was not found in this tenant.", ct);
        if (!validCustody)
            return await RecordTerminalAsync(actor, request, normalized, origin, document, mobile,
                "DOCUMENT_CUSTODY_INVALID", "This Cash Ticket is not available to the current collector or office user.", ct);
        if (document.InstrumentType != RevenueInstrumentType.CashTicket
            || !string.Equals(document.DocumentNumber, request.DocumentNumber?.Trim(), StringComparison.Ordinal))
            return await RecordTerminalAsync(actor, request, normalized, origin, document, mobile,
                "DOCUMENT_IDENTITY_INVALID", "The document identity does not match an assigned Cash Ticket.", ct);

        try
        {
            UtilityBill? bill;
            var expectedVersion = request.WaterSourceVersion;
            if (direct)
            {
                var established = await EstablishDirectSourceAsync(actor, request, ct);
                if (established.Code is { } problem)
                    return await RecordTerminalAsync(actor, request, normalized, origin, document, mobile,
                        problem, established.Message!, ct);
                bill = established.Bill!;
            }
            else
            {
                bill = await UtilityBillQuery(actor.TenantId, tracked: true)
                    .SingleOrDefaultAsync(x => x.Id == request.UtilityBillId, ct);
                if (bill is null)
                    return await RecordTerminalAsync(actor, request, normalized, origin, document, mobile,
                        "SOURCE_NOT_FOUND", "The Water obligation is not available in this tenant.", ct);
            }

            // Prospective authority (WCF active): a Legacy Water part with no legacy settlement at all carries no legacy
            // money, so it becomes Canonical here, in this posting's unit of work. A part with legacy money stays Legacy.
            if (bill.WaterSettlementAuthorityState == SettlementAuthority.Legacy)
            {
                if (!direct && bill.WaterSourceVersion != request.WaterSourceVersion)
                    return await RecordTerminalAsync(actor, request, normalized, origin, document, mobile,
                        "SOURCE_VERSION_STALE", "The Water obligation changed after it was quoted. The physical Cash Ticket requires reconciliation.", ct);
                if (await TryActivateProspectivelyAsync(bill, actor, request.ClientOperationId, direct, ct))
                    expectedVersion = bill.WaterSourceVersion;
            }
            if (direct) expectedVersion = bill.WaterSourceVersion;

            var policy = await ResolvePolicyAsync(actor.TenantId, request.BusinessDate, ct);
            var facts = await BuildFactsAsync(bill, policy, actor.TenantId, request.BusinessDate, ct);
            if (bill.WaterSettlementAuthorityState != SettlementAuthority.Canonical)
                return await RecordTerminalAsync(actor, request, normalized, origin, document, mobile,
                    bill.WaterSettlementAuthorityState == SettlementAuthority.Legacy ? "SOURCE_LEGACY" : "SOURCE_CUTOVER_PENDING",
                    "The Water source is not under Canonical settlement authority. The issued Cash Ticket requires controlled reconciliation.", ct);
            if (bill.WaterSourceVersion != expectedVersion)
                return await RecordTerminalAsync(actor, request, normalized, origin, document, mobile,
                    "SOURCE_VERSION_STALE", "The Water obligation changed after it was quoted. The physical Cash Ticket requires reconciliation.", ct);
            if (request.ReceivedAmount > facts.Quote.OutstandingAmount)
                return await RecordTerminalAsync(actor, request, normalized, origin, document, mobile,
                    "AMOUNT_EXCEEDS_OUTSTANDING", "The received amount exceeds the current Water outstanding balance.", ct);
            if (facts.Quote.Instrument != RevenueInstrumentType.CashTicket)
                return await RecordTerminalAsync(actor, request, normalized, origin, document, mobile,
                    "INSTRUMENT_POLICY_CONFLICT", "The effective WCF policy does not permit Cash Ticket collection.", ct);
            if (facts.Quote.OutstandingAmount <= 0m)
                return await RecordTerminalAsync(actor, request, normalized, origin, document, mobile,
                    "SOURCE_ALREADY_SETTLED", "The Water obligation has no outstanding balance.", ct);

            var snapshot = facts.SourceSnapshot;
            var line = new CollectionLineDraft(
                facts.Classification, facts.Policy, request.ReceivedAmount,
                CollectionSourceKind.UtilityBill, bill.Id, CollectionSourcePart.Water,
                JsonSerializer.Serialize(new { SchemaVersion = 1, Source = snapshot }, JsonOptions),
                [new CollectionAllocationDraft(CollectionSourceKind.UtilityBill, bill.Id,
                    request.ReceivedAmount, CollectionSourcePart.Water, JsonSerializer.Serialize(snapshot, JsonOptions))]);
            var actorId = ActorId(actor);
            var sourceProjection = new Action<DateTime>(now =>
                bill.ApplyCanonicalWaterProjection(facts.Quote.CumulativeSettledEvidence + request.ReceivedAmount,
                    document.DocumentNumber, now, actor.Username));
            var collection = await new CanonicalCollectionPostingCoordinator(db).PostAsync(
                actor.TenantId, request.ClientOperationId, IntentVersion, normalized, origin, actorId,
                actor.Username, actor.Role, request.BusinessDate, actor.Username, [line], document,
                collectorId: mobile ? actor.UserId : null, payorId: facts.Quote.PayorId,
                payerName: facts.Quote.PayerNameSnapshot, sourceProjections: [sourceProjection], ct: ct);
            return Result<WcfCollectionOutcomeDto>.Success(new WcfCollectionOutcomeDto(
                collection.Id, document.Id, document.DocumentNumber, collection.BusinessDate,
                collection.TotalAmount, "Posted", false));
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            var winner = await FindOperationAsync(actor.TenantId, request.ClientOperationId, ct);
            if (winner is not null)
                return await ResolvePriorAsync(winner, fingerprint, origin, actor, ct);
            var currentDocument = await db.AccountableDocuments.SingleOrDefaultAsync(x =>
                x.MunicipalityId == actor.TenantId && x.Id == request.AccountableDocumentId, ct);
            return await RecordTerminalAsync(actor, request, normalized, origin, currentDocument, mobile,
                "POST_CONCURRENCY_CONFLICT", "A source or Cash Ticket changed while posting; no Collection was partially posted.", ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var winner = await FindOperationAsync(actor.TenantId, request.ClientOperationId, ct);
            if (winner is not null)
                return await ResolvePriorAsync(winner, fingerprint, origin, actor, ct);
            var currentDocument = await db.AccountableDocuments.SingleOrDefaultAsync(x =>
                x.MunicipalityId == actor.TenantId && x.Id == request.AccountableDocumentId, ct);
            return await RecordTerminalAsync(actor, request, normalized, origin, currentDocument, mobile,
                "POST_CONFLICT", "The source, Cash Ticket, or operation identity conflicts with another posting.", ct);
        }
    }

    private async Task<Result<WcfCollectionOutcomeDto>> RecordTerminalAsync(
        Actor actor, WcfCollectionPostRequest request, string normalized, string origin,
        AccountableDocument? document, bool mobile, string code, string message, CancellationToken ct)
    {
        var physicalIssue = mobile && request.AccountableDocumentId != Guid.Empty
            && !string.IsNullOrWhiteSpace(request.DocumentNumber);
        var issuedDocument = document;
        if (physicalIssue && (issuedDocument is null
            || issuedDocument.InstrumentType != RevenueInstrumentType.CashTicket
            || !string.Equals(issuedDocument.DocumentNumber, request.DocumentNumber?.Trim(), StringComparison.Ordinal)))
        {
            issuedDocument = await db.AccountableDocuments.SingleOrDefaultAsync(x =>
                x.MunicipalityId == actor.TenantId
                && x.InstrumentType == RevenueInstrumentType.CashTicket
                && x.DocumentNumber == request.DocumentNumber!.Trim(), ct);
        }
        var reconciliation = physicalIssue;
        var issueTime = request.IssuedAtUtc is { Kind: DateTimeKind.Utc } issuedAt && issuedAt <= DateTime.UtcNow
            ? issuedAt : clock?.UtcNow ?? DateTime.UtcNow;
        if (reconciliation && issuedDocument is not null
            && await CanQuarantinePhysicalDocumentAsync(issuedDocument, actor, fromWeb: false, ct)
            && (issuedDocument.ClientOperationId is null || issuedDocument.ClientOperationId == request.ClientOperationId))
            issuedDocument.MarkPhysicalIssueReconciliationRequired(request.ClientOperationId, issueTime, actor.Username);
        var operation = PostingOperation.Record(actor.TenantId, request.ClientOperationId,
            IntentVersion, normalized, origin, ActorId(actor),
            reconciliation ? PostingOperationStatus.ReconciliationRequired : PostingOperationStatus.Rejected,
            reconciliation ? "PHYSICAL_DOCUMENT_RECONCILIATION" : code,
            reconciliation
                ? $"RECONCILIATION_REQUIRED: {message} Ticket {issuedDocument?.DocumentNumber ?? request.DocumentNumber} remains unavailable for reuse and requires review."
                : message,
            null, issuedDocument?.Id ?? document?.Id, DateTime.UtcNow);
        db.PostingOperations.Add(operation);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var prior = await FindOperationAsync(actor.TenantId, request.ClientOperationId, ct);
            if (prior is not null)
                return await ResolvePriorAsync(prior,
                    PostingOperation.ComputeIntentFingerprint(IntentVersion, normalized, origin, ActorId(actor)),
                    origin, actor, ct);
            throw;
        }
        return Result<WcfCollectionOutcomeDto>.Failure(
            reconciliation ? $"RECONCILIATION_REQUIRED: {message} A physical Cash Ticket issuance remains unavailable for reuse and requires office review." : message,
            ResultStatus.Conflict);
    }

    private static Guid CreateLegacyOperationId(Guid tenantId, string actorId, SyncOfflineOperationDto request)
    {
        var intent = string.Join("|", tenantId.ToString("N"), actorId, request.UtilityBillId?.ToString("N"),
            request.BusinessDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            request.WaterStatus?.ToString(), request.WaterPartialAmount?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            request.WaterORNumber?.Trim());
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(intent));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private async Task<bool> CanQuarantinePhysicalDocumentAsync(
        AccountableDocument document, Actor actor, bool fromWeb, CancellationToken ct)
    {
        if (document.State is not (AccountableDocumentState.Assigned or AccountableDocumentState.InOffice))
            return false;
        if (fromWeb && document.State == AccountableDocumentState.InOffice)
            return true;
        // Historical assignments are audit evidence, not present custody. Even an Assigned document must
        // agree with the current custodian and have an active assignment interval.
        if (document.State != AccountableDocumentState.Assigned || document.AssignedUserId != actor.UserId)
            return false;
        return await db.AccountableFormAssignments.AsNoTracking().AnyAsync(x =>
            x.MunicipalityId == actor.TenantId
            && x.AccountableDocumentId == document.Id
            && x.AssignedUserId == actor.UserId
            && x.ReturnedAtUtc == null, ct);
    }

    private async Task<Result<WcfCollectionOutcomeDto>> ResolvePriorAsync(
        PostingOperation prior, string fingerprint, string origin, Actor actor, CancellationToken ct)
    {
        if (prior.Origin != origin || prior.ActorId != ActorId(actor) || prior.IntentFingerprint != fingerprint)
            return Result<WcfCollectionOutcomeDto>.Failure(
                "IDEMPOTENCY CONFLICT: this ClientOperationId is already bound to a different WCF posting intent.",
                ResultStatus.Conflict);
        if (prior.Status != PostingOperationStatus.Succeeded || prior.CollectionId is not { } collectionId)
            return Result<WcfCollectionOutcomeDto>.Failure(
                prior.OutcomeDetails ?? prior.OutcomeCode ?? "The original WCF posting was rejected.",
                ResultStatus.Conflict);
        var collection = await db.Collections.AsNoTracking().SingleOrDefaultAsync(x =>
            x.MunicipalityId == actor.TenantId && x.Id == collectionId, ct);
        if (collection is null)
            return Result<WcfCollectionOutcomeDto>.Failure("The recorded Collection outcome is unavailable.", ResultStatus.Conflict);
        var document = prior.AccountableDocumentId is { } documentId
            ? await db.AccountableDocuments.AsNoTracking().SingleOrDefaultAsync(x =>
                x.MunicipalityId == actor.TenantId && x.Id == documentId, ct)
            : null;
        return Result<WcfCollectionOutcomeDto>.Success(new WcfCollectionOutcomeDto(
            collection.Id, document?.Id ?? Guid.Empty, document?.DocumentNumber ?? "Cash Ticket unavailable",
            collection.BusinessDate, collection.TotalAmount, "Posted", true));
    }

    private async Task<PolicyFacts> ResolvePolicyAsync(Guid tenantId, DateOnly asOf, CancellationToken ct)
    {
        var classification = await db.RevenueClassifications.SingleOrDefaultAsync(x =>
            x.MunicipalityId == tenantId && x.SemanticCode == RevenueClassificationCodes.Wcf && x.IsActive, ct);
        if (classification is null)
            throw new WorkflowProblem("WCF has no active revenue classification for this tenant.", ResultStatus.Conflict);
        var policy = await db.RevenueClassificationPolicies
            .Where(x => x.MunicipalityId == tenantId && x.RevenueClassificationId == classification.Id
                && x.BusinessContext == RevenuePolicyContext.Default
                && x.EffectiveDate <= asOf)
            .OrderByDescending(x => x.EffectiveDate).FirstOrDefaultAsync(ct);
        if (policy?.PermittedInstrumentType != RevenueInstrumentType.CashTicket)
            throw new WorkflowProblem("The effective WCF policy does not approve Cash Ticket collection.", ResultStatus.Conflict);
        return new PolicyFacts(classification, policy);
    }

    private IQueryable<UtilityBill> UtilityBillQuery(Guid tenantId, bool tracked)
    {
        IQueryable<UtilityBill> query = db.UtilityBills.Where(x => x.MunicipalityId == tenantId)
            .Include(x => x.Stall!).ThenInclude(x => x.Facility)
            .Include(x => x.Stall!).ThenInclude(x => x.Contracts).ThenInclude(x => x.Payor);
        return tracked ? query : query.AsNoTracking();
    }

    private async Task<WaterFacts> BuildFactsAsync(
        UtilityBill bill, PolicyFacts policy, Guid tenantId, DateOnly businessDate, CancellationToken ct)
    {
        // A bill established in this unit of work is not stamped with its tenant until it is saved; its stall is.
        var billTenant = bill.MunicipalityId == Guid.Empty ? bill.Stall?.MunicipalityId : bill.MunicipalityId;
        if (billTenant != tenantId || bill.Stall?.Facility?.Code != FacilityCode.NPM)
            throw new WorkflowProblem("This Water source is not an accessible NPM UtilityBill part in this tenant.", ResultStatus.Conflict);
        var settled = bill.WaterAmountPaid;
        if (bill.WaterSettlementAuthorityState == SettlementAuthority.Canonical)
        {
            if (bill.WaterSettlementCutoverId is not { } cutoverId)
                throw new WorkflowProblem("Canonical WCF source has no frozen cutover evidence.", ResultStatus.Conflict);
            // A prospective activation made in this unit of work is tracked but not yet saved; read it from the context.
            var cutover = db.CollectionSettlementCutovers.Local.FirstOrDefault(x =>
                    x.Id == cutoverId && x.MunicipalityId == tenantId
                    && x.SourceKind == CollectionSourceKind.UtilityBill
                    && x.SourceId == bill.Id && x.SourcePart == CollectionSourcePart.Water)
                ?? await db.CollectionSettlementCutovers.AsNoTracking().SingleOrDefaultAsync(x =>
                    x.Id == cutoverId && x.MunicipalityId == tenantId
                    && x.SourceKind == CollectionSourceKind.UtilityBill
                    && x.SourceId == bill.Id && x.SourcePart == CollectionSourcePart.Water, ct);
            if (cutover is null)
                throw new WorkflowProblem("Canonical Water cutover evidence does not match the source.", ResultStatus.Conflict);
            var sourceAllocations = db.CollectionAllocations.AsNoTracking().Where(x =>
                x.MunicipalityId == tenantId && x.SourceKind == CollectionSourceKind.UtilityBill
                && x.SourceId == bill.Id && x.SourcePart == CollectionSourcePart.Water);
            var allocated = await sourceAllocations.Select(x => (decimal?)x.Amount).SumAsync(ct) ?? 0m;
            var allocationIds = sourceAllocations.Select(x => x.Id);
            var corrected = await db.CollectionCorrectionAllocations.AsNoTracking()
                .Where(x => x.MunicipalityId == tenantId && allocationIds.Contains(x.OriginalAllocationId))
                .Select(x => (decimal?)x.FinancialEffectAmount).SumAsync(ct) ?? 0m;
            settled = cutover.OpeningLegacySettledAmount + allocated + corrected;
        }
        var outstanding = Math.Max(0m, bill.WaterCharge - settled);
        var occupancy = bill.Stall.OccupancyAnsweringForMonth(bill.BillingYear, bill.BillingMonth, businessDate);
        var contract = occupancy?.Contract;
        var linkedPayor = contract?.PayorId is not null && contract.Payor?.MunicipalityId == tenantId
            ? contract.Payor : null;
        var payorId = linkedPayor?.Id;
        var payerName = linkedPayor?.DisplayName
            ?? (string.IsNullOrWhiteSpace(contract?.ActualOccupant) ? null : contract.ActualOccupant.Trim());
        var facility = bill.Stall.Facility!;
        var section = bill.Stall.Section is { } marketSection
            ? facility.SectionLabel(marketSection) ?? bill.Stall.CustomSectionName ?? string.Empty
            : bill.Stall.CustomSectionName ?? string.Empty;
        var snapshot = new WcfSourceSnapshot(
            1, tenantId, bill.Id, CollectionSourcePart.Water, bill.WaterSourceVersion,
            bill.StallId, bill.Stall.StallNo, facility.Id, facility.Name,
            contract?.Id, contract?.UpdatedAt, payorId, payerName,
            bill.BillingYear, bill.BillingMonth, bill.WaterPreviousReading,
            bill.WaterCurrentReading, bill.WaterConsumption, bill.WaterRatePerCubicMeter,
            bill.WaterCharge, settled, outstanding, policy.Classification.Id, policy.Policy.Id,
            policy.Policy.DisplayName, RevenueInstrumentType.CashTicket,
            bill.WaterSettlementAuthorityState, (bill.WaterCalculationBasis == UtilityCalculationBasis.DirectApproved ? "DirectApproved" : "Metered"), section);
        var quote = new WcfObligationQuoteDto(
            tenantId, bill.Id, bill.WaterSourceVersion, bill.StallId, bill.Stall.StallNo,
            facility.Name, section, bill.BillingYear, bill.BillingMonth,
            bill.WaterPreviousReading, bill.WaterCurrentReading, bill.WaterConsumption,
            bill.WaterRatePerCubicMeter, bill.WaterCharge, settled, outstanding,
            bill.WaterSettlementAuthorityState, payorId, payerName,
            policy.Classification.Id, policy.Policy.Id, policy.Policy.DisplayName,
            RevenueInstrumentType.CashTicket, (bill.WaterCalculationBasis == UtilityCalculationBasis.DirectApproved ? "DirectApproved" : "Metered"),
            bill.WaterSettlementAuthorityState == SettlementAuthority.Canonical && outstanding > 0m);
        return new WaterFacts(bill, policy.Classification, policy.Policy, quote, snapshot);
    }

    private async Task<bool> CollectorHasNpmAuthorityAsync(Guid userId, Guid tenantId, CancellationToken ct)
    {
        var collector = await db.CollectorUsers.AsNoTracking()
            .Include(x => x.FacilityAssignments)
            .SingleOrDefaultAsync(x => x.MunicipalityId == tenantId && x.Id == userId, ct);
        return collector?.FacilityAssignments.Any(x => x.FacilityCode == FacilityCode.NPM) == true;
    }

    private Task<bool> CollectorHasOperationAssignmentAsync(
        Guid tenantId, Guid collectorId, string operationCode, CancellationToken ct) =>
        db.CollectorOperationAssignments.AsNoTracking().AnyAsync(x =>
            x.MunicipalityId == tenantId && x.CollectorId == collectorId && x.OperationCode == operationCode, ct);

    private Task<PostingOperation?> FindOperationAsync(Guid tenantId, Guid operationId, CancellationToken ct) =>
        db.PostingOperations.AsNoTracking().SingleOrDefaultAsync(x =>
            x.MunicipalityId == tenantId && x.ClientOperationId == operationId, ct);

    private async Task<Result<T>> Run<T>(
        Func<Actor, Task<Result<T>>> action, CancellationToken ct,
        bool requireNpmAuthorityForCollector = true)
    {
        if (!TryGetActor(out var actor, out var failure))
            return failure == ResultStatus.Unauthorized ? Result<T>.Unauthorized() : Result<T>.Forbidden();
        if (actor.Role == "Collector" && requireNpmAuthorityForCollector
            && !await CollectorHasNpmAuthorityAsync(actor.UserId, actor.TenantId, ct))
            return Result<T>.Forbidden();
        try { return await action(actor); }
        catch (WorkflowProblem problem) { return Result<T>.Failure(problem.Message, problem.Status); }
        catch (DbUpdateConcurrencyException) { return Result<T>.Failure("The Water source or Cash Ticket changed concurrently.", ResultStatus.Conflict); }
        catch (DbUpdateException) { return Result<T>.Failure("The WCF Collection conflicts with another saved transaction.", ResultStatus.Conflict); }
    }

    private bool TryGetActor(out Actor actor, out ResultStatus failure)
    {
        actor = default!;
        failure = ResultStatus.Unauthorized;
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId || userId == Guid.Empty)
            return false;
        if (currentUser.Role is not ("Admin" or "SuperAdmin" or "Collector"))
        {
            failure = ResultStatus.Forbidden;
            return false;
        }
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenantId)
        {
            failure = ResultStatus.Forbidden;
            return false;
        }
        actor = new Actor(userId, tenantId, currentUser.Username ?? "Office User", currentUser.Role);
        return true;
    }

    // A direct entry names its source and period instead of a UtilityBill, so its intent carries them; a prepared-source
    // intent keeps its original shape exactly, so an operation already posted still replays to the same fingerprint.
    private static string NormalizeIntent(Actor actor, WcfCollectionPostRequest request) =>
        request.UtilityBillId == Guid.Empty ? JsonSerializer.Serialize(new
        {
            SchemaVersion = request.SchemaVersion,
            OperationType = "WcfWaterDirectCollection",
            TenantId = actor.TenantId,
            ActorId = ActorId(actor),
            BusinessDate = request.BusinessDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            SourceKind = CollectionSourceKind.UtilityBill.ToString(),
            StallId = request.StallId,
            BillingYear = request.BillingYear,
            BillingMonth = request.BillingMonth,
            SourcePart = CollectionSourcePart.Water.ToString(),
            DirectAmount = request.ReceivedAmount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            Instrument = RevenueInstrumentType.CashTicket.ToString(),
            AccountableDocumentId = request.AccountableDocumentId,
            DocumentNumber = request.DocumentNumber?.Trim(),
            IssuedAtUtc = request.IssuedAtUtc?.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture)
        }, JsonOptions)
        : JsonSerializer.Serialize(new
        {
            SchemaVersion = request.SchemaVersion,
            OperationType = "WcfWaterCollection",
            TenantId = actor.TenantId,
            ActorId = ActorId(actor),
            BusinessDate = request.BusinessDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            SourceKind = CollectionSourceKind.UtilityBill.ToString(),
            SourceId = request.UtilityBillId,
            SourcePart = CollectionSourcePart.Water.ToString(),
            ReceivedAmount = request.ReceivedAmount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            WaterSourceVersion = request.WaterSourceVersion,
            Instrument = RevenueInstrumentType.CashTicket.ToString(),
            AccountableDocumentId = request.AccountableDocumentId,
            DocumentNumber = request.DocumentNumber?.Trim(),
            IssuedAtUtc = request.IssuedAtUtc?.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture)
        }, JsonOptions);

    private static string ActorId(Actor actor) => actor.UserId.ToString("N");

    private static (int? Year, int? Month) ReadPeriod(string? sourceSnapshot)
    {
        if (string.IsNullOrWhiteSpace(sourceSnapshot)) return (null, null);
        try
        {
            using var document = JsonDocument.Parse(sourceSnapshot);
            var root = document.RootElement;
            if (root.TryGetProperty("billingYear", out var year)
                && root.TryGetProperty("billingMonth", out var month))
                return (year.GetInt32(), month.GetInt32());
            if (root.TryGetProperty("BillingYear", out year)
                && root.TryGetProperty("BillingMonth", out month))
                return (year.GetInt32(), month.GetInt32());
        }
        catch (JsonException) { }
        return (null, null);
    }

    private sealed record Actor(Guid UserId, Guid TenantId, string Username, string Role);
    private sealed record PolicyFacts(RevenueClassification Classification, RevenueClassificationPolicy Policy);
    private sealed record WaterFacts(UtilityBill Bill, RevenueClassification Classification,
        RevenueClassificationPolicy Policy, WcfObligationQuoteDto Quote, WcfSourceSnapshot SourceSnapshot);
    private sealed record WcfSourceSnapshot(
        int SchemaVersion, Guid MunicipalityId, Guid UtilityBillId, CollectionSourcePart SourcePart,
        long SourceVersion, Guid StallId, string StallNo, Guid FacilityId, string FacilityName,
        Guid? ContractId, DateTime? ContractUpdatedAtUtc, Guid? PayorId, string? PayerNameSnapshot,
        int BillingYear, int BillingMonth, decimal PreviousReading, decimal CurrentReading,
        decimal Consumption, decimal RatePerCubicMeter, decimal AssessedAmount,
        decimal CumulativeSettledEvidence, decimal OutstandingAmount, Guid RevenueClassificationId,
        Guid RevenueClassificationPolicyId, string ClassificationName, RevenueInstrumentType Instrument,
        SettlementAuthority SettlementAuthority, string ChargeBasis, string Section);

    private sealed class WorkflowProblem(string message, ResultStatus status) : Exception(message)
    {
        public ResultStatus Status { get; } = status;
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// Source-scoped Legacy -> PendingCutover -> frozen opening position -> Canonical control plane.
/// It is not a settlement writer: opening evidence never creates a Collection or revenue event.
/// </summary>
public sealed class SettlementCutoverWorkflow(
    IAppDbContext db,
    ICurrentUserService currentUser,
    ICurrentMunicipalityAccessor municipality,
    IClock? clock = null)
{
    private const int RequiredWcfPayloadVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private DateOnly BusinessToday => clock?.PhilippineToday ?? PhilippineTime.Today;
    private DateTime UtcNow => clock?.UtcNow ?? DateTime.UtcNow;

    /// <summary>Read-only evaluation. Supplying evidence simulates a completed reconciliation checklist but saves nothing.</summary>
    public Task<Result<SettlementCutoverReadinessDto>> EvaluateReadinessAsync(
        SettlementCutoverReadinessRequest request, CancellationToken ct = default) => Run(async actor =>
    {
        var report = await EvaluateCoreAsync(actor, request.Scope, request.Evidence, allowFrozen: false, ct);
        return Result<SettlementCutoverReadinessDto>.Success(report);
    }, ct);

    /// <summary>Begins the exact source scope's quiescence window. Never converts the source to Canonical.</summary>
    public Task<Result<SettlementCutoverOutcomeDto>> BeginPendingCutoverAsync(
        SettlementCutoverScope scope, CancellationToken ct = default) => Run(async actor =>
    {
        await using var transaction = await db.BeginSerializableTransactionAsync(ct);
        var source = await LoadSourceAsync(actor.TenantId, scope, tracked: true, ct);
        if (source.Authority != SettlementAuthority.Legacy)
            return Result<SettlementCutoverOutcomeDto>.Failure(
                "Only the exact Legacy source may enter Pending Cutover; direct Canonical transition is prohibited.", ResultStatus.Conflict);
        source.MarkPending();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result<SettlementCutoverOutcomeDto>.Success(ToOutcome(scope, source, null,
            "Exact source scope entered Pending Cutover. Reconcile writers and in-flight evidence before freezing."));
    }, ct);

    /// <summary>Freezes reconciled opening evidence for an exact PendingCutover source; does not activate it.</summary>
    public Task<Result<SettlementCutoverOutcomeDto>> FreezeOpeningPositionAsync(
        SettlementCutoverFreezeRequest request, CancellationToken ct = default) => Run(async actor =>
    {
        await using var transaction = await db.BeginSerializableTransactionAsync(ct);
        var readiness = await EvaluateCoreAsync(actor, request.Scope, request.Evidence, allowFrozen: false, ct);
        if (!readiness.Ready)
            return Result<SettlementCutoverOutcomeDto>.Failure(
                "Cutover freeze is blocked: " + string.Join("; ", readiness.BlockingReasons), ResultStatus.Conflict);
        if (readiness.SourceVersion != request.ExpectedSourceVersion
            || !string.Equals(readiness.ReadinessFingerprint, request.ExpectedReadinessFingerprint, StringComparison.Ordinal))
            return Result<SettlementCutoverOutcomeDto>.Failure(
                "Cutover readiness changed. Re-run the dry-run and reconcile the updated source evidence.", ResultStatus.Conflict);

        var source = await LoadSourceAsync(actor.TenantId, request.Scope, tracked: true, ct);
        if (source.Authority != SettlementAuthority.PendingCutover
            || source.Version != request.ExpectedSourceVersion
            || readiness.LegacySettledEvidence is not { } settled
            || readiness.OutstandingAmount is not { } outstanding)
            return Result<SettlementCutoverOutcomeDto>.Failure(
                "The exact source is no longer at the reviewed Pending Cutover boundary.", ResultStatus.Conflict);
        var assessment = readiness.AssessmentAmount;

        var evidence = JsonSerializer.Serialize(new CutoverEvidenceEnvelope(
            1, readiness.ReadinessFingerprint, readiness.Warnings, request.Evidence), JsonOptions);
        if (Encoding.UTF8.GetByteCount(evidence) > 65_536)
            return Result<SettlementCutoverOutcomeDto>.Failure(
                "The reconciled evidence exceeds the persisted cutover record limit; reduce the evidence manifest and retry.",
                ResultStatus.Invalid);

        var now = UtcNow;
        var boundaryVersion = source.AdvanceBoundary();
        var cutover = CollectionSettlementCutover.Freeze(
            actor.TenantId, request.Scope.SourceKind, request.Scope.SourceId, request.Scope.SourcePart,
            boundaryVersion, now, assessment, settled, outstanding, evidence, actor.UserId, now);
        db.CollectionSettlementCutovers.Add(cutover);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result<SettlementCutoverOutcomeDto>.Success(ToOutcome(request.Scope, source, cutover,
            "Opening settlement evidence frozen. The source remains Pending Cutover until separately activated."));
    }, ct);

    /// <summary>
    /// Atomic activation command for a later approved rollout. Phase 5A exercises this only with isolated test rows;
    /// no controller or bulk/live activation route is exposed.
    /// </summary>
    public Task<Result<SettlementCutoverOutcomeDto>> ActivateCanonicalAsync(
        SettlementCutoverScope scope, long expectedSourceVersion, CancellationToken ct = default) => Run(async actor =>
    {
        await using var transaction = await db.BeginSerializableTransactionAsync(ct);
        var source = await LoadSourceAsync(actor.TenantId, scope, tracked: true, ct);
        if (source.Authority != SettlementAuthority.PendingCutover)
            return Result<SettlementCutoverOutcomeDto>.Failure(
                "Canonical activation requires this exact source to be Pending Cutover.", ResultStatus.Conflict);
        var cutover = await db.CollectionSettlementCutovers.SingleOrDefaultAsync(x =>
            x.MunicipalityId == actor.TenantId && x.SourceKind == scope.SourceKind
            && x.SourceId == scope.SourceId && x.SourcePart == scope.SourcePart, ct);
        if (cutover is null || cutover.BoundaryVersion != source.Version
            || source.Version != expectedSourceVersion)
            return Result<SettlementCutoverOutcomeDto>.Failure(
                "The frozen opening evidence or source version is missing or stale.", ResultStatus.Conflict);

        CutoverEvidenceEnvelope envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<CutoverEvidenceEnvelope>(cutover.ReconciliationEvidence, JsonOptions)
                ?? throw new JsonException();
        }
        catch (JsonException)
        {
            return Result<SettlementCutoverOutcomeDto>.Failure(
                "The frozen reconciliation evidence cannot be verified.", ResultStatus.Conflict);
        }
        if (envelope.SchemaVersion != 1 || envelope.Evidence is null)
            return Result<SettlementCutoverOutcomeDto>.Failure(
                "The frozen reconciliation evidence is incomplete.", ResultStatus.Conflict);

        var readiness = await EvaluateCoreAsync(actor, scope, envelope.Evidence, allowFrozen: true, ct);
        if (!readiness.Ready
            || !string.Equals(readiness.ReadinessFingerprint, envelope.ReadinessFingerprint, StringComparison.Ordinal)
            || readiness.AssessmentAmount != cutover.OpeningAssessmentAmount
            || readiness.LegacySettledEvidence != cutover.OpeningLegacySettledAmount
            || readiness.OutstandingAmount != cutover.OpeningOutstandingAmount)
            return Result<SettlementCutoverOutcomeDto>.Failure(
                "Canonical activation is blocked because reviewed readiness, source, document, online-payment, or reconciliation evidence changed.",
                ResultStatus.Conflict);

        source.Activate(cutover);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result<SettlementCutoverOutcomeDto>.Success(ToOutcome(scope, source, cutover,
            "The exact source scope is now Canonical; its frozen opening settlement remains non-revenue evidence."));
    }, ct);

    private async Task<SettlementCutoverReadinessDto> EvaluateCoreAsync(
        Actor actor, SettlementCutoverScope scope, SettlementCutoverReconciliationEvidence? evidence,
        bool allowFrozen, CancellationToken ct)
    {
        ValidateScope(scope);
        var source = await LoadSourceAsync(actor.TenantId, scope, tracked: false, ct);
        var blockers = new List<string>();
        var warnings = new List<string>();
        if (source.Authority == SettlementAuthority.Legacy)
            blockers.Add("Mark this exact source Pending Cutover before quiescing its legacy writers.");
        else if (source.Authority == SettlementAuthority.Canonical)
            blockers.Add("This exact source is already Canonical and cannot be cut over again.");

        var existingCutover = await db.CollectionSettlementCutovers.AsNoTracking().SingleOrDefaultAsync(x =>
            x.MunicipalityId == actor.TenantId && x.SourceKind == scope.SourceKind
            && x.SourceId == scope.SourceId && x.SourcePart == scope.SourcePart, ct);
        if (existingCutover is not null && !allowFrozen)
            blockers.Add("An opening settlement is already frozen for this exact source.");
        if (existingCutover is not null && allowFrozen && existingCutover.BoundaryVersion != source.Version)
            blockers.Add("The source version moved after its opening settlement was frozen.");

        if (source.Assessment is not { } assessment || source.LegacySettled is not { } settled
            || source.Outstanding is not { } outstanding)
        {
            blockers.Add("The opening position cannot be attributed reliably from this source's legacy settlement fields.");
        }
        else if (settled < 0m || outstanding < 0m || settled > assessment || assessment != settled + outstanding)
        {
            blockers.Add("Assessment, legacy settled evidence, and outstanding do not reconcile.");
        }

        var policy = await ResolvePolicyAsync(actor.TenantId, source.ClassificationCode, ct);
        if (policy is null)
            blockers.Add($"No active {source.ClassificationCode} classification policy is effective on {BusinessToday:yyyy-MM-dd}.");
        else if (policy.PermittedInstrumentType != source.RequiredInstrument)
            blockers.Add($"The effective {source.ClassificationCode} instrument policy does not resolve to {source.RequiredInstrument}.");

        var allocationExists = await db.CollectionAllocations.AsNoTracking().AnyAsync(x =>
            x.MunicipalityId == actor.TenantId && x.SourceKind == scope.SourceKind
            && x.SourceId == scope.SourceId && x.SourcePart == scope.SourcePart, ct);
        if (allocationExists)
            blockers.Add("Canonical allocations already reference this pre-cutover source; reconcile the boundary before freezing.");

        var operations = await db.PostingOperations.AsNoTracking()
            .Where(x => x.MunicipalityId == actor.TenantId && x.Status == PostingOperationStatus.ReconciliationRequired)
            .ToListAsync(ct);
        var sourceOperations = operations.Where(x => JsonReferencesGuid(x.NormalizedIntent, scope.SourceId)).ToArray();
        if (sourceOperations.Length > 0)
            blockers.Add($"{sourceOperations.Length} unresolved PostingOperation reconciliation exception(s) reference this source.");

        var sourceOperationIds = sourceOperations.Select(x => x.ClientOperationId).ToArray();
        var sourceDocumentIds = sourceOperations.Where(x => x.AccountableDocumentId.HasValue)
            .Select(x => x.AccountableDocumentId!.Value).ToArray();
        var affectedCollectors = source.FacilityId is { } facilityId
            ? await db.CollectorFacilityAssignments.AsNoTracking()
                .Where(x => x.MunicipalityId == actor.TenantId && x.FacilityId == facilityId)
                .Select(x => x.CollectorId).Distinct().ToListAsync(ct)
            : [];
        var reconciliationDocumentRows = await db.AccountableDocuments.AsNoTracking()
            .Where(x => x.MunicipalityId == actor.TenantId
                && (x.State == AccountableDocumentState.ReconciliationRequired
                    && (sourceDocumentIds.Contains(x.Id)
                        || x.ClientOperationId.HasValue && sourceOperationIds.Contains(x.ClientOperationId.Value)
                        || x.InstrumentType == source.RequiredInstrument && x.AssignedUserId.HasValue
                            && affectedCollectors.Contains(x.AssignedUserId.Value))))
            .Select(x => new { x.Id, x.DocumentNumber, x.ClientOperationId, x.AssignedUserId, x.ConsumedAtUtc, x.CreatedAt })
            .ToListAsync(ct);
        var reconciliationDocuments = reconciliationDocumentRows.Count;
        if (reconciliationDocuments > 0)
            blockers.Add($"{reconciliationDocuments} physically issued accountable document(s) still require reconciliation.");

        var documentsById = reconciliationDocumentRows.ToDictionary(x => x.Id);
        var documentsByOperation = reconciliationDocumentRows.Where(x => x.ClientOperationId.HasValue)
            .GroupBy(x => x.ClientOperationId!.Value).ToDictionary(x => x.Key, x => x.First());
        var reconciliationExceptions = sourceOperations.Select(operation =>
        {
            var document = operation.AccountableDocumentId is { } documentId && documentsById.TryGetValue(documentId, out var linked)
                ? linked
                : documentsByOperation.GetValueOrDefault(operation.ClientOperationId);
            return new SettlementCutoverExceptionDto("PostingOperation", operation.ClientOperationId,
                document?.Id ?? operation.AccountableDocumentId, document?.DocumentNumber,
                document?.AssignedUserId, operation.OutcomeCode ?? "RECONCILIATION_REQUIRED", operation.RecordedAtUtc);
        }).Concat(reconciliationDocumentRows
            .Where(document => document.ClientOperationId is null
                || !sourceOperationIds.Contains(document.ClientOperationId.Value) && !sourceDocumentIds.Contains(document.Id))
            .Select(document => new SettlementCutoverExceptionDto("IssuedDocumentWithoutMatchingSourceOperation",
                document.ClientOperationId, document.Id, document.DocumentNumber, document.AssignedUserId,
                "MISSING_SOURCE_OPERATION", document.ConsumedAtUtc ?? document.CreatedAt)))
            .OrderBy(x => x.RecordedAtUtc).ThenBy(x => x.ClientOperationId).ToArray();

        var activeAssignedDocuments = await db.AccountableDocuments.AsNoTracking()
            .Where(x => x.MunicipalityId == actor.TenantId && x.InstrumentType == source.RequiredInstrument
                && x.State == AccountableDocumentState.Assigned && x.AssignedUserId.HasValue
                && affectedCollectors.Contains(x.AssignedUserId.Value)
                && db.AccountableFormAssignments.Any(a => a.MunicipalityId == actor.TenantId
                    && a.AccountableDocumentId == x.Id && a.AssignedUserId == x.AssignedUserId
                    && a.ReturnedAtUtc == null))
            .OrderBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.FormBookId,
                x.SerialNumber,
                x.DocumentNumber,
                x.InstrumentType,
                x.State,
                x.AssignedUserId
            })
            .ToArrayAsync(ct);
        var activeAssignedDocumentCount = activeAssignedDocuments.Length;
        var inconsistentAssignedDocuments = await db.AccountableDocuments.AsNoTracking()
            .Where(x => x.MunicipalityId == actor.TenantId && x.InstrumentType == source.RequiredInstrument
                && x.State == AccountableDocumentState.Assigned && x.AssignedUserId.HasValue
                && affectedCollectors.Contains(x.AssignedUserId.Value)
                && !db.AccountableFormAssignments.Any(a => a.MunicipalityId == actor.TenantId
                    && a.AccountableDocumentId == x.Id && a.AssignedUserId == x.AssignedUserId
                    && a.ReturnedAtUtc == null))
            .CountAsync(ct);
        if (inconsistentAssignedDocuments > 0)
            blockers.Add($"{inconsistentAssignedDocuments} assigned document(s) disagree with active custody history.");

        var unresolvedOnlinePayments = await CountUnresolvedOnlinePaymentsAsync(actor.TenantId, source, ct);
        if (unresolvedOnlinePayments > 0)
            blockers.Add($"{unresolvedOnlinePayments} initiated, pending, captured, or reconciliation-required online payment(s) could still affect this source.");

        var requiresWcfPayload = source.Scope.SourceKind == CollectionSourceKind.UtilityBill
            && source.Scope.SourcePart == CollectionSourcePart.Water;
        if (affectedCollectors.Count > 0 && !requiresWcfPayload)
            blockers.Add(source.MobileWriterStatus);
        AddEvidenceBlockers(evidence, affectedCollectors, blockers, requiresWcfPayload);
        var missingCollectorEvidence = GetMissingCollectorEvidence(evidence, affectedCollectors, requiresWcfPayload);
        if (activeAssignedDocumentCount > 0 && evidence?.AccountableDocumentInventoryReconciled != true)
            blockers.Add("Active accountable-document custody assignments exist and have not been reconciled against physical inventory.");

        warnings.Add("The candidate opening position is historical balance evidence, never a Collection or cash report event.");
        warnings.Add("Legacy compatibility reports must be checked for duplicate counting; canonical Collection projections are not additional revenue.");
        warnings.Add("Official cross-period RCD treatment remains pending Office confirmation under IA-043.");
        if (!string.IsNullOrWhiteSpace(source.LegacyDocumentEvidence))
            warnings.Add("The legacy document field is preserved as source evidence and is not proof of an accountable OR/CT unit.");
        if (activeAssignedDocumentCount > 0)
            warnings.Add($"{activeAssignedDocumentCount} active {source.RequiredInstrument} unit(s) remain in collector custody and must be included in the inventory reconciliation evidence.");
        if (source.LegacyWriterStatus is { } writerStatus)
            warnings.Add(writerStatus);

        var fingerprintMaterial = JsonSerializer.Serialize(new
        {
            tenant = actor.TenantId,
            scope,
            source.Authority,
            // Source version is independently bound by the freeze request and cutover boundary.
            // Freeze advances that version itself, so including it here would make an unchanged
            // reviewed readiness fingerprint differ immediately after a successful freeze.
            source.Assessment,
            source.LegacySettled,
            source.Outstanding,
            source.LegacyDocumentEvidence,
            policyId = policy?.Id,
            policyEffectiveDate = policy?.EffectiveDate,
            policyInstrument = policy?.PermittedInstrumentType,
            instrument = source.RequiredInstrument,
            online = unresolvedOnlinePayments,
            reconciliationOperations = sourceOperations.Length,
            reconciliationExceptions,
            activeAssignedDocuments,
            collectors = affectedCollectors.Order().ToArray(),
            evidence = evidence is null ? null : new
            {
                evidence.LegacyWritersQuiesced,
                evidence.MobileQueuesDrained,
                evidence.NoUnregisteredFieldDevices,
                evidence.OnlinePaymentsDrained,
                evidence.AccountableDocumentInventoryReconciled,
                evidence.ReportingPathVerified,
                evidence.EvidenceReference,
                Collectors = (evidence.CollectorEvidence ?? []).OrderBy(x => x.CollectorId).ToArray()
            }
        }, JsonOptions);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintMaterial)));
        return new SettlementCutoverReadinessDto(scope, source.Label, source.Authority, source.Version,
            source.Assessment ?? 0m, source.LegacySettled, source.Outstanding, source.RequiredInstrument,
            unresolvedOnlinePayments, sourceOperations.Length, reconciliationDocuments, reconciliationExceptions,
            activeAssignedDocumentCount, affectedCollectors.Order().ToArray(), missingCollectorEvidence,
            requiresWcfPayload ? RequiredWcfPayloadVersion : 0,
            blockers.Distinct().ToArray(), warnings, blockers.Count == 0, fingerprint);
    }

    private async Task<SourceState> LoadSourceAsync(Guid tenantId, SettlementCutoverScope scope, bool tracked, CancellationToken ct)
    {
        ValidateScope(scope);
        if (scope.SourceKind == CollectionSourceKind.UtilityBill)
        {
            var query = db.UtilityBills.AsQueryable();
            if (!tracked) query = query.AsNoTracking();
            var bill = await query.Include(x => x.Stall!).ThenInclude(x => x.Facility!)
                .SingleOrDefaultAsync(x => x.MunicipalityId == tenantId && x.Id == scope.SourceId, ct);
            if (bill is null) throw new CutoverProblem("The exact source is unavailable in this tenant.", ResultStatus.Conflict);
            var water = scope.SourcePart == CollectionSourcePart.Water;
            var paid = water ? bill.WaterAmountPaid : bill.ElecAmountPaid;
            var assessed = water ? bill.WaterCharge : bill.ElecCharge;
            var sourceVersion = water ? bill.WaterSourceVersion : bill.ElectricitySourceVersion;
            var authority = water ? bill.WaterSettlementAuthorityState : bill.ElectricitySettlementAuthorityState;
            var facilityId = bill.Stall?.FacilityId;
            return new SourceState(scope, bill, null, authority, sourceVersion, assessed, paid,
                assessed - paid, water ? RevenueClassificationCodes.Wcf : RevenueClassificationCodes.Ecf,
                water ? RevenueInstrumentType.CashTicket : RevenueInstrumentType.OfficialReceipt,
                facilityId, $"UtilityBill {bill.Id:N} / {scope.SourcePart} / {bill.BillingYear:D4}-{bill.BillingMonth:D2}",
                water ? bill.WaterORNumber : bill.ElecORNumber,
                "Legacy Web and cumulative Mobile utility writers are disabled while Pending Cutover; late payloads are reconciliation-only.",
                water
                    ? "The focused WCF payload v1 path exists and remains gated on verified assigned-device capability evidence."
                    : "ECF has no canonical focused Mobile posting path yet; assigned collector sources must remain Legacy until Mobile and late-payload reconciliation writers are ready.");
        }

        if (scope.SourceKind == CollectionSourceKind.PaymentRecord)
        {
            var query = db.PaymentRecords.AsQueryable();
            if (!tracked) query = query.AsNoTracking();
            var record = await query.Include(x => x.Stall!).ThenInclude(x => x.Facility!)
                .SingleOrDefaultAsync(x => x.MunicipalityId == tenantId && x.Id == scope.SourceId, ct);
            if (record is null) throw new CutoverProblem("The exact source is unavailable in this tenant.", ResultStatus.Conflict);
            var mixed = record.ElecAmount.GetValueOrDefault() != 0m || record.WaterAmount.GetValueOrDefault() != 0m
                || record.FishFeeAmount.GetValueOrDefault() != 0m;
            decimal? settled;
            if (mixed) settled = null;
            else settled = record.Status switch
            {
                PaymentStatus.Paid => record.BaseRentalAmount,
                PaymentStatus.Partial => record.PartialAmount,
                _ => 0m
            };
            decimal? outstanding = settled.HasValue ? record.BaseRentalAmount - settled.Value : null;
            var writerStatus = mixed
                ? "Mixed legacy utility/fish components make the single PaymentRecord status unsafe to attribute to rent; this source remains blocked until separately reconciled."
                : "The late legacy PaymentRecord Mobile path must be drained and confirmed before freeze; it has no canonical rent offline reconciliation registry yet.";
            return new SourceState(scope, null, record, record.SettlementAuthorityState, record.SettlementVersion,
                record.BaseRentalAmount, settled, outstanding, RevenueClassificationCodes.PermanentStallRent,
                RevenueInstrumentType.OfficialReceipt, record.Stall?.FacilityId,
                $"PaymentRecord {record.Id:N} / {record.BillingYear:D4}-{record.BillingMonth:D2}",
                record.ORNumber, writerStatus, hasMixedLegacyComponents: mixed,
                mobileWriterStatus: "PaymentRecord Mobile still uses the cumulative legacy settlement payload and lacks a durable late-issued-document reconciliation adapter; affected collector scopes remain blocked.");
        }

        throw new CutoverProblem("Phase 5A supports only exact UtilityBill Electricity/Water parts and monthly PaymentRecord rent sources.", ResultStatus.Invalid);
    }

    private async Task<RevenueClassificationPolicy?> ResolvePolicyAsync(Guid tenantId, string code, CancellationToken ct)
    {
        var classification = await db.RevenueClassifications.AsNoTracking()
            .SingleOrDefaultAsync(x => x.MunicipalityId == tenantId && x.SemanticCode == code && x.IsActive, ct);
        if (classification is null) return null;
        return await db.RevenueClassificationPolicies.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && x.RevenueClassificationId == classification.Id
                && x.EffectiveDate <= BusinessToday)
            .OrderByDescending(x => x.EffectiveDate).FirstOrDefaultAsync(ct);
    }

    private async Task<int?> CountUnresolvedOnlinePaymentsAsync(Guid tenantId, SourceState source, CancellationToken ct)
    {
        IQueryable<OnlinePaymentTransaction> query = db.OnlinePaymentTransactions.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId
                && (x.Status == OnlinePaymentStatus.Initiated || x.Status == OnlinePaymentStatus.Pending
                    || x.Status == OnlinePaymentStatus.Paid || x.Status == OnlinePaymentStatus.ReconciliationRequired));
        if (source.Record is { } record)
            query = query.Where(x => x.TargetKind == OnlinePaymentTargetKind.MonthlyRecord && x.PaymentRecordId == record.Id);
        else if (source.Bill is { } bill)
            query = query.Where(x => x.TargetKind == OnlinePaymentTargetKind.NpmUtilityBill
                && x.TargetStallId == bill.StallId && x.TargetYear == bill.BillingYear && x.TargetMonth == bill.BillingMonth);
        else return 0;
        return await query.CountAsync(ct);
    }

    private void AddEvidenceBlockers(SettlementCutoverReconciliationEvidence? evidence,
        IReadOnlyCollection<Guid> affectedCollectors, List<string> blockers, bool requiresWcfPayload)
    {
        if (evidence is null)
        {
            blockers.Add("Reconciliation evidence is required for legacy-writer quiescence, device queues, online payments, accountable documents, and report readers.");
            return;
        }
        if (!evidence.LegacyWritersQuiesced) blockers.Add("Legacy Web, Mobile, import, maintenance, and correction writers are not attested quiesced for this exact scope.");
        if (!evidence.MobileQueuesDrained) blockers.Add("Collector device-local offline queues have not been attested drained.");
        if (!evidence.NoUnregisteredFieldDevices) blockers.Add("The affected collector/device inventory is not confirmed complete.");
        if (!evidence.OnlinePaymentsDrained) blockers.Add("Initiated and provider-confirmed online payment activity is not attested drained.");
        if (!evidence.AccountableDocumentInventoryReconciled) blockers.Add("Physical accountable-document inventory and custody have not been reconciled.");
        if (!evidence.ReportingPathVerified) blockers.Add("Collection Activity, classification totals, and applicable financial report paths have not been verified for this source.");
        if (string.IsNullOrWhiteSpace(evidence.EvidenceReference)) blockers.Add("A durable reconciliation evidence reference is required.");
        if (evidence.EvidenceReference?.Length > 1_000 || (evidence.CollectorEvidence?.Count ?? 0) > 500)
            blockers.Add("Reconciliation evidence exceeds the bounded cutover evidence limits.");
        else if (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(evidence, JsonOptions)) > 48_000)
            blockers.Add("The reconciliation evidence manifest is too large to retain safely with the frozen opening position.");
        var entries = evidence.CollectorEvidence ?? [];
        if (entries.Select(x => x.CollectorId).Distinct().Count() != entries.Count)
            blockers.Add("Collector capability evidence contains duplicate collector identities.");
        foreach (var collectorId in affectedCollectors)
        {
            var entry = entries.SingleOrDefault(x => x.CollectorId == collectorId);
            if (entry is null)
                blockers.Add($"Collector {collectorId:N} has no verified client capability and queue-drain evidence.");
            else if (string.IsNullOrWhiteSpace(entry.ApplicationVersion) || entry.ApplicationVersion.Length > 64
                || requiresWcfPayload && entry.WcfPayloadVersion < RequiredWcfPayloadVersion
                || entry.WcfPayloadVersion < 0
                || !entry.LegacyQueueDrained
                || entry.VerifiedAtUtc.Kind != DateTimeKind.Utc || entry.VerifiedAtUtc > UtcNow
                || string.IsNullOrWhiteSpace(entry.EvidenceReference) || entry.EvidenceReference.Length > 1_000)
                blockers.Add(requiresWcfPayload
                    ? $"Collector {collectorId:N} has incomplete or incompatible Mobile capability evidence; WCF payload v{RequiredWcfPayloadVersion} is required."
                    : $"Collector {collectorId:N} has incomplete Mobile version/queue-drain evidence.");
        }
        if (entries.Any(x => !affectedCollectors.Contains(x.CollectorId)))
            blockers.Add("Collector capability evidence includes a collector outside the exact source facility scope.");
    }

    private IReadOnlyList<Guid> GetMissingCollectorEvidence(
        SettlementCutoverReconciliationEvidence? evidence, IReadOnlyCollection<Guid> collectors, bool requiresWcfPayload)
    {
        var valid = (evidence?.CollectorEvidence ?? []).Where(x => !string.IsNullOrWhiteSpace(x.ApplicationVersion)
            && x.ApplicationVersion.Length <= 64
            && (!requiresWcfPayload || x.WcfPayloadVersion >= RequiredWcfPayloadVersion)
            && x.WcfPayloadVersion >= 0 && x.LegacyQueueDrained && x.VerifiedAtUtc.Kind == DateTimeKind.Utc
            && x.VerifiedAtUtc <= UtcNow && !string.IsNullOrWhiteSpace(x.EvidenceReference)
            && x.EvidenceReference.Length <= 1_000).Select(x => x.CollectorId).ToHashSet();
        return collectors.Where(x => !valid.Contains(x)).Order().ToArray();
    }

    private async Task<Result<T>> Run<T>(Func<Actor, Task<Result<T>>> action, CancellationToken ct)
    {
        if (!TryGetActor(out var actor, out var failure))
            return failure == ResultStatus.Unauthorized ? Result<T>.Unauthorized() : Result<T>.Forbidden();
        try { return await action(actor); }
        catch (CutoverProblem problem) { return Result<T>.Failure(problem.Message, problem.Status); }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return Result<T>.Failure("The source or cutover record changed concurrently; refresh readiness before retrying.", ResultStatus.Conflict);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return Result<T>.Failure("The cutover conflicts with a concurrent source or evidence change.", ResultStatus.Conflict);
        }
        catch (InvalidOperationException ex)
        {
            db.ChangeTracker.Clear();
            return Result<T>.Failure(ex.Message, ResultStatus.Conflict);
        }
        catch (ArgumentException ex)
        {
            db.ChangeTracker.Clear();
            return Result<T>.Failure(ex.Message, ResultStatus.Conflict);
        }
    }

    private bool TryGetActor(out Actor actor, out ResultStatus failure)
    {
        actor = default!;
        failure = ResultStatus.Unauthorized;
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId || userId == Guid.Empty)
            return false;
        if (currentUser.Role is not ("Admin" or "SuperAdmin"))
        {
            failure = ResultStatus.Forbidden;
            return false;
        }
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty || currentUser.MunicipalityId is { } claimTenant && claimTenant != tenantId)
        {
            failure = ResultStatus.Forbidden;
            return false;
        }
        actor = new Actor(userId, tenantId, currentUser.Username ?? "Office User");
        return true;
    }

    private static SettlementCutoverOutcomeDto ToOutcome(SettlementCutoverScope scope, SourceState source,
        CollectionSettlementCutover? cutover, string message) => new(scope, source.CurrentAuthority, cutover?.Id,
        source.CurrentVersion, cutover?.OpeningAssessmentAmount, cutover?.OpeningLegacySettledAmount,
        cutover?.OpeningOutstandingAmount, cutover?.CutoverAtUtc, message);

    private static void ValidateScope(SettlementCutoverScope scope)
    {
        if (scope.SourceId == Guid.Empty) throw new CutoverProblem("An exact source identity is required.", ResultStatus.Invalid);
        if (scope.SourceKind == CollectionSourceKind.UtilityBill
            && scope.SourcePart is not (CollectionSourcePart.Electricity or CollectionSourcePart.Water))
            throw new CutoverProblem("UtilityBill cutover must identify exactly Electricity or Water.", ResultStatus.Invalid);
        if (scope.SourceKind == CollectionSourceKind.PaymentRecord && scope.SourcePart is not null)
            throw new CutoverProblem("Monthly PaymentRecord cutover has no source part.", ResultStatus.Invalid);
        if (scope.SourceKind is not (CollectionSourceKind.UtilityBill or CollectionSourceKind.PaymentRecord))
            throw new CutoverProblem("Phase 5A supports UtilityBill Electricity/Water and monthly PaymentRecord rent scopes only.", ResultStatus.Invalid);
    }

    private static bool JsonReferencesGuid(string json, Guid expected)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return HasGuid(doc.RootElement, expected);
        }
        catch (JsonException) { return false; }
    }

    private static bool HasGuid(JsonElement element, Guid expected) => element.ValueKind switch
    {
        JsonValueKind.String => Guid.TryParse(element.GetString(), out var parsed) && parsed == expected,
        JsonValueKind.Array => element.EnumerateArray().Any(x => HasGuid(x, expected)),
        JsonValueKind.Object => element.EnumerateObject().Any(x => HasGuid(x.Value, expected)),
        _ => false
    };

    private sealed record Actor(Guid UserId, Guid TenantId, string Username);
    private sealed record CutoverEvidenceEnvelope(int SchemaVersion, string ReadinessFingerprint,
        IReadOnlyList<string> Warnings, SettlementCutoverReconciliationEvidence Evidence);

    private sealed class SourceState
    {
        public SourceState(SettlementCutoverScope scope, UtilityBill? bill, PaymentRecord? record,
            SettlementAuthority authority, long version, decimal? assessment, decimal? legacySettled,
            decimal? outstanding, string classificationCode, RevenueInstrumentType requiredInstrument,
            Guid? facilityId, string label, string? legacyDocumentEvidence, string? legacyWriterStatus,
            string? mobileWriterStatus = null, bool hasMixedLegacyComponents = false)
        {
            Scope = scope; Bill = bill; Record = record; Authority = authority; Version = version;
            Assessment = assessment; LegacySettled = legacySettled; Outstanding = outstanding;
            ClassificationCode = classificationCode; RequiredInstrument = requiredInstrument;
            FacilityId = facilityId; Label = label; LegacyDocumentEvidence = legacyDocumentEvidence;
            LegacyWriterStatus = legacyWriterStatus; HasMixedLegacyComponents = hasMixedLegacyComponents;
            MobileWriterStatus = mobileWriterStatus ?? "Mobile readiness is not established for this source family.";
        }
        public SettlementCutoverScope Scope { get; }
        public UtilityBill? Bill { get; }
        public PaymentRecord? Record { get; }
        public SettlementAuthority Authority { get; }
        public long Version { get; }
        public SettlementAuthority CurrentAuthority => Bill is not null
            ? Scope.SourcePart == CollectionSourcePart.Water ? Bill.WaterSettlementAuthorityState : Bill.ElectricitySettlementAuthorityState
            : Record!.SettlementAuthorityState;
        public long CurrentVersion => Bill is not null
            ? Scope.SourcePart == CollectionSourcePart.Water ? Bill.WaterSourceVersion : Bill.ElectricitySourceVersion
            : Record!.SettlementVersion;
        public decimal? Assessment { get; }
        public decimal? LegacySettled { get; }
        public decimal? Outstanding { get; }
        public string ClassificationCode { get; }
        public RevenueInstrumentType RequiredInstrument { get; }
        public Guid? FacilityId { get; }
        public string Label { get; }
        public string? LegacyDocumentEvidence { get; }
        public string? LegacyWriterStatus { get; }
        public string MobileWriterStatus { get; }
        public bool HasMixedLegacyComponents { get; }
        public void MarkPending()
        {
            if (Bill is not null && Scope.SourcePart == CollectionSourcePart.Water) Bill.MarkWaterPendingCutover();
            else if (Bill is not null) Bill.MarkElectricityPendingCutover();
            else Record!.MarkSettlementPendingCutover();
        }
        public long AdvanceBoundary() => Bill is not null
            ? Scope.SourcePart == CollectionSourcePart.Water
                ? Bill.AdvancePendingWaterCutoverBoundary() : Bill.AdvancePendingElectricityCutoverBoundary()
            : Record!.AdvancePendingSettlementCutoverBoundary();
        public void Activate(CollectionSettlementCutover cutover)
        {
            if (Bill is not null && Scope.SourcePart == CollectionSourcePart.Water) Bill.ActivateCanonicalWaterSettlement(cutover);
            else if (Bill is not null) Bill.ActivateCanonicalElectricitySettlement(cutover);
            else Record!.ActivateCanonicalSettlement(cutover);
        }
    }

    private sealed class CutoverProblem(string message, ResultStatus status) : Exception(message)
    {
        public ResultStatus Status { get; } = status;
    }
}

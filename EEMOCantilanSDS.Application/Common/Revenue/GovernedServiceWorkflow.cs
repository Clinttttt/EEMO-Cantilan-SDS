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

    public static readonly IReadOnlyList<Entry> All =
    [
        new(CollectorOperationCodes.MarketFees, "Market Fees", RevenueClassificationCodes.MarketFees, false, Both),
        new(CollectorOperationCodes.LandingBerthing, "Landing / Berthing", RevenueClassificationCodes.LandingBerthing, false, Both),
        new(CollectorOperationCodes.TransferLargeCattle, "Transfer Large Cattle", RevenueClassificationCodes.TransferLargeCattle, false, Both),
        // A whole payment and a daily transaction are different amounts under different instruments; one fixed amount
        // cannot describe both, so this service records an approved direct amount (with an optional ceiling).
        new(CollectorOperationCodes.VegetableFruitSpaceRental, "Vegetable / Fruit Space Rental",
            RevenueClassificationCodes.VegetableFruitSpaceRental, true, [GovernedServiceBasis.DirectApprovedAmount]),
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

    /// <summary>The documents of the instrument this operation (and mode) resolves to that are present in this collector's custody.</summary>
    public Task<Result<IReadOnlyList<CashTicketDocumentDto>>> GetAvailableDocumentsAsync(
        string operationCode, GovernedServiceMode? mode, CancellationToken ct = default) =>
        Run<IReadOnlyList<CashTicketDocumentDto>>(async actor =>
        {
            var entry = GovernedServiceCatalog.Find(operationCode);
            if (actor.Role != "Collector" || entry is null
                || !await IsAssignedAsync(actor, entry.Code, ct))
                return Result<IReadOnlyList<CashTicketDocumentDto>>.Forbidden();
            if (entry.ModeAware != mode.HasValue)
                return Result<IReadOnlyList<CashTicketDocumentDto>>.Failure(
                    entry.ModeAware ? "Choose whole payment or daily transaction." : "This service has no transaction modes.", ResultStatus.Invalid);
            var resolved = await ResolvePolicyAsync(actor.TenantId, entry, mode, BusinessToday, ct);
            if (resolved?.Policy.PermittedInstrumentType is not { } instrument)
                return Result<IReadOnlyList<CashTicketDocumentDto>>.Success([]);
            var documents = await (
                    from document in db.AccountableDocuments.AsNoTracking()
                    join assignment in db.AccountableFormAssignments.AsNoTracking()
                        on new { document.MunicipalityId, DocumentId = document.Id }
                        equals new { assignment.MunicipalityId, DocumentId = assignment.AccountableDocumentId }
                    where document.MunicipalityId == actor.TenantId
                        && document.InstrumentType == instrument
                        && document.State == AccountableDocumentState.Assigned
                        && document.AssignedUserId == actor.UserId
                        && assignment.AssignedUserId == actor.UserId
                        && assignment.ReturnedAtUtc == null
                    orderby document.SerialNumber
                    select new CashTicketDocumentDto(document.Id, document.DocumentNumber, document.State, document.AssignedUserId, document.SerialNumber))
                .ToListAsync(ct);
            return Result<IReadOnlyList<CashTicketDocumentDto>>.Success(documents);
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

        var normalized = NormalizeIntent(actor, request);
        var fingerprint = PostingOperation.ComputeIntentFingerprint(IntentVersion, normalized, MobileOrigin, ActorId(actor));
        var prior = await FindOperationAsync(actor.TenantId, request.ClientOperationId, ct);
        if (prior is not null)
            return await ResolvePriorAsync(prior, fingerprint, actor, ct);

        var document = request.AccountableDocumentId == Guid.Empty ? null
            : await db.AccountableDocuments.SingleOrDefaultAsync(x =>
                x.MunicipalityId == actor.TenantId && x.Id == request.AccountableDocumentId, ct);

        var entry = GovernedServiceCatalog.Find(request.OperationCode);
        if (entry is null)
            return await RecordTerminalAsync(actor, request, normalized, document, "OPERATION_UNSUPPORTED",
                "This operation is not a governed configurable service.", ct);
        if (!await IsAssignedAsync(actor, entry.Code, ct))
            return await RecordTerminalAsync(actor, request, normalized, document, "COLLECTOR_OPERATION_NOT_ASSIGNED",
                "An explicit operation assignment for an active collector is required. A physically issued document is retained for office reconciliation.", ct);
        if (request.SchemaVersion != 1)
            return await RecordTerminalAsync(actor, request, normalized, document, "PAYLOAD_VERSION_UNSUPPORTED",
                "This collection payload version is not supported.", ct);
        if (request.AccountableDocumentId == Guid.Empty || string.IsNullOrWhiteSpace(request.DocumentNumber)
            || request.ReceivedAmount <= 0m
            || decimal.Round(request.ReceivedAmount, 2, MidpointRounding.ToZero) != request.ReceivedAmount)
            return await RecordTerminalAsync(actor, request, normalized, document, "INVALID_INTENT",
                "A positive received amount and the issued document identity are required.", ct);
        if (request.PayerName?.Trim().Length > 200 || request.Reference?.Trim().Length > 200)
            return await RecordTerminalAsync(actor, request, normalized, document, "INVALID_INTENT",
                "Payer and reference text must not exceed 200 characters.", ct);
        if (request.BusinessDate > BusinessToday)
            return await RecordTerminalAsync(actor, request, normalized, document, "FUTURE_BUSINESS_DATE",
                "Collection BusinessDate cannot be later than the current Philippine business date.", ct);
        if (request.IssuedAtUtc is not { Kind: DateTimeKind.Utc } issuedAt || issuedAt > DateTime.UtcNow)
            return await RecordTerminalAsync(actor, request, normalized, document, "INVALID_ISSUE_TIME",
                "The physical document issue time must be a valid past or present UTC timestamp.", ct);
        if (document is null)
            return await RecordTerminalAsync(actor, request, normalized, null, "DOCUMENT_NOT_FOUND",
                "The document identity was not found in this tenant.", ct);
        if (document.State != AccountableDocumentState.Assigned || document.AssignedUserId != actor.UserId)
            return await RecordTerminalAsync(actor, request, normalized, document, "DOCUMENT_CUSTODY_INVALID",
                "This document is not assigned to the current collector.", ct);
        if (!string.Equals(document.DocumentNumber, request.DocumentNumber.Trim(), StringComparison.Ordinal))
            return await RecordTerminalAsync(actor, request, normalized, document, "DOCUMENT_IDENTITY_INVALID",
                "The document number does not match the assigned document.", ct);
        if (entry.ModeAware != request.Mode.HasValue || request.Mode is { } m && !Enum.IsDefined(m))
            return await RecordTerminalAsync(actor, request, normalized, document, "INVALID_MODE",
                entry.ModeAware ? "Choose whole payment or daily transaction." : "This service has no transaction modes.", ct);

        try
        {
            var service = await db.GovernedServices.AsNoTracking().SingleOrDefaultAsync(x =>
                x.MunicipalityId == actor.TenantId && x.OperationCode == entry.Code, ct);
            var versions = service is null ? [] : await db.GovernedServiceSettings.AsNoTracking()
                .Where(x => x.MunicipalityId == actor.TenantId && x.GovernedServiceId == service.Id).ToListAsync(ct);
            var setting = GovernedServiceSetting.Resolve(versions, request.BusinessDate);
            if (service is null || setting is null)
                return await RecordTerminalAsync(actor, request, normalized, document, "SERVICE_SETUP_REQUIRED",
                    "This service has no approved amount rule for the business date. No Collection was created.", ct);
            if (!setting.IsEnabled)
                return await RecordTerminalAsync(actor, request, normalized, document, "SERVICE_DISABLED",
                    "This service is disabled for new transactions.", ct);
            if (!setting.MobileEnabled)
                return await RecordTerminalAsync(actor, request, normalized, document, "MOBILE_CHANNEL_DISABLED",
                    "This service is not enabled for Collector Mobile.", ct);
            var resolved = await ResolvePolicyAsync(actor.TenantId, entry, request.Mode, request.BusinessDate, ct);
            if (resolved is null)
                return await RecordTerminalAsync(actor, request, normalized, document, "POLICY_NOT_EFFECTIVE",
                    "No effective approved instrument policy exists for this operation and business date.", ct);
            var instrument = resolved.Policy.PermittedInstrumentType!.Value;
            if (document.InstrumentType != instrument)
                return await RecordTerminalAsync(actor, request, normalized, document, "INSTRUMENT_POLICY_CONFLICT",
                    $"The approved policy requires {(instrument == RevenueInstrumentType.OfficialReceipt ? "an Official Receipt" : "a Cash Ticket")} for this transaction.", ct);
            if (setting.CheckAmount(request.ReceivedAmount) is { } amountProblem)
                return await RecordTerminalAsync(actor, request, normalized, document, amountProblem,
                    amountProblem == "AMOUNT_ABOVE_CEILING"
                        ? "The amount exceeds the approved ceiling for this service."
                        : "The amount is not the approved amount for this service.", ct);

            var snapshot = JsonSerializer.Serialize(new GovernedSnapshot(
                1, actor.TenantId, service.Id, entry.Code, setting.Id, setting.EffectiveDate, setting.Basis,
                setting.FixedAmount, setting.MaximumAmount, request.Mode, instrument, entry.ClassificationCode,
                resolved.Policy.Id, resolved.Policy.EffectiveDate, request.Reference?.Trim(), request.PayerName?.Trim()), JsonOptions);
            // An immediate activity charge: an approved source identity and frozen evidence, no fabricated receivable.
            var line = new CollectionLineDraft(resolved.Classification, resolved.Policy, request.ReceivedAmount,
                CollectionSourceKind.GovernedService, service.Id, null, snapshot, null);
            var collection = await new CanonicalCollectionPostingCoordinator(db).PostAsync(
                actor.TenantId, request.ClientOperationId, IntentVersion, normalized, MobileOrigin, ActorId(actor),
                actor.Username, actor.Role, request.BusinessDate, actor.Username, [line], document,
                collectorId: actor.UserId, payerName: string.IsNullOrWhiteSpace(request.PayerName) ? null : request.PayerName.Trim(), ct: ct);
            return Result<GovernedServiceOutcomeDto>.Success(new(collection.Id, document.Id, document.DocumentNumber,
                collection.BusinessDate, collection.TotalAmount, instrument, "Posted", false));
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var winner = await FindOperationAsync(actor.TenantId, request.ClientOperationId, ct);
            if (winner is not null) return await ResolvePriorAsync(winner, fingerprint, actor, ct);
            var current = await db.AccountableDocuments.SingleOrDefaultAsync(x =>
                x.MunicipalityId == actor.TenantId && x.Id == request.AccountableDocumentId, ct);
            return await RecordTerminalAsync(actor, request, normalized, current, "POST_CONFLICT",
                "The document or operation identity conflicts with another posting; no Collection was partially posted.", ct);
        }
    }

    private async Task<Result<GovernedServiceOutcomeDto>> RecordTerminalAsync(
        Actor actor, GovernedServicePostRequest request, string normalized, AccountableDocument? document,
        string code, string message, CancellationToken ct)
    {
        // A collector who wrote a physical document has issued it whatever the server now says: it is quarantined for
        // office reconciliation and never returned to stock. Without a document identity there is nothing issued.
        var physicalIssue = request.AccountableDocumentId != Guid.Empty && !string.IsNullOrWhiteSpace(request.DocumentNumber);
        var issued = document;
        if (physicalIssue && (issued is null
            || !string.Equals(issued.DocumentNumber, request.DocumentNumber.Trim(), StringComparison.Ordinal)))
        {
            var number = request.DocumentNumber.Trim();
            issued = await db.AccountableDocuments.SingleOrDefaultAsync(x =>
                x.MunicipalityId == actor.TenantId && x.DocumentNumber == number, ct);
        }
        var issueTime = request.IssuedAtUtc is { Kind: DateTimeKind.Utc } at && at <= DateTime.UtcNow ? at : UtcNow;
        if (physicalIssue && issued is not null
            && await CanQuarantineAsync(issued, actor, ct)
            && (issued.ClientOperationId is null || issued.ClientOperationId == request.ClientOperationId))
            issued.MarkPhysicalIssueReconciliationRequired(request.ClientOperationId, issueTime, actor.Username);

        db.PostingOperations.Add(PostingOperation.Record(actor.TenantId, request.ClientOperationId,
            IntentVersion, normalized, MobileOrigin, ActorId(actor),
            physicalIssue ? PostingOperationStatus.ReconciliationRequired : PostingOperationStatus.Rejected,
            physicalIssue ? "PHYSICAL_DOCUMENT_RECONCILIATION" : code,
            physicalIssue
                ? $"RECONCILIATION_REQUIRED: {message} Document {issued?.DocumentNumber ?? request.DocumentNumber} remains unavailable for reuse and requires review."
                : message,
            null, issued?.Id ?? document?.Id, DateTime.UtcNow));
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
        return Result<GovernedServiceOutcomeDto>.Failure(
            physicalIssue ? $"RECONCILIATION_REQUIRED: {message} A physically issued document remains unavailable for reuse and requires office review." : message,
            ResultStatus.Conflict);
    }

    private async Task<Result<GovernedServiceOutcomeDto>> ResolvePriorAsync(
        PostingOperation prior, string fingerprint, Actor actor, CancellationToken ct)
    {
        if (prior.Origin != MobileOrigin || prior.ActorId != ActorId(actor) || prior.IntentFingerprint != fingerprint)
            return Result<GovernedServiceOutcomeDto>.Failure(
                "IDEMPOTENCY CONFLICT: this ClientOperationId is already bound to a different posting intent.", ResultStatus.Conflict);
        if (prior.Status != PostingOperationStatus.Succeeded || prior.CollectionId is not { } collectionId)
            return Result<GovernedServiceOutcomeDto>.Failure(
                prior.OutcomeDetails ?? prior.OutcomeCode ?? "The original posting was rejected.", ResultStatus.Conflict);
        var collection = await db.Collections.AsNoTracking().Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.MunicipalityId == actor.TenantId && x.Id == collectionId, ct);
        if (collection is null)
            return Result<GovernedServiceOutcomeDto>.Failure("The recorded Collection outcome is unavailable.", ResultStatus.Conflict);
        var document = prior.AccountableDocumentId is { } documentId
            ? await db.AccountableDocuments.AsNoTracking().SingleOrDefaultAsync(x =>
                x.MunicipalityId == actor.TenantId && x.Id == documentId, ct)
            : null;
        return Result<GovernedServiceOutcomeDto>.Success(new(collection.Id, document?.Id ?? Guid.Empty,
            document?.DocumentNumber ?? "Document unavailable", collection.BusinessDate, collection.TotalAmount,
            document?.InstrumentType ?? RevenueInstrumentType.CashTicket, "Posted", true));
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
            var documents = await db.AccountableDocuments.AsNoTracking().Where(x =>
                x.MunicipalityId == actor.TenantId && x.CollectionId.HasValue && collectionIds.Contains(x.CollectionId.Value)).ToListAsync(ct);
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
                var document = documents.FirstOrDefault(x => x.CollectionId == collection.Id);
                var effects = corrections.Where(x => x.OriginalCollectionId == collection.Id).ToList();
                var disposition = effects.Any(x => x.FinancialEffectAmount < 0m) ? "Reversed"
                    : effects.Any(x => x.CorrectionType == CollectionCorrectionType.DocumentCorrection) ? "Document corrected" : "Posted";
                return new GovernedServiceActivityDto(collection.Id, collection.BusinessDate, collection.RecordedAtUtc,
                    document?.DocumentNumber ?? "Document unavailable", document?.InstrumentType, facts?.Mode,
                    collection.PayerName, facts?.Reference, line.Amount,
                    collection.CollectorId is { } id ? collectors.GetValueOrDefault(id) : null, disposition);
            }).ToList();
            return Result<IReadOnlyList<GovernedServiceActivityDto>>.Success(activity);
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

    private async Task<bool> CanQuarantineAsync(AccountableDocument document, Actor actor, CancellationToken ct)
    {
        if (document.State != AccountableDocumentState.Assigned || document.AssignedUserId != actor.UserId)
            return false;
        return await db.AccountableFormAssignments.AsNoTracking().AnyAsync(x =>
            x.MunicipalityId == actor.TenantId && x.AccountableDocumentId == document.Id
            && x.AssignedUserId == actor.UserId && x.ReturnedAtUtc == null, ct);
    }

    private Task<PostingOperation?> FindOperationAsync(Guid tenantId, Guid operationId, CancellationToken ct) =>
        db.PostingOperations.AsNoTracking().SingleOrDefaultAsync(x =>
            x.MunicipalityId == tenantId && x.ClientOperationId == operationId, ct);

    private static string NormalizeIntent(Actor actor, GovernedServicePostRequest request) =>
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
            Reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim(),
            AccountableDocumentId = request.AccountableDocumentId,
            DocumentNumber = request.DocumentNumber?.Trim(),
            IssuedAtUtc = request.IssuedAtUtc?.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture)
        }, JsonOptions);

    private static string ActorId(Actor actor) => actor.UserId.ToString("N");

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
        Guid PolicyId, DateOnly PolicyEffectiveDate, string? Reference, string? PayerName);
}

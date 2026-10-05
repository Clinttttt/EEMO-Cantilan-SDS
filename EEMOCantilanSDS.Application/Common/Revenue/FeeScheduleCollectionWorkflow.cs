using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Slaughterhouse;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Domain.Entities.TaboanMarket;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// Canonical Collector Mobile collection for the two services whose amount is already defined by the office's existing fee
/// schedule: Tabo (vendor market-day fee) and the current Slaughterhouse transaction (per-head approved rate x heads). The
/// amount is NEVER typed or re-derived here: it is the amount the existing legacy rules produce (fee-rate resolver, the existing
/// SlaughterTransaction calculation, the approved custom-animal registry), and the collector's confirmed amount must equal it.
/// Money is written by the one canonical posting coordinator as a Collection with an SRC; no legacy operational row is created, so
/// one payment can never be both a legacy record and a Collection. The Head enabling the service (as for Transportation) is the
/// prospective boundary; before it, and for Web/Admin entry, the existing legacy writers are unchanged.
/// </summary>
public sealed class FeeScheduleCollectionWorkflow(
    IAppDbContext db,
    ICurrentUserService currentUser,
    ICurrentMunicipalityAccessor municipality,
    IFeeRateResolver feeRates,
    ITpmMarketDayProvider marketDays,
    IClock? clock = null)
{
    private const string Origin = "MobileFeeSchedule";
    private const int IntentVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private DateOnly BusinessToday => clock?.PhilippineToday ?? PhilippineTime.Today;

    public static bool Handles(string? operationCode) =>
        operationCode is CollectorOperationCodes.Tabo or CollectorOperationCodes.Slaughterhouse;

    public sealed record SlaughterQuote(decimal Amount, RevenueInstrumentType Instrument, string Context, string Version);
    /// <summary>Read-only preview of the SAME calculation used by PostMobileAsync.</summary>
    public async Task<Result<SlaughterQuote>> QuoteSlaughterAsync(FeeSchedulePostRequest request, CancellationToken ct = default)
    {
        var tenant = municipality.MunicipalityId;
        if (!currentUser.IsAuthenticated || currentUser.Role != "Collector" || currentUser.CollectorId is not { } collector
            || tenant == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenant
            || request.OperationCode != CollectorOperationCodes.Slaughterhouse || request.BusinessDate == default || request.BusinessDate > BusinessToday
            || !await db.CollectorUsers.AsNoTracking().AnyAsync(x => x.MunicipalityId == tenant && x.Id == collector && x.IsActive
                && x.FacilityAssignments.Any(a => a.FacilityCode == FacilityCode.SLH), ct)) return Result<SlaughterQuote>.Forbidden();
        var resolved = await ResolveSlaughterAsync(tenant, collector, currentUser.Username ?? "Collector", request, ct);
        if (resolved.Problem is { } problem) return Result<SlaughterQuote>.Failure(problem.Message);
        var service = await db.GovernedServices.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == tenant && x.OperationCode == request.OperationCode, ct);
        if (service is null) return Result<SlaughterQuote>.Failure("Slaughterhouse collection is not currently available.");
        var setting = GovernedServiceSetting.Resolve(await db.GovernedServiceSettings.AsNoTracking().Where(x => x.MunicipalityId == tenant && x.GovernedServiceId == service.Id).ToListAsync(ct), request.BusinessDate);
        if (setting is null || !setting.IsEnabled || !setting.MobileEnabled) return Result<SlaughterQuote>.Failure("Slaughterhouse collection is not currently available.");
        if (setting.MaximumAmount is { } ceiling && resolved.Amount > ceiling)
            return Result<SlaughterQuote>.Failure("The amount exceeds the approved Slaughterhouse ceiling.");
        var classification = await db.RevenueClassifications.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == tenant && x.IsActive && x.SemanticCode == RevenueClassificationCodes.Slaughterhouse, ct);
        if (classification is null) return Result<SlaughterQuote>.Failure("The collection policy is unavailable.");
        var policy = await db.RevenueClassificationPolicies.AsNoTracking().Where(x => x.MunicipalityId == tenant && x.RevenueClassificationId == classification.Id && x.BusinessContext == RevenuePolicyContext.Default && x.EffectiveDate <= request.BusinessDate)
            .OrderByDescending(x => x.EffectiveDate).FirstOrDefaultAsync(ct);
        if (policy?.PermittedInstrumentType is not { } instrument) return Result<SlaughterQuote>.Failure("The collection policy is unavailable.");
        var version = JsonSerializer.Serialize(new { setting.Id, setting.MaximumAmount, Policy = policy.Id, resolved.Facts, resolved.Amount }, JsonOptions);
        return Result<SlaughterQuote>.Success(new(resolved.Amount, instrument, resolved.Reference!, version));
    }

    public async Task<Result<GovernedServiceOutcomeDto>> PostMobileAsync(FeeSchedulePostRequest request, CancellationToken ct = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.Role != "Collector" || currentUser.CollectorId is not { } collectorId
            || collectorId == Guid.Empty || !Handles(request.OperationCode))
            return Result<GovernedServiceOutcomeDto>.Forbidden();
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenantId)
            return Result<GovernedServiceOutcomeDto>.Forbidden();
        if (request.ClientOperationId == Guid.Empty)
            return Result<GovernedServiceOutcomeDto>.Failure("A valid ClientOperationId is required.", ResultStatus.Invalid);

        var isTabo = request.OperationCode == CollectorOperationCodes.Tabo;
        var facilityCode = isTabo ? FacilityCode.TPM : FacilityCode.SLH;
        var collector = await db.CollectorUsers.AsNoTracking().Include(x => x.FacilityAssignments)
            .SingleOrDefaultAsync(x => x.MunicipalityId == tenantId && x.Id == collectorId && x.IsActive, ct);
        if (collector is null || collector.FacilityAssignments.All(x => x.FacilityCode != facilityCode))
            return Result<GovernedServiceOutcomeDto>.Forbidden();
        var username = collector.Username ?? collector.FullName ?? "Collector";
        var actorId = collectorId.ToString("N");

        var normalized = JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            OperationType = "FeeScheduleCollection",
            TenantId = tenantId,
            ActorId = actorId,
            request.OperationCode,
            BusinessDate = request.BusinessDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ReceivedAmount = request.ReceivedAmount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            request.VendorId,
            VendorName = request.VendorName?.Trim(),
            Goods = request.Goods?.Trim(),
            request.Animal,
            CustomAnimalName = request.CustomAnimalName?.Trim(),
            request.Heads,
            OwnerName = request.OwnerName?.Trim()
        }, JsonOptions);
        var fingerprint = PostingOperation.ComputeIntentFingerprint(IntentVersion, normalized, Origin, actorId);

        try
        {
            var prior = await db.PostingOperations.AsNoTracking().SingleOrDefaultAsync(x =>
                x.MunicipalityId == tenantId && x.ClientOperationId == request.ClientOperationId, ct);
            if (prior is not null) return await ResolvePriorAsync(prior, fingerprint, actorId, tenantId, ct);

            Task<Result<GovernedServiceOutcomeDto>> Reject(string code, string message) =>
                RejectAsync(tenantId, actorId, request, normalized, fingerprint, code, message, ct);

            if (request.ReceivedAmount <= 0m || decimal.Round(request.ReceivedAmount, 2, MidpointRounding.ToZero) != request.ReceivedAmount)
                return await Reject("INVALID_INTENT", "A positive confirmed amount with at most two decimals is required.");
            if (request.BusinessDate > BusinessToday)
                return await Reject("FUTURE_BUSINESS_DATE", "Collection BusinessDate cannot be later than the current Philippine business date.");

            // The prospective boundary: the Head has enabled this service for Collector Mobile on this business date.
            var service = await db.GovernedServices.AsNoTracking().SingleOrDefaultAsync(x =>
                x.MunicipalityId == tenantId && x.OperationCode == request.OperationCode, ct);
            var versions = service is null ? [] : await db.GovernedServiceSettings.AsNoTracking()
                .Where(x => x.MunicipalityId == tenantId && x.GovernedServiceId == service.Id).ToListAsync(ct);
            var setting = GovernedServiceSetting.Resolve(versions, request.BusinessDate);
            if (service is null || setting is null || !setting.IsEnabled || !setting.MobileEnabled)
                return await Reject("SERVICE_NOT_ENABLED",
                    "This service is not yet collected through canonical collections; it stays on its existing path until the office enables it.");

            var classificationCode = isTabo ? RevenueClassificationCodes.Tabo : RevenueClassificationCodes.Slaughterhouse;
            var classification = await db.RevenueClassifications.SingleOrDefaultAsync(x =>
                x.MunicipalityId == tenantId && x.SemanticCode == classificationCode && x.IsActive, ct);
            var policy = classification is null ? null : await db.RevenueClassificationPolicies.Where(x =>
                    x.MunicipalityId == tenantId && x.RevenueClassificationId == classification.Id
                    && x.BusinessContext == RevenuePolicyContext.Default && x.EffectiveDate <= request.BusinessDate)
                .OrderByDescending(x => x.EffectiveDate).FirstOrDefaultAsync(ct);
            if (classification is null || policy?.PermittedInstrumentType is not { } instrument)
                return await Reject("POLICY_NOT_EFFECTIVE", "No effective approved instrument policy exists for this service and business date.");

            decimal amount;
            string payer;
            string reference;
            object facts;
            if (isTabo)
            {
                var tabo = await ResolveTaboAsync(tenantId, request, ct);
                if (tabo.Problem is { } problem) return await Reject(problem.Code, problem.Message);
                (amount, payer, reference, facts) = (tabo.Amount, tabo.Payer!, tabo.Reference!, tabo.Facts!);
            }
            else
            {
                var slaughter = await ResolveSlaughterAsync(tenantId, collectorId, username, request, ct);
                if (slaughter.Problem is { } problem) return await Reject(problem.Code, problem.Message);
                (amount, payer, reference, facts) = (slaughter.Amount, slaughter.Payer!, slaughter.Reference!, slaughter.Facts!);
            }
            if (amount != request.ReceivedAmount)
                return await Reject("AMOUNT_NOT_APPROVED",
                    $"The confirmed amount is not the amount the current fee schedule gives for this transaction (PHP {amount:N2}).");
            if (setting.MaximumAmount is { } ceiling && amount > ceiling)
                return await Reject("AMOUNT_ABOVE_CEILING", "The amount exceeds the approved ceiling for this service.");

            var snapshot = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                municipalityId = tenantId,
                serviceId = service.Id,
                operationCode = request.OperationCode,
                settingId = setting.Id,
                settingEffectiveDate = setting.EffectiveDate,
                basis = setting.Basis,
                fixedAmount = setting.FixedAmount,
                maximumAmount = setting.MaximumAmount,
                mode = (GovernedServiceMode?)null,
                instrument,
                classificationCode,
                policyId = policy.Id,
                policyEffectiveDate = policy.EffectiveDate,
                reference,
                payerName = payer,
                source = facts
            }, JsonOptions);
            var line = new CollectionLineDraft(classification, policy, amount, CollectionSourceKind.GovernedService,
                service.Id, null, snapshot, null);
            var collection = await new CanonicalCollectionPostingCoordinator(db).PostAsync(
                tenantId, request.ClientOperationId, IntentVersion, normalized, Origin, actorId, username, "Collector",
                request.BusinessDate, username, [line], null, collectorId: collectorId, payerName: payer, ct: ct);
            return Result<GovernedServiceOutcomeDto>.Success(new GovernedServiceOutcomeDto(
                collection.Id, collection.ReferenceCode, collection.BusinessDate, collection.TotalAmount, instrument, "Posted", false));
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var winner = await db.PostingOperations.AsNoTracking().SingleOrDefaultAsync(x =>
                x.MunicipalityId == tenantId && x.ClientOperationId == request.ClientOperationId, ct);
            if (winner is not null) return await ResolvePriorAsync(winner, fingerprint, actorId, tenantId, ct);
            return await RejectAsync(tenantId, actorId, request, normalized, fingerprint, "POST_CONFLICT",
                "The operation identity conflicts with another saved transaction; nothing was posted.", ct);
        }
    }

    private sealed record Problem(string Code, string Message);
    private sealed record Resolved(decimal Amount, string? Payer, string? Reference, object? Facts, Problem? Problem)
    {
        public static Resolved Fail(string code, string message) => new(0m, null, null, null, new Problem(code, message));
    }

    // ── Tabo: the existing vendor market-day fee for a registered vendor on a market day ─────────────────

    private async Task<Resolved> ResolveTaboAsync(Guid tenantId, FeeSchedulePostRequest request, CancellationToken ct)
    {
        var marketDay = await marketDays.GetMarketDayAsync(request.BusinessDate, ct);
        if (request.BusinessDate.DayOfWeek != marketDay)
            return Resolved.Fail("NOT_A_MARKET_DAY", $"Market date must be a {marketDay}.");

        TpmVendor? vendor = null;
        if (request.VendorId is { } vendorId)
        {
            vendor = await db.TpmVendors.SingleOrDefaultAsync(x => x.MunicipalityId == tenantId && x.Id == vendorId, ct);
            if (vendor is null || !vendor.IsActive)
                return Resolved.Fail("SOURCE_NOT_FOUND", "The vendor is not available in this tenant.");
        }
        else
        {
            // The existing Tabo rule, unchanged: the vendor registry is matched by name (case-insensitive); an unknown name
            // registers a new vendor. This is a vendor-registry identity, never a Payor.
            var name = request.VendorName?.Trim();
            var goods = request.Goods?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                return Resolved.Fail("INVALID_INTENT", "The vendor name is required.");
            vendor = await db.TpmVendors.FirstOrDefaultAsync(x => x.MunicipalityId == tenantId && x.VendorName.ToUpper() == name.ToUpper(), ct);
            if (vendor is null)
            {
                if (string.IsNullOrWhiteSpace(goods))
                    return Resolved.Fail("INVALID_INTENT", "Goods are required to register a new vendor.");
                vendor = TpmVendor.Create(name, goods);
                db.TpmVendors.Add(vendor);
            }
        }

        // One vendor, one market day: neither an earlier legacy attendance nor an earlier canonical collection may exist.
        if (vendor.Id != Guid.Empty && await db.TpmAttendances.AsNoTracking().AnyAsync(x =>
                x.MunicipalityId == tenantId && x.VendorId == vendor.Id && x.MarketDate == request.BusinessDate, ct))
            return Resolved.Fail("ALREADY_ADDED", "Vendor already added to this market day.");
        var vendorKey = $"\"vendorId\":\"{vendor.Id}\"";
        var tabo = await db.RevenueClassifications.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && x.SemanticCode == RevenueClassificationCodes.Tabo)
            .Select(x => x.Id).ToListAsync(ct);
        if (await db.CollectionLines.AsNoTracking().AnyAsync(x => x.MunicipalityId == tenantId
                && tabo.Contains(x.RevenueClassificationId) && x.CalculationSnapshot!.Contains(vendorKey)
                && x.CalculationSnapshot!.Contains($"\"marketDate\":\"{request.BusinessDate:yyyy-MM-dd}\""), ct))
            return Resolved.Fail("ALREADY_ADDED", "Vendor already added to this market day.");

        var snapshot = await feeRates.GetSnapshotAsync(ct);
        if (snapshot.ResolveOrNull(FeeRateKey.TpmVendorDay, request.BusinessDate) is not { } fee)
            return Resolved.Fail("RATE_NOT_STATED", FeeRateMessages.NotStated(FeeRateKey.TpmVendorDay));
        var reference = $"{vendor.Goods} · {request.BusinessDate:MMM d, yyyy}";
        return new Resolved(fee, vendor.VendorName, reference, new
        {
            kind = "Tabo", vendorId = vendor.Id, vendorName = vendor.VendorName, goods = vendor.Goods,
            marketDate = request.BusinessDate.ToString("yyyy-MM-dd"), marketDay = marketDay.ToString(), vendorDayFee = fee
        }, null);
    }

    // ── Slaughterhouse: the existing per-transaction calculation, unchanged ──────────────────────────────

    private async Task<Resolved> ResolveSlaughterAsync(
        Guid tenantId, Guid collectorId, string username, FeeSchedulePostRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.OwnerName) || request.OwnerName.Trim().Length > 200)
            return Resolved.Fail("INVALID_INTENT", "The owner name is required.");
        if (request.Heads is not > 0 or > 1000 || request.Animal is not { } animal || !Enum.IsDefined(animal))
            return Resolved.Fail("INVALID_INTENT", "An approved animal and a head count are required.");
        var facility = await db.Facilities.AsNoTracking().SingleOrDefaultAsync(x =>
            x.MunicipalityId == tenantId && x.Code == FacilityCode.SLH, ct);
        if (facility is null) return Resolved.Fail("SOURCE_NOT_FOUND", "The slaughterhouse facility is not available in this tenant.");

        var snapshot = await feeRates.GetSnapshotAsync(ct);
        var key = SlaughterRateKeys.For(animal);
        var perHead = key is { } k ? snapshot.ResolveOrNull(k, request.BusinessDate) : null;
        if (key is { } required && perHead is null)
            return Resolved.Fail("RATE_NOT_STATED", FeeRateMessages.NotStated(required));

        // The same rules as the existing record: an unusual animal is only an approved registry animal at its approved rate.
        string? approvedName = null;
        decimal? approvedRate = null;
        if (animal == AnimalType.Other)
        {
            var wanted = request.CustomAnimalName?.Trim();
            var approved = await db.SlaughterAnimalRates.AsNoTracking()
                .FirstOrDefaultAsync(x => x.MunicipalityId == tenantId && x.IsActive && x.AnimalName.ToUpper() == (wanted ?? "").ToUpper(), ct);
            if (approved is null)
                return Resolved.Fail("ANIMAL_NOT_APPROVED", $"'{wanted}' is not an approved slaughter animal.");
            (approvedName, approvedRate) = (approved.AnimalName, approved.RatePerHead);
        }

        // The existing calculation, run on an unsaved transaction: nothing here is a second fee algorithm and nothing is stored.
        var heads = request.Heads!.Value;
        var transaction = animal switch
        {
            AnimalType.Hog => SlaughterTransaction.CreateHog(facility.Id, collectorId, request.OwnerName, heads, string.Empty,
                request.BusinessDate, username, ratePerHead: perHead),
            AnimalType.Carabao or AnimalType.Cow => SlaughterTransaction.CreateLargeAnimal(facility.Id, collectorId, request.OwnerName,
                animal, heads, string.Empty, request.BusinessDate, username, ratePerHead: perHead),
            _ => SlaughterTransaction.CreateCustomAnimal(facility.Id, collectorId, request.OwnerName, approvedName!, heads,
                approvedRate!.Value, string.Empty, request.BusinessDate, username)
        };
        var label = animal == AnimalType.Other ? approvedName! : animal.ToString();
        return new Resolved(transaction.TotalAmount, transaction.OwnerName, $"{heads} {label}", new
        {
            kind = "Slaughterhouse", ownerName = transaction.OwnerName, animal = animal.ToString(), customAnimalName = approvedName,
            heads, ratePerHead = transaction.RatePerHead, total = transaction.TotalAmount,
            breakdown = new
            {
                transaction.SlaughterFee, transaction.SlaughterPermit, transaction.AntemortemFee, transaction.PostmortemFee,
                transaction.TableCharge, transaction.EntranceFee, transaction.LivestockFee
            }
        }, null);
    }

    // ── Idempotency and durable rejection ────────────────────────────────────────────────────────────────

    private async Task<Result<GovernedServiceOutcomeDto>> ResolvePriorAsync(
        PostingOperation prior, string fingerprint, string actorId, Guid tenantId, CancellationToken ct)
    {
        if (prior.Origin != Origin || prior.ActorId != actorId || prior.IntentFingerprint != fingerprint)
            return Result<GovernedServiceOutcomeDto>.Failure(
                "IDEMPOTENCY CONFLICT: this ClientOperationId is already bound to a different posting intent.", ResultStatus.Conflict);
        if (prior.Status != PostingOperationStatus.Succeeded || prior.CollectionId is not { } collectionId)
            return Result<GovernedServiceOutcomeDto>.Failure(prior.OutcomeDetails ?? prior.OutcomeCode ?? "The original posting was rejected.", ResultStatus.Conflict);
        var collection = await db.Collections.AsNoTracking().Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.MunicipalityId == tenantId && x.Id == collectionId, ct);
        if (collection is null)
            return Result<GovernedServiceOutcomeDto>.Failure("The recorded Collection outcome is unavailable.", ResultStatus.Conflict);
        var instrument = ReadInstrument(collection.Lines.OrderBy(x => x.Id).FirstOrDefault()?.CalculationSnapshot);
        return Result<GovernedServiceOutcomeDto>.Success(new GovernedServiceOutcomeDto(
            collection.Id, collection.ReferenceCode, collection.BusinessDate, collection.TotalAmount, instrument, "Posted", true));
    }

    private static RevenueInstrumentType ReadInstrument(string? snapshot)
    {
        try
        {
            using var doc = JsonDocument.Parse(snapshot ?? "{}");
            return doc.RootElement.TryGetProperty("instrument", out var v) && v.TryGetInt32(out var n)
                ? (RevenueInstrumentType)n : RevenueInstrumentType.OfficialReceipt;
        }
        catch (JsonException) { return RevenueInstrumentType.OfficialReceipt; }
    }

    private async Task<Result<GovernedServiceOutcomeDto>> RejectAsync(
        Guid tenantId, string actorId, FeeSchedulePostRequest request, string normalized, string fingerprint,
        string code, string message, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        db.PostingOperations.Add(PostingOperation.Record(tenantId, request.ClientOperationId, IntentVersion, normalized, Origin,
            actorId, PostingOperationStatus.Rejected, code, message, null, null, DateTime.UtcNow));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var prior = await db.PostingOperations.AsNoTracking().SingleOrDefaultAsync(x =>
                x.MunicipalityId == tenantId && x.ClientOperationId == request.ClientOperationId, ct);
            if (prior is not null) return await ResolvePriorAsync(prior, fingerprint, actorId, tenantId, ct);
            throw;
        }
        return Result<GovernedServiceOutcomeDto>.Failure(message, ResultStatus.Conflict);
    }
}

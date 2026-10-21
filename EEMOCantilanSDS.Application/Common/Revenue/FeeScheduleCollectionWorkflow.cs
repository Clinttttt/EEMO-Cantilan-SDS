using System.Text.Json;
using System.Text.Json.Nodes;
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
public sealed partial class FeeScheduleCollectionWorkflow(
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

    public sealed record SlaughterQuote(decimal Amount, RevenueInstrumentType Instrument, string Context, string Version,
        IReadOnlyList<GovernedActivityItemDto>? Items = null);
    /// <summary>Read-only preview of the SAME calculation used by PostMobileAsync.</summary>
    public async Task<Result<SlaughterQuote>> QuoteSlaughterAsync(FeeSchedulePostRequest request, CancellationToken ct = default)
    {
        var tenant = municipality.MunicipalityId;
        if (!currentUser.IsAuthenticated || currentUser.Role != "Collector" || currentUser.CollectorId is not { } collector
            || tenant == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenant
            || request.OperationCode != CollectorOperationCodes.Slaughterhouse || request.BusinessDate == default || request.BusinessDate > BusinessToday
            || !await db.CollectorUsers.AsNoTracking().AnyAsync(x => x.MunicipalityId == tenant && x.Id == collector && x.IsActive
                && x.FacilityAssignments.Any(a => a.FacilityCode == FacilityCode.SLH), ct)) return Result<SlaughterQuote>.Forbidden();
        return await QuoteSlaughterCoreAsync(request, tenant, collector, currentUser.Username ?? "Collector", true, ct);
    }

    private async Task<Result<SlaughterQuote>> QuoteSlaughterCoreAsync(
        FeeSchedulePostRequest request, Guid tenant, Guid? collector, string username, bool mobile, CancellationToken ct)
    {
        var service = await db.GovernedServices.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == tenant && x.OperationCode == request.OperationCode, ct);
        if (service is null) return Result<SlaughterQuote>.Failure("Slaughterhouse collection is not currently available.");
        var setting = GovernedServiceSetting.Resolve(await db.GovernedServiceSettings.AsNoTracking().Where(x => x.MunicipalityId == tenant && x.GovernedServiceId == service.Id).ToListAsync(ct), request.BusinessDate);
        if (setting is null || !setting.IsEnabled || mobile && !setting.MobileEnabled || !mobile && setting.Basis != GovernedServiceBasis.ApprovedFeeOption) return Result<SlaughterQuote>.Failure("Slaughterhouse collection is not currently available.");
        var resolved = await ResolveSlaughterAsync(tenant, collector ?? Guid.Empty, username, request, service.Id,
            setting.Basis == GovernedServiceBasis.ApprovedFeeOption, ct);
        if (resolved.Problem is { } problem) return Result<SlaughterQuote>.Failure(problem.Message);
        if (setting.MaximumAmount is { } ceiling && resolved.Amount > ceiling)
            return Result<SlaughterQuote>.Failure("The amount exceeds the approved Slaughterhouse ceiling.");
        var classification = await db.RevenueClassifications.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == tenant && x.IsActive && x.SemanticCode == RevenueClassificationCodes.Slaughterhouse, ct);
        if (classification is null) return Result<SlaughterQuote>.Failure("The collection policy is unavailable.");
        var policy = await db.RevenueClassificationPolicies.AsNoTracking().Where(x => x.MunicipalityId == tenant && x.RevenueClassificationId == classification.Id && x.BusinessContext == RevenuePolicyContext.Default && x.EffectiveDate <= request.BusinessDate)
            .OrderByDescending(x => x.EffectiveDate).FirstOrDefaultAsync(ct);
        if (policy?.PermittedInstrumentType is not { } instrument) return Result<SlaughterQuote>.Failure("The collection policy is unavailable.");
        var version = JsonSerializer.Serialize(new { setting.Id, setting.MaximumAmount, Policy = policy.Id, resolved.Facts, resolved.Amount }, JsonOptions);
        var items = resolved.Charges?.Select(c => new GovernedActivityItemDto(c.FeeOptionId, c.Name, c.Quantity, c.UnitAmount, c.Amount, c.RateEffectiveDate)).ToList();
        return Result<SlaughterQuote>.Success(new(resolved.Amount, instrument, resolved.Reference!, version, items));
    }

    /// <summary>
    /// What a facility-assigned collector may select today. When the Head has the service on approved charges (<c>Itemized</c>) the
    /// list is the charges offered with an approved fixed rate in force; the collector selects and counts, and never prices.
    /// </summary>
    public async Task<Result<SlaughterTermsDto>> GetSlaughterTermsAsync(DateOnly? date, CancellationToken ct = default)
    {
        var tenant = municipality.MunicipalityId;
        var day = date ?? BusinessToday;
        if (!currentUser.IsAuthenticated || currentUser.Role != "Collector" || currentUser.CollectorId is not { } collector
            || tenant == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenant || day > BusinessToday
            || !await db.CollectorUsers.AsNoTracking().AnyAsync(x => x.MunicipalityId == tenant && x.Id == collector && x.IsActive
                && x.FacilityAssignments.Any(a => a.FacilityCode == FacilityCode.SLH), ct)) return Result<SlaughterTermsDto>.Forbidden();
        return await BuildTermsAsync(tenant, day, true, ct);
    }

    private async Task<Result<SlaughterTermsDto>> BuildTermsAsync(Guid tenant, DateOnly day, bool mobile, CancellationToken ct)
    {
        var service = await db.GovernedServices.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == tenant && x.OperationCode == CollectorOperationCodes.Slaughterhouse, ct);
        if (service is null) return Result<SlaughterTermsDto>.Success(new(false, []));
        var setting = GovernedServiceSetting.Resolve(await db.GovernedServiceSettings.AsNoTracking()
            .Where(x => x.MunicipalityId == tenant && x.GovernedServiceId == service.Id).ToListAsync(ct), day);
        if (setting is not { IsEnabled: true, Basis: GovernedServiceBasis.ApprovedFeeOption } || mobile && !setting.MobileEnabled)
            return Result<SlaughterTermsDto>.Success(new(false, []));
        var options = (await db.GovernedServiceFeeOptions.AsNoTracking()
            .Where(x => x.MunicipalityId == tenant && x.GovernedServiceId == service.Id).ToListAsync(ct)).Where(x => x.IsOfferedOn(day)).ToList();
        var ids = options.Select(x => x.Id).ToArray();
        var rates = (await db.GovernedServiceFeeOptionRates.AsNoTracking()
            .Where(x => x.MunicipalityId == tenant && ids.Contains(x.FeeOptionId)).ToListAsync(ct)).ToLookup(x => x.FeeOptionId);
        var charges = options.Select(o => (Option: o, Rate: GovernedServiceFeeOptionRate.Resolve(rates[o.Id], day)))
            .Where(x => x.Rate is { Basis: GovernedServiceBasis.FixedAmount })
            .OrderBy(x => x.Option.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(x => new FeeOptionTermDto(x.Option.Id, x.Option.DisplayName, x.Option.Location, x.Rate!.Basis, x.Rate.FixedAmount, null,
                x.Option.Code, x.Rate.Id, x.Rate.EffectiveDate, x.Option.ApplicableAnimals, x.Option.QuantityBasis ?? FeeQuantityBasis.PerHead)).ToList();
        return Result<SlaughterTermsDto>.Success(new(true, charges));
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
        return await PostCoreAsync(request, new Poster(tenantId, collectorId, username, actorId, "Collector", Origin, true), ct);
    }

    /// <summary>Who is posting: a facility-assigned collector (Mobile) or the office (Admin/Head) recording by approved charges. The rules are the same.</summary>
    private sealed record Poster(Guid TenantId, Guid? CollectorId, string Username, string ActorId, string Role, string Origin, bool Mobile);

    private async Task<Result<GovernedServiceOutcomeDto>> PostCoreAsync(FeeSchedulePostRequest request, Poster poster, CancellationToken ct)
    {
        var (tenantId, collectorId, username, actorId, role, origin, mobile) = poster;
        var isTabo = request.OperationCode == CollectorOperationCodes.Tabo;

        var normalized = WithCharges(JsonSerializer.Serialize(new
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
        }, JsonOptions), request);
        var fingerprint = PostingOperation.ComputeIntentFingerprint(IntentVersion, normalized, origin, actorId);

        try
        {
            var prior = await db.PostingOperations.AsNoTracking().SingleOrDefaultAsync(x =>
                x.MunicipalityId == tenantId && x.ClientOperationId == request.ClientOperationId, ct);
            if (prior is not null) return await ResolvePriorAsync(prior, fingerprint, actorId, tenantId, ct, origin);

            Task<Result<GovernedServiceOutcomeDto>> Reject(string code, string message) =>
                RejectAsync(tenantId, actorId, request, normalized, fingerprint, code, message, ct, origin);

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
            if (service is null || setting is null || !setting.IsEnabled || mobile && !setting.MobileEnabled
                || !mobile && setting.Basis != GovernedServiceBasis.ApprovedFeeOption)
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
            IReadOnlyList<ChargeLine>? charges = null;
            if (isTabo)
            {
                var tabo = await ResolveTaboAsync(tenantId, request, ct);
                if (tabo.Problem is { } problem) return await Reject(problem.Code, problem.Message);
                (amount, payer, reference, facts) = (tabo.Amount, tabo.Payer!, tabo.Reference!, tabo.Facts!);
            }
            else
            {
                var slaughter = await ResolveSlaughterAsync(tenantId, collectorId ?? Guid.Empty, username, request, service.Id,
                    setting.Basis == GovernedServiceBasis.ApprovedFeeOption, ct);
                if (slaughter.Problem is { } problem) return await Reject(problem.Code, problem.Message);
                (amount, payer, reference, facts) = (slaughter.Amount, slaughter.Payer!, slaughter.Reference!, slaughter.Facts!);
                charges = slaughter.Charges;
            }
            if (amount != request.ReceivedAmount)
                return await Reject("AMOUNT_NOT_APPROVED",
                    $"The confirmed amount is not the amount the current fee schedule gives for this transaction (PHP {amount:N2}).");
            if (setting.MaximumAmount is { } ceiling && amount > ceiling)
                return await Reject("AMOUNT_ABOVE_CEILING", "The amount exceeds the approved ceiling for this service.");

            string Snapshot(ChargeLine? charge)
            {
                var node = JsonSerializer.SerializeToNode(new
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
            }, JsonOptions)!.AsObject();
                if (charge is null) return node.ToJsonString(JsonOptions);
                // An itemized line freezes the approved charge, its quantity and the approved rate it used (the same fields the
                // fee-type drill-down reads), so a later rate change never alters a recorded receipt. Single-line snapshots are unchanged.
                node["feeOptionId"] = JsonSerializer.SerializeToNode(charge.FeeOptionId, JsonOptions);
                node["feeOptionName"] = charge.Name;
                node["feeOptionCode"] = charge.Code;
                node["feeOptionRateId"] = JsonSerializer.SerializeToNode(charge.RateId, JsonOptions);
                node["feeOptionRateEffectiveDate"] = JsonSerializer.SerializeToNode(charge.RateEffectiveDate, JsonOptions);
                node["feeOptionBasis"] = (int)GovernedServiceBasis.FixedAmount;
                node["feeOptionFixedAmount"] = charge.UnitAmount;
                node["quantity"] = charge.Quantity;
                node["unitAmount"] = charge.UnitAmount;
                return node.ToJsonString(JsonOptions);
            }
            // One canonical Collection (one SRC): a single line as before, or one line per selected approved charge.
            IReadOnlyList<CollectionLineDraft> lines = charges is { Count: > 0 }
                ? charges.Select(c => new CollectionLineDraft(classification, policy, c.Amount, CollectionSourceKind.GovernedService,
                    service.Id, null, Snapshot(c), null)).ToList()
                : [new CollectionLineDraft(classification, policy, amount, CollectionSourceKind.GovernedService,
                    service.Id, null, Snapshot(null), null)];
            var collection = await new CanonicalCollectionPostingCoordinator(db).PostAsync(
                tenantId, request.ClientOperationId, IntentVersion, normalized, origin, actorId, username, role,
                request.BusinessDate, username, lines, null, collectorId: collectorId, payerName: payer, ct: ct);
            return Result<GovernedServiceOutcomeDto>.Success(new GovernedServiceOutcomeDto(
                collection.Id, collection.ReferenceCode, collection.BusinessDate, collection.TotalAmount, instrument, "Posted", false));
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var winner = await db.PostingOperations.AsNoTracking().SingleOrDefaultAsync(x =>
                x.MunicipalityId == tenantId && x.ClientOperationId == request.ClientOperationId, ct);
            if (winner is not null) return await ResolvePriorAsync(winner, fingerprint, actorId, tenantId, ct, origin);
            return await RejectAsync(tenantId, actorId, request, normalized, fingerprint, "POST_CONFLICT",
                "The operation identity conflicts with another saved transaction; nothing was posted.", ct, origin);
        }
    }

    private sealed record Problem(string Code, string Message);
    private sealed record ChargeLine(Guid FeeOptionId, string Name, string? Code, decimal Quantity, decimal UnitAmount, decimal Amount,
        Guid RateId, DateOnly RateEffectiveDate);

    private sealed record Resolved(decimal Amount, string? Payer, string? Reference, object? Facts, Problem? Problem,
        IReadOnlyList<ChargeLine>? Charges = null)
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
        Guid tenantId, Guid collectorId, string username, FeeSchedulePostRequest request, Guid serviceId, bool itemized, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.OwnerName) || request.OwnerName.Trim().Length > 200)
            return Resolved.Fail("INVALID_INTENT", "The owner name is required.");
        if (request.Heads is not > 0 or > 1000 || request.Animal is not { } animal || !Enum.IsDefined(animal))
            return Resolved.Fail("INVALID_INTENT", "An approved animal and a head count are required.");
        var facility = await db.Facilities.AsNoTracking().SingleOrDefaultAsync(x =>
            x.MunicipalityId == tenantId && x.Code == FacilityCode.SLH, ct);
        if (facility is null) return Resolved.Fail("SOURCE_NOT_FOUND", "The slaughterhouse facility is not available in this tenant.");

        // Itemized mode (the Head has the service on approved charges): the total is the sum of the selected charges at their approved
        // rates. The per-head total below is the unchanged legacy calculation and is never read in this mode, nor are its rows reinterpreted.
        if (itemized) return await ResolveItemizedSlaughterAsync(tenantId, serviceId, request, animal, request.Heads!.Value, ct);
        if (request.Charges is { Count: > 0 })
            return Resolved.Fail("CHARGES_NOT_ACTIVE", "Approved charges apply only once the office collects Slaughterhouse by approved charges.");

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
            if (approved.RatePerHead <= 0m)
                return Resolved.Fail("RATE_NOT_STATED", $"'{approved.AnimalName}' has no per-head rate; the office collects it by approved charges.");
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

    private async Task<Resolved> ResolveItemizedSlaughterAsync(
        Guid tenantId, Guid serviceId, FeeSchedulePostRequest request, AnimalType animal, int heads, CancellationToken ct)
    {
        string? approvedName = null;
        if (animal == AnimalType.Other)
        {
            var wanted = request.CustomAnimalName?.Trim();
            var approved = await db.SlaughterAnimalRates.AsNoTracking()
                .FirstOrDefaultAsync(x => x.MunicipalityId == tenantId && x.IsActive && x.AnimalName.ToUpper() == (wanted ?? "").ToUpper(), ct);
            if (approved is null)
                return Resolved.Fail("ANIMAL_NOT_APPROVED", $"'{wanted}' is not an approved slaughter animal.");
            approvedName = approved.AnimalName;
        }
        var selections = request.Charges ?? [];
        if (selections.Count == 0)
            return Resolved.Fail("CHARGES_REQUIRED", "Select the approved charges for this transaction.");
        var ids = selections.Select(x => x.FeeOptionId).Distinct().ToArray();
        if (selections.Count > 20 || ids.Length != selections.Count || ids.Any(x => x == Guid.Empty))
            return Resolved.Fail("INVALID_INTENT", "Each approved charge may be selected once.");

        var options = await db.GovernedServiceFeeOptions.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && x.GovernedServiceId == serviceId && ids.Contains(x.Id)).ToListAsync(ct);
        if (options.Count != ids.Length)
            return Resolved.Fail("CHARGE_UNKNOWN", "A selected charge is not an approved Slaughterhouse charge.");
        var rates = (await db.GovernedServiceFeeOptionRates.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && ids.Contains(x.FeeOptionId)).ToListAsync(ct)).ToLookup(x => x.FeeOptionId);

        var lines = new List<ChargeLine>();
        foreach (var selection in selections.OrderBy(x => options.First(o => o.Id == x.FeeOptionId).DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var option = options.First(o => o.Id == selection.FeeOptionId);
            if (!option.IsOfferedOn(request.BusinessDate))
                return Resolved.Fail("CHARGE_NOT_OFFERED", $"'{option.DisplayName}' is not offered on the business date.");
            if (!SlaughterAnimalScopes.Includes(option.ApplicableAnimals, animal))
                return Resolved.Fail("CHARGE_NOT_APPLICABLE", $"'{option.DisplayName}' does not apply to this animal.");
            var rate = GovernedServiceFeeOptionRate.Resolve(rates[option.Id], request.BusinessDate);
            if (rate is not { Basis: GovernedServiceBasis.FixedAmount, FixedAmount: { } unit })
                return Resolved.Fail("RATE_NOT_STATED", $"'{option.DisplayName}' has no approved rate in force on the business date.");
            var perHead = (option.QuantityBasis ?? FeeQuantityBasis.PerHead) == FeeQuantityBasis.PerHead;
            if (perHead ? selection.Quantity != heads : selection.Quantity is < 1 or > 100_000)
                return Resolved.Fail("INVALID_INTENT", perHead
                    ? $"'{option.DisplayName}' is charged per head: its quantity is the head count."
                    : $"A quantity of 1 to 100,000 is required for '{option.DisplayName}'.");
            lines.Add(new ChargeLine(option.Id, option.DisplayName, option.Code, selection.Quantity, unit, unit * selection.Quantity, rate.Id, rate.EffectiveDate));
        }

        var owner = PersonName.Canonical(request.OwnerName!);
        var label = approvedName ?? animal.ToString();
        var total = lines.Sum(x => x.Amount);
        return new Resolved(total, owner, $"{heads} {label} · {lines.Count} charge{(lines.Count == 1 ? "" : "s")}", new
        {
            kind = "Slaughterhouse", ownerName = owner, animal = animal.ToString(), customAnimalName = approvedName,
            heads, itemized = true, total
        }, null, lines);
    }

    /// <summary>The selected charges are part of the intent only when stated, so every earlier intent keeps its exact fingerprint.</summary>
    private static string WithCharges(string normalized, FeeSchedulePostRequest request)
    {
        if (request.Charges is not { Count: > 0 } charges) return normalized;
        var node = JsonNode.Parse(normalized)!.AsObject();
        node["Charges"] = JsonSerializer.SerializeToNode(
            charges.OrderBy(x => x.FeeOptionId).Select(x => new { FeeOptionId = x.FeeOptionId.ToString("D"), x.Quantity }), JsonOptions);
        return node.ToJsonString(JsonOptions);
    }

    // ── Idempotency and durable rejection ────────────────────────────────────────────────────────────────

    private async Task<Result<GovernedServiceOutcomeDto>> ResolvePriorAsync(
        PostingOperation prior, string fingerprint, string actorId, Guid tenantId, CancellationToken ct, string? origin = null)
    {
        if (prior.Origin != (origin ?? Origin) || prior.ActorId != actorId || prior.IntentFingerprint != fingerprint)
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
        string code, string message, CancellationToken ct, string? origin = null)
    {
        db.ChangeTracker.Clear();
        db.PostingOperations.Add(PostingOperation.Record(tenantId, request.ClientOperationId, IntentVersion, normalized, origin ?? Origin,
            actorId, PostingOperationStatus.Rejected, code, message, null, null, DateTime.UtcNow));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var prior = await db.PostingOperations.AsNoTracking().SingleOrDefaultAsync(x =>
                x.MunicipalityId == tenantId && x.ClientOperationId == request.ClientOperationId, ct);
            if (prior is not null) return await ResolvePriorAsync(prior, fingerprint, actorId, tenantId, ct, origin);
            throw;
        }
        return Result<GovernedServiceOutcomeDto>.Failure(message, ResultStatus.Conflict);
    }
}

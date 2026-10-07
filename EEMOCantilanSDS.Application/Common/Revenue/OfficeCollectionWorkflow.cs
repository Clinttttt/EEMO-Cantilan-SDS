using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>IA-067/068 source facts delegate money to the shared canonical coordinator. No legacy projections.</summary>
public sealed partial class OfficeCollectionWorkflow(IAppDbContext db, ICurrentUserService user,
    ICurrentMunicipalityAccessor tenant, IClock clock, IFeeRateResolver rates)
{
    public static readonly DateOnly EffectiveFrom = new(2026, 10, 6);
    private Guid Tenant => tenant.MunicipalityId;
    private bool TenantValid => Tenant != Guid.Empty && (!user.MunicipalityId.HasValue || user.MunicipalityId == Tenant);
    private const string Origin = "SourceNativeOfficeCollection";
    public static string SectionCode(TerminalSection section) => section switch {
        TerminalSection.ComfortRoom => RevenueClassificationCodes.TerminalComfortRoom,
        TerminalSection.PullPulVansCargoVans => RevenueClassificationCodes.TerminalPullPulVansCargoVans,
        TerminalSection.Tricycad => RevenueClassificationCodes.TerminalTricycad,
        _ => throw new ArgumentException("InvalidTerminalSection") };
    public static string SectionName(TerminalSection section) => section switch {
        TerminalSection.ComfortRoom => "COMFORT ROOM", TerminalSection.PullPulVansCargoVans => "PULL PUL VANS, CARGO VANS",
        TerminalSection.Tricycad => "TRICYCAD", _ => throw new ArgumentException("InvalidTerminalSection") };
    public async Task<bool> IsAssignedAsync(string operation, CancellationToken ct) => TenantValid && user.IsAuthenticated && user.Role == "Collector"
        && user.CollectorId is { } collector && await db.CollectorUsers.AsNoTracking().AnyAsync(x => x.MunicipalityId == Tenant && x.Id == collector && x.IsActive, ct)
        && await db.CollectorOperationAssignments.AsNoTracking().AnyAsync(x => x.MunicipalityId == Tenant && x.CollectorId == collector && x.OperationCode == operation, ct);
    public async Task<Result<FishMeatVendorRegistrationDto>> RegisterAsync(RegisterFishMeatVendorRequest request, CancellationToken ct = default)
    {
        if (!TenantValid || !user.IsAuthenticated || user.Role != "SuperAdmin") return Result<FishMeatVendorRegistrationDto>.Forbidden();
        FishMeatVendorRegistration candidate;
        try { candidate = FishMeatVendorRegistration.Register(Tenant, request.ClientOperationId, request.TaxYear, request.VendorType,
            request.RegistrationKind, request.DisplayName, request.BusinessName, request.Address, request.Reference, user.Username ?? "Head"); }
        catch (ArgumentException e) { return Result<FishMeatVendorRegistrationDto>.Failure(e.Message, ResultStatus.Invalid); }
        var prior = await db.FishMeatVendorRegistrations.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.ClientOperationId == request.ClientOperationId, ct);
        if (prior is not null) return ToDto(prior) == ToDto(candidate) with { Id = prior.Id }
            ? Result<FishMeatVendorRegistrationDto>.Success(ToDto(prior))
            : Result<FishMeatVendorRegistrationDto>.Failure("RegistrationIntentConflict", ResultStatus.Conflict);
        db.FishMeatVendorRegistrations.Add(candidate);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var winner = await db.FishMeatVendorRegistrations.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.ClientOperationId == request.ClientOperationId, ct);
            return winner is not null && ToDto(winner) == ToDto(candidate) with { Id = winner.Id }
                ? Result<FishMeatVendorRegistrationDto>.Success(ToDto(winner))
                : Result<FishMeatVendorRegistrationDto>.Failure("RegistrationIntentConflict", ResultStatus.Conflict);
        }
        return Result<FishMeatVendorRegistrationDto>.Success(ToDto(candidate));
    }
    public async Task<Result<IReadOnlyList<FishMeatVendorRegistrationDto>>> RegistrationsAsync(int year, CancellationToken ct = default)
    {
        if (!TenantValid || !user.IsAuthenticated || user.Role is not ("SuperAdmin" or "Admin" or "Collector")) return Result<IReadOnlyList<FishMeatVendorRegistrationDto>>.Forbidden();
        if (user.Role == "Collector" && !await IsAssignedAsync(CollectorOperationCodes.FishMeatVendorFee, ct)
            && !await IsAssignedAsync(CollectorOperationCodes.WeightAndMeasure, ct))
            return Result<IReadOnlyList<FishMeatVendorRegistrationDto>>.Forbidden();
        if (year is < 2000 or > 2200) return Result<IReadOnlyList<FishMeatVendorRegistrationDto>>.Failure("InvalidPeriod", ResultStatus.Invalid);
        return Result<IReadOnlyList<FishMeatVendorRegistrationDto>>.Success((await db.FishMeatVendorRegistrations.AsNoTracking()
            .Where(x => x.MunicipalityId == Tenant && x.TaxYear == year).OrderBy(x => x.DisplayName).ThenBy(x => x.Id).ToListAsync(ct)).Select(ToDto).ToArray());
    }
    public static FishMeatVendorRegistrationDto ToDto(FishMeatVendorRegistration x) => new(x.Id, x.TaxYear, x.VendorType, x.RegistrationKind, x.DisplayName, x.BusinessName, x.Address, x.Reference);
    public async Task<Result<bool>> MapVehicleAsync(TerminalVehicleMappingRequest request, CancellationToken ct = default)
    {
        if (!TenantValid || !user.IsAuthenticated || user.Role != "SuperAdmin") return Result<bool>.Forbidden();
        var vehicle = await db.VehicleClasses.SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.Id == request.VehicleClassId, ct);
        if (vehicle is null) return Result<bool>.NotFound();
        try { vehicle.AssociateTerminalSection(request.Section); }
        catch (ArgumentException e) { return Result<bool>.Failure(e.Message, ResultStatus.Invalid); }
        await db.SaveChangesAsync(ct); return Result<bool>.Success(true);
    }
    public async Task<IReadOnlyList<TerminalVehicleChoice>> VehicleChoicesAsync(DateOnly date, CancellationToken ct)
    {
        if (!TenantValid || !user.IsAuthenticated || user.Role is not ("SuperAdmin" or "Admin" or "Collector")) return [];
        if (user.Role == "Collector" && !await IsAssignedAsync(CollectorOperationCodes.Terminal, ct)) return [];
        var vehicles = (await db.VehicleClasses.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.IsActive).ToListAsync(ct)).Where(x => x.EffectiveTerminalSection != null).ToList();
        var ids = vehicles.Select(x => x.Id).ToArray();
        var versions = (await db.VehicleClassRates.AsNoTracking().Where(x => x.MunicipalityId == Tenant && ids.Contains(x.VehicleClassId)).ToListAsync(ct)).ToLookup(x => x.VehicleClassId);
        return vehicles.Select(v => (v, rate: VehicleClassRate.Resolve(versions[v.Id], date))).Where(x => x.rate is not null)
            .Select(x => new TerminalVehicleChoice(x.v.Id, x.v.Code, x.v.DisplayName, x.v.EffectiveTerminalSection!.Value, x.rate!.Amount, x.rate.Id, x.rate.EffectiveDate)).ToArray();
    }
    private sealed record Facts(SourceNativeChargeQuote Quote, RevenueClassification Classification, RevenueClassificationPolicy Policy,
        Guid? SourceId, string? Payer, string Snapshot);
    private async Task<(Facts? Facts, string? Error)> ResolveAsync(SourceNativeCollectionRequest request, CancellationToken ct)
    {
        if (request.Charge is null) return (null, "InvalidIntent");
        if (request.BusinessDate < EffectiveFrom || request.BusinessDate > clock.PhilippineToday) return (null, "InvalidBusinessDate");
        if (!await IsAssignedAsync(request.Charge.OperationCode, ct)) return (null, "CollectorNotAssigned");
        var charge = request.Charge;
        string code, label; var amount = request.AmountReceived; string? payer = request.PayerSnapshot?.Trim();
        Guid? source = null; object? calculation = null; Guid? rateId = null; DateOnly? rateDate = null;
        decimal? approvedRate = null; FishMeatVendorType? vendorType = null;
        if (payer?.Length > 200) return (null, "InvalidPayerSnapshot");
        if (charge.OperationCode == CollectorOperationCodes.Terminal)
        {
            if (charge.Section is not { } section || !Enum.IsDefined(section) || charge.CashTicketCount is < 0 || charge.VendorRegistrationId.HasValue || charge.Kilograms.HasValue)
                return (null, "InvalidTerminalSection");
            code = SectionCode(section); label = SectionName(section);
            if (charge.VehicleClassId is { } vehicleId)
            {
                var vehicle = (await VehicleChoicesAsync(request.BusinessDate, ct)).SingleOrDefault(x => x.VehicleClassId == vehicleId && x.Section == section);
                if (vehicle is null) return (null, "VehicleClassNotAvailable");
                calculation = vehicle; // Aid only: the final received amount is authoritative, never ticket-count × rate.
                rateId = vehicle.RateId; rateDate = vehicle.EffectiveDate; approvedRate = vehicle.Rate;
            }
        }
        else if (charge.OperationCode is CollectorOperationCodes.FishMeatVendorFee or CollectorOperationCodes.WeightAndMeasure)
        {
            if (charge.Section.HasValue || charge.VehicleClassId.HasValue || charge.CashTicketCount.HasValue) return (null, "InvalidIntent");
            var vendor = charge.VendorRegistrationId is { } id ? await db.FishMeatVendorRegistrations.AsNoTracking().SingleOrDefaultAsync(x =>
                x.MunicipalityId == Tenant && x.Id == id && x.TaxYear == request.BusinessDate.Year, ct) : null;
            if (charge.VendorRegistrationId.HasValue && vendor is null) return (null, "InvalidSource");
            if (vendor is not null) { source = vendor.Id; payer = vendor.DisplayName; vendorType = vendor.VendorType; }
            if (charge.OperationCode == CollectorOperationCodes.FishMeatVendorFee)
            {
                if (charge.Kilograms.HasValue) return (null, "InvalidIntent");
                if (string.IsNullOrWhiteSpace(payer)) return (null, "RequiresPayerSnapshot");
                code = RevenueClassificationCodes.FishMeatVendorFee; label = "Fish / Meat Vendor Fee";
                calculation = vendor is null ? null : ToDto(vendor);
            }
            else
            {
                if (vendor is null) return (null, "RequiresRegisteredVendor");
                if (charge.Kilograms is not { } kg || kg <= 0m || kg > 1_000_000m || decimal.Round(kg, 2) != kg) return (null, "InvalidQuantity");
                var key = vendor.VendorType == FishMeatVendorType.Fish ? FeeRateKey.NpmFishPerKilo : FeeRateKey.NpmMeatPerKilo;
                var entry = (await rates.GetSnapshotAsync(ct)).ResolveEntryOrNull(key, request.BusinessDate);
                if (entry is not { Amount: > 0m }) return (null, "RateNotEffective");
                var rate = await db.FacilityRates.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.FacilityCode == entry.Value.Facility && x.RateKey == key && x.EffectiveDate == entry.Value.EffectiveDate, ct);
                if (rate is null) return (null, "RateNotEffective");
                amount = decimal.Round(kg * rate.Amount, 2, MidpointRounding.AwayFromZero);
                rateId = rate.Id; rateDate = rate.EffectiveDate; approvedRate = rate.Amount;
                calculation = new { Vendor = ToDto(vendor), Kilograms = kg, RateId = rate.Id, Rate = rate.Amount, RateEffectiveDate = rate.EffectiveDate, rate.UpdatedAt };
                code = RevenueClassificationCodes.WeightAndMeasure; label = "Weight & Measure";
            }
        }
        else return (null, "NotSupportedYet");
        if (amount <= 0m || amount > Collection.MaximumMoneyAmount || decimal.Round(amount, 2) != amount) return (null, "InvalidAmount");
        var classification = await db.RevenueClassifications.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.SemanticCode == code && x.IsActive, ct);
        var policy = classification is null ? null : await db.RevenueClassificationPolicies.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.RevenueClassificationId == classification.Id &&
            x.BusinessContext == RevenuePolicyContext.Default && x.EffectiveDate <= request.BusinessDate).OrderByDescending(x => x.EffectiveDate).FirstOrDefaultAsync(ct);
        var instrument = charge.OperationCode == CollectorOperationCodes.Terminal ? RevenueInstrumentType.CashTicket : RevenueInstrumentType.OfficialReceipt;
        if (policy?.PermittedInstrumentType != instrument) return (null, "PolicyNotEffective");
        var snapshot = JsonSerializer.Serialize(new { Version = 1, SourceIdentity = source, PayerSnapshot = payer, charge, Calculation = calculation,
            Amount = amount, PolicyId = policy.Id, Instrument = instrument, EffectiveFrom });
        var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot)));
        return (new(new(charge.OperationCode, label, payer ?? label, instrument, amount, version, charge,
            rateId, rateDate, approvedRate, vendorType), classification!, policy, source, payer, snapshot), null);
    }
    public async Task<Result<SourceNativeChargeQuote>> QuoteAsync(SourceNativeCollectionRequest request, CancellationToken ct = default)
    {
        var resolved = await ResolveAsync(request, ct);
        return resolved.Facts is null ? Result<SourceNativeChargeQuote>.Failure(resolved.Error!, ResultStatus.Invalid) : Result<SourceNativeChargeQuote>.Success(resolved.Facts.Quote);
    }
    public async Task<Result<GovernedServiceOutcomeDto>> PostAsync(SourceNativeCollectionRequest request, CancellationToken ct = default)
    {
        var outerTransaction = db.HasActiveTransaction;
        try { return await PostCoreAsync(request, ct); }
        catch (DbUpdateException) when (!outerTransaction)
        {
            // The owned transaction has disposed/rolled back. Resolve a concurrent winning operation, never generate new intent.
            db.ChangeTracker.Clear();
            if (await db.PostingOperations.AsNoTracking().AnyAsync(x => x.MunicipalityId == Tenant && x.ClientOperationId == request.ClientOperationId, ct))
                return await PostCoreAsync(request, ct);
            return Result<GovernedServiceOutcomeDto>.Failure("RetrySession", ResultStatus.Conflict);
        }
    }
    private async Task<Result<GovernedServiceOutcomeDto>> PostCoreAsync(SourceNativeCollectionRequest request, CancellationToken ct)
    {
        if (request.Charge is null) return Result<GovernedServiceOutcomeDto>.Failure("InvalidIntent", ResultStatus.Invalid);
        if (!await IsAssignedAsync(request.Charge.OperationCode, ct)) return Result<GovernedServiceOutcomeDto>.Forbidden();
        if (request.ClientOperationId == Guid.Empty) return Result<GovernedServiceOutcomeDto>.Failure("InvalidIntent", ResultStatus.Invalid);
        var normalized = JsonSerializer.Serialize(new { Version = 1, request.BusinessDate, Amount = request.AmountReceived.ToString("0.00##########################", System.Globalization.CultureInfo.InvariantCulture), request.Charge, PayerSnapshot = request.PayerSnapshot?.Trim(), request.ExpectedSourceVersion });
        var actor = user.CollectorId!.Value.ToString("N");
        var fingerprint = PostingOperation.ComputeIntentFingerprint(1, normalized, Origin, actor);
        var prior = await db.PostingOperations.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.ClientOperationId == request.ClientOperationId, ct);
        if (prior is not null)
        {
            if (prior.Origin != Origin || prior.ActorId != actor || prior.IntentFingerprint != fingerprint || prior.CollectionId is null)
                return Result<GovernedServiceOutcomeDto>.Failure("CollectionIntentConflict", ResultStatus.Conflict);
            var recorded = await db.Collections.AsNoTracking().SingleAsync(x => x.MunicipalityId == Tenant && x.Id == prior.CollectionId, ct);
            var instrument = request.Charge.OperationCode == CollectorOperationCodes.Terminal ? RevenueInstrumentType.CashTicket : RevenueInstrumentType.OfficialReceipt;
            var corrected = await db.CollectionCorrections.AsNoTracking().AnyAsync(x => x.MunicipalityId == Tenant && x.OriginalCollectionId == recorded.Id && x.FinancialEffectAmount < 0m, ct);
            return Result<GovernedServiceOutcomeDto>.Success(new(recorded.Id, recorded.ReferenceCode, recorded.BusinessDate, recorded.TotalAmount, instrument, corrected ? "Reversed" : "Posted", true));
        }
        await using var tx = db.HasActiveTransaction ? null : await db.BeginSerializableTransactionAsync(ct);
        var resolved = await ResolveAsync(request, ct);
        if (resolved.Facts is not { } f) return Result<GovernedServiceOutcomeDto>.Failure(resolved.Error!, ResultStatus.Invalid);
        if (request.ExpectedSourceVersion is not null && request.ExpectedSourceVersion != f.Quote.SourceVersion)
            return Result<GovernedServiceOutcomeDto>.Failure("QuoteStale", ResultStatus.Conflict);
        if (request.Charge.OperationCode == CollectorOperationCodes.WeightAndMeasure && request.AmountReceived != f.Quote.Amount)
            return Result<GovernedServiceOutcomeDto>.Failure("AmountChanged", ResultStatus.Conflict);
        CollectionSourceKind? kind = request.Charge.OperationCode == CollectorOperationCodes.Terminal ? CollectionSourceKind.TerminalSection
            : f.SourceId.HasValue ? CollectionSourceKind.FishMeatVendorRegistration : null;
        var collection = await new CanonicalCollectionPostingCoordinator(db).PostAsync(Tenant, request.ClientOperationId, 1, normalized, Origin, actor,
            user.Username ?? "Collector", "Collector", request.BusinessDate, user.Username ?? "Collector",
            [new(f.Classification, f.Policy, f.Quote.Amount, kind, kind == CollectionSourceKind.TerminalSection ? f.Classification.Id : f.SourceId, null, f.Snapshot)],
            collectorId: user.CollectorId, payerName: f.Payer, ct: ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return Result<GovernedServiceOutcomeDto>.Success(new(collection.Id, collection.ReferenceCode, collection.BusinessDate, collection.TotalAmount, f.Quote.Instrument, "Posted", false));
    }
}
